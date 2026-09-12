using System.Net;
using System.Net.Sockets;
using System.Text;
using JobForge.Application.Common;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobForge.Infrastructure.Execution;

/// <summary>
/// Executes the single supported job type: an HTTP request. All failure modes
/// (timeout, DNS failure, connection refused, non-2xx status, unexpected
/// exception) are caught here and converted into a structured
/// <see cref="JobExecutionOutcome"/> — this method never throws, so the worker
/// loop that calls it never has to guess what went wrong.
/// </summary>
public sealed class HttpJobExecutor : IJobExecutor
{
    public const int MaxResponseBodyChars = 8_000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpJobExecutor> _logger;

    public HttpJobExecutor(IHttpClientFactory httpClientFactory, ILogger<HttpJobExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<JobExecutionOutcome> ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(job);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(job.TimeoutSeconds));

        var client = _httpClientFactory.CreateClient(SsrfSafeHttpClient.Name);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
            var body = await ReadTruncatedAsync(response.Content, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (statusCode is >= 200 and < 400)
            {
                return JobExecutionOutcome.Ok(statusCode, body);
            }

            return JobExecutionOutcome.Failure(
                $"Target returned HTTP {statusCode} {response.ReasonPhrase}",
                isRetryable: RetryPolicy.IsRetryableStatusCode(statusCode),
                httpStatusCode: statusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JobExecutionOutcome.Failure($"Request timed out after {job.TimeoutSeconds}s", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            // Covers DNS failure, connection refused, TLS failure, and the SSRF guard's own rejection.
            return JobExecutionOutcome.Failure($"Network error: {ex.Message}", isRetryable: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception executing jobId={JobId}", job.Id);
            return JobExecutionOutcome.Failure($"Unexpected error: {ex.Message}", isRetryable: true);
        }
    }

    private static HttpRequestMessage BuildRequest(Job job)
    {
        var method = job.HttpMethod switch
        {
            HttpMethodType.GET => HttpMethod.Get,
            HttpMethodType.POST => HttpMethod.Post,
            HttpMethodType.PUT => HttpMethod.Put,
            HttpMethodType.PATCH => HttpMethod.Patch,
            HttpMethodType.DELETE => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(job), job.HttpMethod, "Unsupported HTTP method.")
        };

        var request = new HttpRequestMessage(method, job.Url);

        string? contentType = null;
        if (job.Headers is not null)
        {
            foreach (var (key, value) in job.Headers)
            {
                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = value;
                    continue;
                }
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        if (!string.IsNullOrEmpty(job.Body) && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(job.Body, Encoding.UTF8, contentType ?? "application/json");
        }

        return request;
    }

    private static async Task<string?> ReadTruncatedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var text = await content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrEmpty(text)) return null;

        return text.Length > MaxResponseBodyChars
            ? text[..MaxResponseBodyChars] + "...[truncated]"
            : text;
    }
}

/// <summary>
/// Registers the named HttpClient used for all job executions, wired with a
/// ConnectCallback that resolves DNS itself and refuses to connect to a
/// loopback/private/link-local address — this runs on every connection,
/// including ones opened for a redirect, so it isn't bypassed by a 3xx pointing
/// at an internal address after the initial URL passed validation.
/// </summary>
public static class SsrfSafeHttpClient
{
    public const string Name = "JobExecutor";

    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var target = addresses.FirstOrDefault(a => !SsrfGuard.IsBlockedAddress(a));

            if (target is null)
            {
                throw new HttpRequestException(
                    $"Refusing to connect to '{context.DnsEndPoint.Host}': resolves only to blocked internal/private addresses.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };
}

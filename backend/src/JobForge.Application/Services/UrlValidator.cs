using System.Net;
using JobForge.Application.Common;
using JobForge.Domain.Exceptions;

namespace JobForge.Application.Services;

public interface IUrlValidator
{
    /// <summary>Throws ValidationException if the URL is malformed, uses a disallowed scheme, or is a literal private/loopback address.</summary>
    void ValidateJobUrl(string url);
}

public sealed class UrlValidator : IUrlValidator
{
    public void ValidateJobUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ValidationException("URL must be a valid absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidationException("Only http and https URLs are supported.");
        }

        if (SsrfGuard.IsBlockedHostname(uri.Host))
        {
            throw new ValidationException("URLs targeting localhost are not allowed.");
        }

        if (IPAddress.TryParse(uri.Host, out var literalIp) && SsrfGuard.IsBlockedAddress(literalIp))
        {
            throw new ValidationException("URLs targeting private or internal network addresses are not allowed.");
        }
    }
}

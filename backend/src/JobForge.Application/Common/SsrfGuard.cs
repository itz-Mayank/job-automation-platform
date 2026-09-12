using System.Net;
using System.Net.Sockets;

namespace JobForge.Application.Common;

/// <summary>
/// Best-effort SSRF guard for a "jobs execute arbitrary URLs" feature. Blocks loopback,
/// private, link-local, and cloud-metadata address ranges. Used twice: at job
/// create/update time (literal-IP hostnames only, no DNS) and again in the HTTP
/// executor right before each request (after resolving the hostname), so a
/// hostname that later repoints to a private address is still caught.
///
/// This is deliberately not a complete SSRF-prevention system: it does not
/// recheck the resolved address on redirects, and DNS resolution happening a few
/// milliseconds before connect is a (small, accepted) TOCTOU window. See
/// ENGINEERING.md "Security" for the documented limitation.
/// </summary>
public static class SsrfGuard
{
    private static readonly string[] BlockedHostnames =
    {
        "localhost", "localhost.localdomain", "metadata.google.internal"
    };

    public static bool IsBlockedHostname(string host) =>
        BlockedHostnames.Contains(host.Trim().ToLowerInvariant());

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (link-local, includes cloud metadata endpoints)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 0.0.0.0/8
            if (bytes[0] == 0) return true;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
            // fc00::/7 unique local
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }
}

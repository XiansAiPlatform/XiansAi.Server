using System.Net;
using System.Net.Sockets;

namespace Shared.Utils;

/// <summary>
/// Provides URL validation to prevent Server-Side Request Forgery (SSRF) attacks.
/// Validates that URLs don't point to internal networks, localhost, or cloud metadata endpoints.
/// </summary>
public static class UrlValidator
{
    // Private IP ranges (RFC 1918)
    private static readonly List<(IPAddress start, IPAddress end)> PrivateRanges = new()
    {
        (IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255")),
        (IPAddress.Parse("172.16.0.0"), IPAddress.Parse("172.31.255.255")),
        (IPAddress.Parse("192.168.0.0"), IPAddress.Parse("192.168.255.255")),
        (IPAddress.Parse("127.0.0.0"), IPAddress.Parse("127.255.255.255")), // Loopback
        (IPAddress.Parse("169.254.0.0"), IPAddress.Parse("169.254.255.255")), // Link-local (includes AWS metadata)
        (IPAddress.Parse("fc00::"), IPAddress.Parse("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")), // IPv6 private
        (IPAddress.Parse("fe80::"), IPAddress.Parse("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff")), // IPv6 link-local
    };

    // Blocked hostnames
    private static readonly HashSet<string> BlockedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "metadata.google.internal", // GCP metadata
        "169.254.169.254", // AWS/Azure/Oracle metadata endpoint
    };

    /// <summary>
    /// Validates a URL to ensure it's safe for webhook callbacks and doesn't pose SSRF risks.
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <param name="errorMessage">Output parameter containing the error message if validation fails</param>
    /// <returns>True if the URL is safe, false otherwise</returns>
    public static bool IsValidWebhookUrl(string url, out string errorMessage)
    {
        errorMessage = string.Empty;

        // Check if URL is well-formed
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errorMessage = "Invalid URL format";
            return false;
        }

        // Only allow HTTP and HTTPS schemes
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = "Only HTTP and HTTPS schemes are allowed";
            return false;
        }

        // Check for blocked hostnames
        if (BlockedHostnames.Contains(uri.Host))
        {
            errorMessage = $"Hostname '{uri.Host}' is not allowed for security reasons";
            return false;
        }

        // Check if hostname is an IP address
        if (IPAddress.TryParse(uri.Host, out var ipAddress))
        {
            if (!IsPublicIpAddress(ipAddress))
            {
                errorMessage = "IP addresses in private ranges, localhost, or link-local ranges are not allowed";
                return false;
            }
        }
        else
        {
            // Resolve hostname to IP addresses
            try
            {
                var hostEntry = Dns.GetHostEntry(uri.Host);
                foreach (var address in hostEntry.AddressList)
                {
                    if (!IsPublicIpAddress(address))
                    {
                        errorMessage = $"Hostname '{uri.Host}' resolves to a private or internal IP address";
                        return false;
                    }
                }
            }
            catch (SocketException)
            {
                errorMessage = $"Unable to resolve hostname '{uri.Host}'";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error validating hostname: {ex.Message}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if an IP address is a public IP (not private, loopback, or link-local).
    /// </summary>
    private static bool IsPublicIpAddress(IPAddress address)
    {
        // Check IPv4 loopback and any
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        // Check against private ranges
        foreach (var (start, end) in PrivateRanges)
        {
            if (IsInRange(address, start, end))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if an IP address is within a given range.
    /// </summary>
    private static bool IsInRange(IPAddress address, IPAddress start, IPAddress end)
    {
        // Convert to same address family if needed
        if (address.AddressFamily != start.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var startBytes = start.GetAddressBytes();
        var endBytes = end.GetAddressBytes();

        if (addressBytes.Length != startBytes.Length || addressBytes.Length != endBytes.Length)
        {
            return false;
        }

        // Check if address is >= start
        bool afterStart = true;
        for (int i = 0; i < addressBytes.Length; i++)
        {
            if (addressBytes[i] < startBytes[i])
            {
                afterStart = false;
                break;
            }
            if (addressBytes[i] > startBytes[i])
            {
                break;
            }
        }

        // Check if address is <= end
        bool beforeEnd = true;
        for (int i = 0; i < addressBytes.Length; i++)
        {
            if (addressBytes[i] > endBytes[i])
            {
                beforeEnd = false;
                break;
            }
            if (addressBytes[i] < endBytes[i])
            {
                break;
            }
        }

        return afterStart && beforeEnd;
    }
}


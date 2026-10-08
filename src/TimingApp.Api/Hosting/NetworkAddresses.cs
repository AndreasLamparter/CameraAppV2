using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TimingApp.Api.Hosting;

/// <summary>Base URLs under which other computers (e.g. the timing system) reach this application.</summary>
internal static class NetworkAddresses
{
    /// <summary>
    /// The listening addresses with a wildcard host (<c>0.0.0.0</c>, <c>[::]</c>, <c>*</c>, <c>+</c>) expanded to
    /// the given local IPv4 addresses; specific hosts are kept. Distinct, in the given order.
    /// </summary>
    public static IReadOnlyList<string> BaseUrls(IEnumerable<string> listening, IEnumerable<IPAddress> local)
    {
        var addresses = local.ToList();
        var result = new List<string>();
        foreach (var address in listening)
        {
            var normalized = address.Replace("://+", "://0.0.0.0", StringComparison.Ordinal).Replace("://*", "://0.0.0.0", StringComparison.Ordinal);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                continue;
            }
            var wildcard = uri.Host is "0.0.0.0" or "[::]" or "::";
            var hosts = wildcard ? addresses.Select(a => a.ToString()) : [uri.Host];
            result.AddRange(hosts.Select(host => $"{uri.Scheme}://{host}:{uri.Port}"));
        }
        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>IPv4 addresses of the network interfaces that are up, excluding loopback.</summary>
    public static IEnumerable<IPAddress> LocalIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
}

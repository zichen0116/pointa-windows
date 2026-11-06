using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pointa.Windows;

internal static class NetworkPolicy
{
    public static IReadOnlyList<IPAddress> GetAddresses()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet)
            .Select(adapter => new { Adapter = adapter, Properties = adapter.GetIPProperties() })
            .Where(item => item.Adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                item.Properties.GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any)))
            .OrderBy(item => item.Adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .ThenBy(item => item.Properties.GatewayAddresses.Count == 0 ? 1 : 0);
        // Public is common on home Wi-Fi; TLS, pairing approval and subnet firewall rules protect this listener.
        return new[] { IPAddress.Loopback }.Concat(adapters.SelectMany(item => item.Properties.UnicastAddresses)
            .Select(item => item.Address).Where(IsLanV4)).Distinct().ToArray();
    }

    internal static bool IsLanV4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}

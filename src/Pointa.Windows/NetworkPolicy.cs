using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Pointa.Windows;

internal static class NetworkPolicy
{
    public static IReadOnlyList<IPAddress> GetAddresses(bool allowPublic)
    {
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? manager = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), throwOnError: true)!;
            manager = Activator.CreateInstance(type)!;
            foreach (dynamic network in (IEnumerable)((dynamic)manager).GetNetworks(1))
            {
                try
                {
                    if (!allowPublic && (int)network.GetCategory() == 0) continue;
                    foreach (dynamic connection in (IEnumerable)network.GetNetworkConnections())
                    {
                        try { trusted.Add(((Guid)connection.GetAdapterId()).ToString("B")); }
                        finally { Marshal.ReleaseComObject(connection); }
                    }
                }
                finally { Marshal.ReleaseComObject(network); }
            }
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Fail closed when Windows cannot identify a trusted network profile.
        }
        finally { if (manager is not null) Marshal.ReleaseComObject(manager); }

        var addresses = new List<IPAddress> { IPAddress.Loopback };
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                (!allowPublic && !trusted.Contains(adapter.Id))) continue;
            addresses.AddRange(adapter.GetIPProperties().UnicastAddresses
                .Select(x => x.Address).Where(IsLanV4));
        }
        return addresses.Distinct().ToArray();
    }

    private static bool IsLanV4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}

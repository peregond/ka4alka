using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Kachalka.Lan;

internal static class LanNetwork
{
    public static IReadOnlyList<(IPAddress Address, IPAddress Mask)> Interfaces()
    {
        var result = new List<(IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(address.Address) && address.IPv4Mask is not null)
                    result.Add((address.Address, address.IPv4Mask));
        }
        return result;
    }

    public static bool IsLocal(IPAddress address, bool allowLoopback)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(address)) return allowLoopback;
        if (!IsPrivate(address)) return false;
        var bytes = address.GetAddressBytes();
        foreach (var (local, mask) in Interfaces())
        {
            var localBytes = local.GetAddressBytes(); var maskBytes = mask.GetAddressBytes();
            if (maskBytes.All(b => b == 0)) continue;
            if (!Enumerable.Range(0, 4).All(i => (bytes[i] & maskBytes[i]) == (localBytes[i] & maskBytes[i]))) continue;
            var hostBytes = Enumerable.Range(0, 4).Select(i => bytes[i] & ~maskBytes[i]).ToArray();
            if (hostBytes.All(b => b == 0) || Enumerable.Range(0, 4).All(i => (bytes[i] | maskBytes[i]) == 255)) return false;
            return true;
        }
        return false;
    }

    public static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254);
    }

    public static IPAddress Broadcast(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes(); var m = mask.GetAddressBytes();
        return new IPAddress(Enumerable.Range(0, 4).Select(i => (byte)(a[i] | ~m[i])).ToArray());
    }
}

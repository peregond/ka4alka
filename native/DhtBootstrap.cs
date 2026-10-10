using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Kachalka;

// MonoTorrent starts its DHT from the nodes saved in dht_nodes.cache. Without that
// file it first resolves router.bittorrent.com and stays "not ready" until the name
// answers, so a slow or blocked resolver left new installs without DHT peers. When
// the file is missing or empty it is seeded with the public bootstrap routers; the
// engine replaces it with the nodes it really met when it stops.
public static class DhtBootstrap
{
    public const string CacheFile="dht_nodes.cache";
    public static readonly IReadOnlyList<IPEndPoint> Routers=
    [
        new(IPAddress.Parse("67.215.246.10"),6881),   // router.bittorrent.com
        new(IPAddress.Parse("82.221.103.244"),6881),  // router.utorrent.com
        new(IPAddress.Parse("87.98.162.88"),6881),    // dht.transmissionbt.com
        new(IPAddress.Parse("212.129.33.59"),6881),   // dht.transmissionbt.com
        new(IPAddress.Parse("185.157.221.247"),25401) // dht.libtorrent.org
    ];

    // Compact node info: a 20-byte node id, the IPv4 address and the port in network order.
    public static byte[] Compact(IPEndPoint router)
    {
        if(router.AddressFamily!=AddressFamily.InterNetwork)throw new ArgumentException("Only IPv4 routers are supported.",nameof(router));
        var node=new byte[26];RandomNumberGenerator.Fill(node.AsSpan(0,20));
        router.Address.GetAddressBytes().CopyTo(node,20);node[24]=(byte)(router.Port>>8);node[25]=(byte)router.Port;
        return node;
    }

    // The bencoded list of compact nodes that DhtEngine.SaveNodesAsync writes.
    public static byte[] SeedList()
    {
        using var output=new MemoryStream();output.WriteByte((byte)'l');
        foreach(var router in Routers){output.Write("26:"u8);output.Write(Compact(router));}
        output.WriteByte((byte)'e');return output.ToArray();
    }

    // A saved list shorter than one node ("le" after a session without DHT) counts as missing.
    public static bool EnsureSeed(string cacheDirectory)
    {
        var path=Path.Combine(cacheDirectory,CacheFile);
        if(File.Exists(path)&&new FileInfo(path).Length>=2+3+26)return false;
        Directory.CreateDirectory(cacheDirectory);File.WriteAllBytes(path,SeedList());
        return true;
    }
}

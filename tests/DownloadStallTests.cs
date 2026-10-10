using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Kachalka;
using MonoTorrent;
using MonoTorrent.Client;

// October 2026 report: after the computer slept for 11 minutes every peer
// connection failed at once and the release stayed at zero participants,
// waiting for the next tracker announce. Stalled torrents now restart.
static class DownloadStallTests
{
    public static async Task RunDhtBootstrap(string root)
    {
        static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
        var node=DhtBootstrap.Compact(new IPEndPoint(IPAddress.Parse("67.215.246.10"),6881));
        Check(node.Length==26&&node.AsSpan(20,4).SequenceEqual(new byte[]{67,215,246,10})&&node[24]==0x1A&&node[25]==0xE1,"a DHT router becomes a 26-byte compact node with its IPv4 address and port in network order");
        var rejected=false;try{DhtBootstrap.Compact(new IPEndPoint(IPAddress.IPv6Loopback,1));}catch(ArgumentException){rejected=true;}
        Check(rejected,"IPv6 router addresses are not encoded as IPv4 compact nodes");
        var seed=DhtBootstrap.SeedList();
        Check(seed[0]==(byte)'l'&&seed[^1]==(byte)'e'&&seed.Length==2+DhtBootstrap.Routers.Count*29&&DhtBootstrap.Routers.Count>=4,"the seed is a bencoded list of every bootstrap router");
        var cache=Path.Combine(root,"dht-seed-cache");
        Check(DhtBootstrap.EnsureSeed(cache)&&!DhtBootstrap.EnsureSeed(cache),"a missing node cache is seeded once and a real one is left alone");
        await File.WriteAllTextAsync(Path.Combine(cache,DhtBootstrap.CacheFile),"le");
        Check(DhtBootstrap.EnsureSeed(cache),"an empty saved node list is seeded again");
        // Without the seed MonoTorrent waits for router.bittorrent.com to resolve before the DHT even starts.
        // Fast resume is off: on Windows a stop could race the engine's own fast-resume write for the same file.
        using(var engine=new ClientEngine(new EngineSettingsBuilder{CacheDirectory=cache,AllowPortForwarding=false,AllowLocalPeerDiscovery=false,AutoSaveLoadFastResume=false}.ToSettings()))
        {
            var folder=Path.Combine(root,"dht-seed-data");Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(Path.Combine(folder,"dht.bin"),RandomNumberGenerator.GetBytes(64*1024));
            var torrent=Path.Combine(root,"dht-seed.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(Path.Combine(folder,"dht.bin")),torrent);
            var manager=await engine.AddAsync(torrent,Path.Combine(root,"dht-seed-download"));await manager.StartAsync();
            var until=DateTime.UtcNow.AddSeconds(10);while(engine.Dht.State==MonoTorrent.Dht.DhtState.NotReady&&DateTime.UtcNow<until)await Task.Delay(100);
            Check(engine.Dht.State!=MonoTorrent.Dht.DhtState.NotReady,"with the seeded node cache the DHT starts at once, without resolving a router name");
            await engine.StopAllAsync();
        }
        Check(DownloadService.DiskCacheFor(16L<<30)==64<<20&&DownloadService.DiskCacheFor(6L<<30)==64<<20&&DownloadService.DiskCacheFor(4L<<30)==32<<20,"the disk write cache is 64 MB with at least 6 GB of memory and 32 MB below that");
        Check(MagnetDiscovery.PublicTrackers.Distinct().Count()==MagnetDiscovery.PublicTrackers.Count&&MagnetDiscovery.PublicTrackers.All(x=>Uri.TryCreate(x,UriKind.Absolute,out var uri)&&uri.Scheme is "udp" or "https"),"public fallback trackers are distinct UDP or HTTPS announce addresses");
    }
    public static async Task Run(string root)
    {
        static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
        static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        var oldData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"stall-state"));
        var folder=Path.Combine(root,"stall-seed");Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder,"stalled.bin"),RandomNumberGenerator.GetBytes(256*1024));
        var torrent=Path.Combine(root,"stalled.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(Path.Combine(folder,"stalled.bin")),torrent);
        var builder=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,"stall-cache"),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false};
        builder.ListenEndPoints.Clear();builder.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,Port()));
        var space=new DownloadSpace(_=>new("stall-volume",8L*1024*1024*1024),path=>File.Exists(path)?new FileInfo(path).Length:0);
        var service=new DownloadService(builder.ToSettings(),downloadSpace:space);
        try
        {
            var now=new DateTime(2026,10,10,14,0,0,DateTimeKind.Utc);
            typeof(DownloadService).GetField("stallClock",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(service,new Func<DateTime>(()=>now));
            var item=await service.Add(torrent,Path.Combine(root,"stall-download"));
            var managers=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
            var manager=managers[item.Id];
            var until=DateTime.UtcNow.AddSeconds(20);while(manager.State!=TorrentState.Downloading&&DateTime.UtcNow<until)await Task.Delay(100);
            Check(manager.State==TorrentState.Downloading&&manager.OpenConnections==0,"a release without reachable peers downloads with zero connections");
            async Task<bool> Poll(TimeSpan advance,bool recover=true)
            {
                now+=advance;var started=item.LastStartedUtc;
                await service.PollReliabilityAsync(default,recover);
                return item.LastStartedUtc!=started;
            }
            Check(!await Poll(TimeSpan.Zero),"the first idle poll only starts the stall clock");
            Check(!await Poll(TimeSpan.FromSeconds(60)),"a minute without connections is not yet a stall");
            Check(await Poll(TimeSpan.FromSeconds(40)),"after 90 seconds without connections the torrent restarts and announces again");
            Check(manager.State==TorrentState.Downloading,"the restarted torrent keeps downloading");
            Check(!await Poll(TimeSpan.FromSeconds(60))&&!await Poll(TimeSpan.FromSeconds(60))&&!await Poll(TimeSpan.FromSeconds(90)),"the next restart waits five minutes, so a dead release does not hammer trackers");
            Check(await Poll(TimeSpan.FromSeconds(90)),"a release that is still stalled restarts again after five minutes");
            Check(!await Poll(TimeSpan.FromSeconds(100))&&!await Poll(TimeSpan.FromSeconds(100))&&!await Poll(TimeSpan.FromSeconds(100))&&!await Poll(TimeSpan.FromSeconds(100))&&!await Poll(TimeSpan.FromSeconds(100)),"the pause between restarts grows to ten minutes");
            Check(await Poll(TimeSpan.FromMinutes(3)),"the first poll after the computer wakes restarts at once");
            Check(!await Poll(TimeSpan.FromMinutes(3),recover:false),"with recovery switched off nothing restarts");
            service.SetNetworkAvailable(false);
            Check(!await Poll(TimeSpan.FromMinutes(30)),"while the network is down nothing restarts");
        }
        finally
        {
            await service.Close();
            Environment.SetEnvironmentVariable("KACHALKA_DATA",oldData);
        }
    }
}

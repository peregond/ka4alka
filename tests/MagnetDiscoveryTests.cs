using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Kachalka;
using MonoTorrent;
using MonoTorrent.Client;

static class MagnetDiscoveryTests
{
    public static async Task Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        EngineSettings Settings(string name,int port)
        {
            var builder=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,name),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false};
            builder.ListenEndPoints.Clear();builder.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,port));return builder.ToSettings();
        }
        async Task Until(Func<bool> ready,string message)
        {
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(!ready()&&DateTime.UtcNow<deadline)await Task.Delay(100);
            Check(ready(),message);
        }
        var data=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"discovery-state"));
        var folder=Path.Combine(root,"discovery-seed");Directory.CreateDirectory(folder);
        var payload=RandomNumberGenerator.GetBytes(256*1024);var file=Path.Combine(folder,"discovery.bin");await File.WriteAllBytesAsync(file,payload);
        var path=Path.Combine(root,"discovery.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(file),path);
        var port=FreePort();using var seed=new ClientEngine(Settings("discovery-seed-cache",port));
        using var silentUdp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        var unavailable=$"udp://127.0.0.1:{((IPEndPoint)silentUdp.Client.LocalEndPoint!).Port}/announce";
        await using var http=new LocalTracker(port);
        var service=new DownloadService(Settings("discovery-download-cache",FreePort()),publicTrackers:[http.FailUrl,http.Url,http.Url]);
        try
        {
            var seeder=await seed.AddAsync(path,folder);await seeder.StartAsync();await Until(()=>seeder.State==TorrentState.Seeding,"tracker discovery fixture seeder ready");
            var hash=seeder.InfoHashes.V1!.ToHex();
            var magnet="magnet:?xt=urn:btih:"+hash+"&dn=discovery.bin&tr="+Uri.EscapeDataString(unavailable);
            var release=new SourceEntry("The Pirate Bay:fixture","Discovery","The Pirate Bay","https://thepiratebay.org/description.php?id=1",magnet,null);
            await service.Add(magnet,Path.Combine(root,"discovery-download"),release:release);
            var managers=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
            var manager=managers.Values.Single();
            // No AddPeerAsync call: the downloader must discover its peer via the fallback announce.
            await Until(()=>manager.HasMetadata,"magnet obtains file metadata through HTTP while its UDP tracker never responds");
            await Until(()=>manager.Progress==100,"tracker-discovered peer transfers the real payload without injected peers or DHT");
            var received=Directory.GetFiles(service.Items.Single().Folder,"discovery.bin",SearchOption.AllDirectories).Single();
            Check(SHA256.HashData(await File.ReadAllBytesAsync(received)).SequenceEqual(SHA256.HashData(payload)),"fallback discovery transfers bytes matching SHA256");
            Check(http.GoodAnnounces>0&&http.FailedAnnounces>0,"a failed HTTP tracker does not prevent another independent tier from finding a peer");
            Check(manager.TrackerManager.Tiers.SelectMany(t=>t.Trackers).Count()==3,"fallback tracker deduplication retains the original tracker");
            Check(manager.InfoHashes.V1!.ToHex()==hash,"discovery keeps the exact selected infohash");
            var closeWatch=System.Diagnostics.Stopwatch.StartNew();await service.Close();
            Check(closeWatch.Elapsed<TimeSpan.FromSeconds(10),"closing with an unresponsive UDP tracker finishes promptly and persists the queue");
            var restored=new DownloadService(Settings("discovery-restore-cache",FreePort()),publicTrackers:[http.Url]);
            try
            {
                Check(restored.PendingResumeCount==1,"public magnet remains eligible for startup resume");
                await restored.ResumePendingAsync();
                var restoredManagers=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(restored)!;
                var restoredManager=restoredManagers.Values.Single();
                Check(restoredManager.TrackerManager.Tiers.SelectMany(t=>t.Trackers).Any(t=>t.Uri.AbsoluteUri==http.Url),"restored older TPB magnet gains fallback discovery without changing its payload path");
                Check(restored.Items.Single().Folder==service.Items.Single().Folder,"upgrading discovery preserves the original download folder");
            }
            finally{await restored.Close();}
            await service.Remove(service.Items.Single());
            var publicRelease=release with{Source="RuTor",TorrentUrl=null};
            await service.Add(path,Path.Combine(root,"discovery-torrent-download"),release:publicRelease);
            var publicManager=managers.Values.Single();
            await Until(()=>publicManager.Progress==100,"public .torrent transfer discovers its seed through fallback trackers without an injected peer");
            var publicFile=Directory.GetFiles(service.Items.Single().Folder,"discovery.bin",SearchOption.AllDirectories).Single();
            Check(SHA256.HashData(await File.ReadAllBytesAsync(publicFile)).SequenceEqual(SHA256.HashData(payload)),"public source .torrent fallback preserves the selected payload bytes");
            var privateCreator=new TorrentCreator{Private=true};
            var privatePath=Path.Combine(root,"private-discovery.torrent");await privateCreator.CreateAsync(new TorrentFileSource(file),privatePath);
            using var privateEngine=new ClientEngine(Settings("private-discovery-cache",FreePort()));
            var privateManager=await privateEngine.AddAsync(privatePath,folder);
            await MagnetDiscovery.ConfigureAsync(privateManager,[http.Url]);
            Check(privateManager.TrackerManager.Tiers.Count==0,"private torrents do not gain public trackers");
        }
        finally{await service.Close();await seed.StopAllAsync();Environment.SetEnvironmentVariable("KACHALKA_DATA",data);}
    }

    // A real loopback HTTP tracker with a compact seed endpoint and a second,
    // deliberately failing announce route. No public network is used by this regression.
    sealed class LocalTracker : IAsyncDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);
        readonly CancellationTokenSource stop=new();
        readonly Task loop;
        readonly int seedPort;
        int good,failed;
        public int GoodAnnounces=>Volatile.Read(ref good);
        public int FailedAnnounces=>Volatile.Read(ref failed);
        public string Url {get;}
        public string FailUrl {get;}
        public LocalTracker(int seedPort)
        {
            this.seedPort=seedPort;listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
            Url=$"http://127.0.0.1:{port}/announce";FailUrl=$"http://127.0.0.1:{port}/fail";loop=Listen();
        }
        async Task Listen()
        {
            try
            {
                while(!stop.IsCancellationRequested)
                {
                    using var client=await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                    var line=await reader.ReadLineAsync(stop.Token)??"";
                    while(!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))){}
                    byte[] body;string status;
                    if(line.StartsWith("GET /scrape?",StringComparison.Ordinal)){status="200 OK";body=Encoding.ASCII.GetBytes("d5:filesdee");}
                    else if(line.StartsWith("GET /fail?",StringComparison.Ordinal)){Interlocked.Increment(ref failed);status="503 Service Unavailable";body=[];}
                    else
                    {
                        if(!line.StartsWith("GET /announce?",StringComparison.Ordinal)||!line.Contains("info_hash="))throw new Exception("Malformed tracker announce");
                        Interlocked.Increment(ref good);status="200 OK";
                        body=Encoding.ASCII.GetBytes("d8:completei1e10:incompletei0e8:intervali60e5:peers6:")
                            .Concat(new byte[]{127,0,0,1,(byte)(seedPort>>8),(byte)seedPort}).Concat(new byte[]{(byte)'e'}).ToArray();
                    }
                    var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/x-bittorrent\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header,stop.Token);await stream.WriteAsync(body,stop.Token);
                }
            }
            catch(OperationCanceledException)when(stop.IsCancellationRequested){}
            catch(SocketException)when(stop.IsCancellationRequested){}
            catch(IOException)when(stop.IsCancellationRequested){}
        }
        public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();try{await loop;}finally{stop.Dispose();}}
    }
}

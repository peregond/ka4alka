using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;
using Kachalka.Lan;
using MonoTorrent;
using MonoTorrent.Client;

static class PhoneDownloadTests
{
    public static async Task Run(string root,TorrentManager seed,int port,byte[] expected)
    {
        var folder=Path.Combine(root,"phone-download");Directory.CreateDirectory(folder);
        var settings=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,"phone-cache"),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,DiskCacheBytes=1024*1024};
        settings.ListenEndPoints.Clear();settings.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,0));
        var downloads=new DownloadService(settings.ToSettings());
        var prefs=new Preferences{Folder=folder,FolderConfigured=true};
        using var catalog=new PhoneRemoteCatalog(downloads,prefs);
        await using var server=new PhoneRemoteServer(catalog.Handle,0,true);server.Start();
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{server.Port}"),Timeout=TimeSpan.FromSeconds(20)};
        async Task<JsonElement> Call(string path,object args,bool ok=true)
        {
            using var content=new StringContent(JsonSerializer.Serialize(args),Encoding.UTF8);content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
            using var response=await client.PostAsync(path,content);
            if(response.IsSuccessStatusCode!=ok)throw new Exception("Unexpected phone HTTP response: "+response.StatusCode);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();
        }
        try
        {
            var code=JsonSerializer.SerializeToElement(server.OpenPairing()).GetProperty("code").GetString();
            var paired=await Call("/pair",new{code});
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",paired.GetProperty("token").GetString());
            foreach(var invalid in new[]{"file:///etc/passwd",Path.Combine(root,"test.torrent"),"https://example.test/file.torrent","magnet:?xt=invalid"})
                await Call("/api/add",new{source=invalid},false);
            if(downloads.EngineCreated||downloads.Items.Count!=0)throw new Exception("Invalid phone input started engine.");
            var magnet="magnet:?xt=urn:btih:"+seed.InfoHashes.V1!.ToHex()+"&dn=test.bin";
            var added=await Call("/api/add",new{source=magnet});
            var repeated=await Call("/api/add",new{source=magnet});
            if(added.GetProperty("id").GetString()!=repeated.GetProperty("id").GetString()||downloads.Items.Count!=1)throw new Exception("Phone retry duplicated queue.");
            var managers=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(downloads)!;
            var manager=managers.Values.Single();await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
            var deadline=DateTime.UtcNow.AddSeconds(30);while(manager.Progress<100&&DateTime.UtcNow<deadline)await Task.Delay(100);
            if(manager.Progress<100)throw new Exception("Phone-started download did not complete.");
            await downloads.Toggle(downloads.Items.Single());
            var file=Directory.GetFiles(folder,"test.bin",SearchOption.AllDirectories).Single();
            if(!SHA256.HashData(await File.ReadAllBytesAsync(file)).SequenceEqual(SHA256.HashData(expected)))throw new Exception("Phone download bytes differ.");
            var transfers=await Call("/api/downloads",new{});
            if(transfers.GetProperty("items").GetArrayLength()!=1||transfers.ToString().Contains(folder))throw new Exception("Phone transfer list invalid or reveals local path.");
            prefs.FolderConfigured=false;await Call("/api/add",new{source=magnet},false);
            Console.WriteLine("PASS: paired phone starts actual torrent transfer, SHA-256 matches, retries deduplicate, invalid paths/URLs and unconfigured folder rejected");
        }
        finally{foreach(var item in downloads.Items.ToArray())await downloads.Remove(item);await downloads.Close();}
    }
}

using System.Reflection;
using System.Text.Json;
using Kachalka;
using MonoTorrent.Client;

static class MagnetDiscoveryProbe
{
    public static async Task Run(string root)
    {
        using var client=new SourceClient();
        var film=new MediaItem(0,"Гипотеза любви","Фильмы","мелодрама",2026,"","","#526B69"){OriginalTitle="The Love Hypothesis"};
        var rows=PirateBaySource.Parse(await client.Read(PirateBaySource.QueryUri(film.OriginalTitle,false),2_000_000,CancellationToken.None),film);
        var release=rows.Single(x=>x.Title=="The Love Hypothesis (2026) [1080p] [BluRay] [5.1]");
        Console.WriteLine(JsonSerializer.Serialize(new{release.Title,release.Id,release.Seeds,release.Size}));
        foreach(var fallback in new[]{false,true})
        {
            var label=fallback?"independent-HTTPS-fallback":"original-UDP-only";
            Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,label));
            var settings=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,label,"cache"),AllowPortForwarding=false,MaximumDownloadRate=1}.ToSettings();
            var service=new DownloadService(settings,downloadLimitKbps:1,publicTrackers:fallback?null:[]);
            try
            {
                // A one-KB/second payload cap keeps this a discovery probe.
                await service.Add(release.TorrentUrl!,Path.Combine(root,label,"files"),release:release);
                var managers=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
                var manager=managers.Values.Single();var deadline=DateTime.UtcNow.AddSeconds(fallback?60:20);
                while(!manager.HasMetadata&&DateTime.UtcNow<deadline)await Task.Delay(100);
                service.Update();
                var results=new{Mode=label,Metadata=manager.HasMetadata,Hash=manager.InfoHashes.V1?.ToHex(),Size=manager.Torrent?.Size,Connections=manager.OpenConnections,PayloadBytes=manager.Monitor.DataBytesReceived,Trackers=manager.TrackerManager.Tiers.SelectMany(t=>t.Trackers).Select(t=>new{Url=t.Uri.AbsoluteUri,Status=t.Status.ToString()}).ToArray()};
                Console.WriteLine(JsonSerializer.Serialize(results));
                await File.WriteAllTextAsync(Path.Combine(root,label+".json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
                if(manager.HasMetadata&&manager.Torrent!.Size!=release.Size)throw new Exception("Public metadata size does not match the selected indexed release.");
            }
            finally{await service.Close();}
        }
    }
}

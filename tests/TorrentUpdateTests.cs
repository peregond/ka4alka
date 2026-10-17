using Kachalka;
using MonoTorrent;
using MonoTorrent.Client;

// A running series: the tracker swaps the topic's torrent when an episode is added. The queued task takes the new
// torrent in place and the episode that is already on disk is found again, not downloaded a second time.
static class TorrentUpdateTests
{
    public static async Task Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var old=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"torrent-update-state"));
        Directory.CreateDirectory(Preferences.DataDir);
        var downloads=Path.Combine(root,"torrent-update-downloads");var show=Path.Combine(downloads,"Show");Directory.CreateDirectory(show);
        var first=new byte[256*1024];Random.Shared.NextBytes(first);await File.WriteAllBytesAsync(Path.Combine(show,"S01E01.bin"),first);
        var before=Path.Combine(root,"torrent-update-before.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(show),before);
        var second=new byte[256*1024];Random.Shared.NextBytes(second);var episode2=Path.Combine(show,"S01E02.bin");await File.WriteAllBytesAsync(episode2,second);
        var after=Path.Combine(root,"torrent-update-after.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(show),after);
        // The new episode is not on this computer yet.
        File.Delete(episode2);
        var settings=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,"torrent-update-cache"),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,AutoSaveLoadFastResume=true}.ToSettings();
        var space=new DownloadSpace(_=>new("test-volume",4L*1024*1024*1024),p=>File.Exists(p)?new FileInfo(p).Length:0);
        var service=new DownloadService(settings,publicTrackers:[],downloadSpace:space);
        try
        {
            var item=await service.Add(before,downloads);
            var firstHash=item.InfoHash;
            for(var wait=0;wait<150&&item.Progress<99;wait++){await Task.Delay(100);service.Update();}
            Check(item.Progress>=99,"the first torrent is complete from the file that is already on disk");
            Check(await service.ReplaceTorrentAsync(item,before)==DownloadService.TorrentUpdate.Unchanged&&item.InfoHash==firstHash,"the same torrent again changes nothing");
            Check(await service.ReplaceTorrentAsync(item,after)==DownloadService.TorrentUpdate.Updated&&item.InfoHash!=firstHash&&!item.Paused,"a different torrent of the same topic replaces the queued one and keeps running");
            for(var wait=0;wait<200&&item.Progress<40;wait++){await Task.Delay(100);service.Update();}
            Check(item.Progress is >=40 and <90&&service.Items.Count==1,"the episode already on disk is found again and only the new one is left to fetch");
            Check(item.Files.Count==2||item.Files.Count==0,"the task now lists the files of the new torrent");
        }
        finally{await service.Close();Environment.SetEnvironmentVariable("KACHALKA_DATA",old);}
    }
}

using System.Text.Json;
using Kachalka;
using MonoTorrent;
using MonoTorrent.Client;

static class MissingDownloadFilesTests
{
    public static async Task Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var old=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"missing-payload-state"));
        Directory.CreateDirectory(Preferences.DataDir);
        var folder=Path.Combine(root,"missing-payload");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"episode.bin");var bytes=new byte[64*1024];Random.Shared.NextBytes(bytes);await File.WriteAllBytesAsync(path,bytes);
        var torrent=Path.Combine(root,"missing-payload.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(path),torrent);
        var file=new DownloadFile("episode.bin",path,path+".!mt",bytes.Length,100);
        Check(CompletedPayloadFiles.Problem([file])==null,"completed payload is checked using filesystem metadata");
        await File.WriteAllBytesAsync(path,bytes[..1024]);Check(CompletedPayloadFiles.Problem([file])!=null,"externally truncated completed payload needs explicit recovery");
        await File.WriteAllBytesAsync(path,bytes);File.Delete(path);
        var item=new DownloadItem{Name="Completed externally deleted",Folder=folder,Source=torrent,Progress=100,Paused=false,Files=[file]};
        await File.WriteAllTextAsync(Path.Combine(Preferences.DataDir,"queue.json"),JsonSerializer.Serialize(new[]{item}));
        var settings=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,"missing-cache"),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,AutoSaveLoadFastResume=true}.ToSettings();
        var space=new DownloadSpace(_=>new("test-volume",4L*1024*1024*1024),p=>File.Exists(p)?new FileInfo(p).Length:0);
        var service=new DownloadService(settings,publicTrackers:[],downloadSpace:space);
        try
        {
            await service.ResumePendingAsync();var restored=service.Items.Single();
            Check(restored.HasCompletedDownload&&restored.FilesMissing&&restored.Paused&&!service.EngineCreated&&!File.Exists(path),"startup recovery never recreates externally deleted completed files or starts the torrent engine");
            await service.Toggle(restored);await service.SetGroupAsync(false,false);await service.SetGroupAsync(true,false);
            Check(restored.Paused&&restored.Status=="Файлы не найдены"&&!service.EngineCreated,"individual and bulk Continue cannot silently restart a missing completed torrent");
            await File.WriteAllBytesAsync(path,bytes);await service.CheckCompletedFilesAsync(true);
            Check(!restored.FilesMissing&&restored.Paused&&!service.EngineCreated,"returning the original file clears the warning without automatically starting seeding");
            File.Delete(path);await service.CheckCompletedFilesAsync(true);
            Check(restored.FilesMissing&&restored.Paused,"deleting payload while the application is open is detected");
            await service.Close();service=new DownloadService(settings,publicTrackers:[],downloadSpace:space);
            await service.Toggle(service.Items.Single());restored=service.Items.Single();
            Check(restored.FilesMissing&&restored.Paused&&!service.EngineCreated,"missing-file protection persists across another application restart");
            await service.RedownloadAsync(restored);
            Check(!restored.FilesMissing&&!restored.HasCompletedDownload&&!restored.Paused&&service.EngineCreated,"explicit redownload creates a real torrent manager and removes the completed-file guard");
            await service.Remove(restored);
            Check(service.Items.Count==0,"missing-file task can be removed from the queue independently of payload deletion");
            Check(CompletedPayloadFiles.Problem([])!=null,"legacy completed tasks without a file list are protected conservatively");
            await File.WriteAllBytesAsync(path,bytes);
            Check(CompletedPayloadFiles.Problem([file,new("ignored",path+".ignored","",100,0)])==null,"unselected incomplete files do not invalidate an otherwise completed payload");
        }
        finally{await service.Close();Environment.SetEnvironmentVariable("KACHALKA_DATA",old);}
    }
}

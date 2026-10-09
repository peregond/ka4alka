using System.Diagnostics;
using System.Text.Json;
using Kachalka;

static class DownloadDeletionTests
{
    public static async Task Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var folder=Path.Combine(root,"deletion-files");Directory.CreateDirectory(folder);
        var complete=Path.Combine(folder,"completed.mkv");var partial=Path.Combine(folder,"partial.mkv.!mt");var unrelated=Path.Combine(folder,"unrelated.txt");
        await File.WriteAllTextAsync(complete,"complete");await File.WriteAllTextAsync(partial,"partial");await File.WriteAllTextAsync(unrelated,"keep");
        File.SetAttributes(complete,File.GetAttributes(complete)|FileAttributes.ReadOnly);File.SetAttributes(partial,File.GetAttributes(partial)|FileAttributes.ReadOnly);File.SetAttributes(unrelated,File.GetAttributes(unrelated)|FileAttributes.ReadOnly);
        Check(await DownloadFiles.DeleteAsync(folder,[complete,partial,complete,Path.Combine(folder,"missing.mkv")])==2,"deletion removes readonly completed and partial payload, deduplicates paths and tolerates missing files");
        Check(!File.Exists(complete)&&!File.Exists(partial)&&File.ReadAllText(unrelated)=="keep"&&(File.GetAttributes(unrelated)&FileAttributes.ReadOnly)!=0,"deletion leaves unrelated content and attributes intact");
        File.SetAttributes(unrelated,FileAttributes.Normal);

        var safe=Path.Combine(folder,"safe.mkv");var outside=Path.Combine(root,"outside-deletion.mkv");await File.WriteAllTextAsync(safe,"safe");await File.WriteAllTextAsync(outside,"outside");
        try{await DownloadFiles.DeleteAsync(folder,[safe,outside]);throw new Exception("outside payload accepted");}catch(IOException){ }
        Check(File.Exists(safe)&&File.Exists(outside),"all paths are validated before any payload is deleted");
        var nested=Path.Combine(folder,"nested");Directory.CreateDirectory(nested);var nestedPayload=Path.Combine(nested,"episode.mkv");await File.WriteAllTextAsync(nestedPayload,"episode");
        try{await DownloadFiles.DeleteAsync(folder,[safe,nested]);throw new Exception("directory target accepted");}catch(IOException){ }
        Check(File.Exists(safe)&&File.Exists(nestedPayload),"directory masquerading as a torrent file never deletes prior payload or directory contents");
        Check(await DownloadFiles.DeleteAsync(folder,[nestedPayload])==1&&!Directory.Exists(nested)&&Directory.Exists(folder),"successful deletion removes empty torrent parents and keeps the download root");

        var previous=Environment.GetEnvironmentVariable("KACHALKA_DATA");Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"deletion-state"));
        var service=new DownloadService();
        try
        {
            var originalTorrent=Path.Combine(root,"user-original.torrent");await File.WriteAllTextAsync(originalTorrent,"original torrent stays");
            var removeOnly=new DownloadItem{Folder=folder,Source=originalTorrent,Name="remove only",Files=[new("safe",safe,safe,4,100)]};service.Items.Add(removeOnly);service.Save();
            await service.Remove(removeOnly);
            Check(!service.Items.Contains(removeOnly)&&File.Exists(safe)&&File.Exists(originalTorrent),"removing a queue entry preserves payload and the user's original torrent file");
            await service.Remove(removeOnly,true);
            Check(File.Exists(safe),"a stale deleted queue item cannot later delete its retained payload");
            var first=new DownloadItem{Folder=folder,Name="first",Files=[new("safe",safe,safe,4,100)]};
            var other=new DownloadItem{Folder=folder,Name="other",Files=[new("safe",Path.Combine(folder,"nested","..","safe.mkv"),"",4,100)]};service.Items.Add(first);service.Items.Add(other);
            try{await service.Remove(first,true);throw new Exception("shared file deletion allowed");}catch(IOException){ }
            Check(service.Items.Contains(first)&&service.Items.Contains(other)&&File.Exists(safe),"canonical shared payload paths block deletion and retain both queue items");
            await service.Remove(other);await service.Remove(first,true);
            Check(!File.Exists(safe)&&service.Items.Count==0,"deletion succeeds after the other reference is removed");

            if(OperatingSystem.IsWindows())
            {
                var externalFolder=Path.Combine(root,"junction-external");Directory.CreateDirectory(externalFolder);
                var externalPayload=Path.Combine(externalFolder,"external.mkv");await File.WriteAllTextAsync(externalPayload,"external stays");
                var junction=Path.Combine(folder,"junction");var junctionStart=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var argument in new[]{"/d","/c","mklink","/J",junction,externalFolder})junctionStart.ArgumentList.Add(argument);
                using(var creation=Process.Start(junctionStart)??throw new Exception("Cannot start Windows junction fixture"))
                {
                    var stdout=creation.StandardOutput.ReadToEndAsync();var stderr=creation.StandardError.ReadToEndAsync();await creation.WaitForExitAsync();await Task.WhenAll(stdout,stderr);
                    Check(creation.ExitCode==0&&(File.GetAttributes(junction)&FileAttributes.ReparsePoint)!=0,"native Windows fixture creates an actual directory junction");
                }
                var normal=Path.Combine(folder,"before-junction.mkv");await File.WriteAllTextAsync(normal,"normal stays");
                try{await DownloadFiles.DeleteAsync(folder,[normal,Path.Combine(junction,"external.mkv")]);throw new Exception("junction deletion allowed");}catch(IOException){ }
                Check(File.Exists(normal)&&File.ReadAllText(externalPayload)=="external stays","Windows junction is rejected before any legitimate or external payload is deleted");
                Directory.Delete(junction);await DownloadFiles.DeleteAsync(folder,[normal]);

                var transient=Path.Combine(folder,"transient-lock.mkv");await File.WriteAllTextAsync(transient,"locked");
                var blocker=new FileStream(transient,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                var clock=Stopwatch.StartNew();var delete=DownloadFiles.DeleteAsync(folder,[transient]);
                await Task.Delay(180);Check(!delete.IsCompleted&&File.Exists(transient),"transient Windows lock waits without deleting an open file");
                blocker.Dispose();Check(await delete==1&&!File.Exists(transient)&&clock.Elapsed<TimeSpan.FromSeconds(3),"deletion retries transient Windows sharing violation within a bounded time");

                var failed=Path.Combine(folder,"permanent-lock.mkv");await File.WriteAllTextAsync(failed,"locked");File.SetAttributes(failed,FileAttributes.ReadOnly);
                var item=new DownloadItem{Folder=folder,Name="locked payload",Files=[new("locked",failed,failed,6,100)]};service.Items.Add(item);service.Save();
                using(var held=new FileStream(failed,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                {
                    clock.Restart();
                    try{await service.Remove(item,true);throw new Exception("locked deletion accepted");}catch(DownloadFileDeletionException error){Check(error.Message.Contains("другой программе")&&error.DeletedFiles==0,"permanent file lock reports a useful reason without claiming deletion");}
                    Check(clock.Elapsed<TimeSpan.FromSeconds(3)&&service.Items.Contains(item)&&!item.Busy&&File.Exists(failed)&&(File.GetAttributes(failed)&FileAttributes.ReadOnly)!=0,"failed removal is bounded, retains the queue item and restores readonly attributes");
                    Check(new DownloadService().Items.Any(x=>x.Id==item.Id),"failed file deletion survives queue restart for a safe retry");
                }
                await service.Remove(item,true);Check(!File.Exists(failed)&&!service.Items.Contains(item),"retry after the external handle closes deletes the retained task and payload");
                var logs=File.ReadAllLines(Path.Combine(Preferences.DataDir,"diagnostic.log")).Select(line=>JsonDocument.Parse(line)).ToArray();
                try
                {
                    var failedEvent=logs.Last(doc=>doc.RootElement.GetProperty("Event").GetString()=="download-remove-failed");var details=failedEvent.RootElement.GetProperty("Details");
                    Check(details.GetProperty("Phase").GetString()=="files"&&(details.GetProperty("HResult").GetInt32()&0xffff) is 32 or 33&&!details.ToString().Contains(folder,StringComparison.OrdinalIgnoreCase),"removal failure logs contain the file stage and Windows error without private paths");
                }
                finally{foreach(var log in logs)log.Dispose();}
            }
            else Console.WriteLine("Windows-only sharing lock and readonly deletion semantics are validated by native Windows CI.");
            var noMetadata=new DownloadItem{Folder=folder,Source=originalTorrent,Name="no metadata"};service.Items.Add(noMetadata);await service.Remove(noMetadata,true);
            Check(service.Items.Count==0&&File.Exists(unrelated)&&File.Exists(originalTorrent),"unknown-metadata removal deletes the task without guessing payload paths or erasing the user's torrent");
            if(OperatingSystem.IsWindows())
            {
                var alreadyDeleted=Path.Combine(folder,"cancel-first.mkv");var retained=Path.Combine(folder,"cancel-locked.mkv");
                await File.WriteAllTextAsync(alreadyDeleted,"first");await File.WriteAllTextAsync(retained,"locked");File.SetAttributes(retained,FileAttributes.ReadOnly);
                var canceledItem=new DownloadItem{Folder=folder,Name="cancel midway",Files=[new("first",alreadyDeleted,alreadyDeleted,5,100),new("locked",retained,retained,6,100)]};
                service.Items.Add(canceledItem);service.Save();
                using(var held=new FileStream(retained,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                {
                    var removing=service.Remove(canceledItem,true);var until=DateTime.UtcNow.AddSeconds(3);
                    while(File.Exists(alreadyDeleted)&&DateTime.UtcNow<until)await Task.Delay(10);
                    Check(!File.Exists(alreadyDeleted)&&!removing.IsCompleted,"first payload is deleted while the second is held by a Windows handle");
                    await Task.Delay(40);var closing=service.Close();
                    try{await removing;throw new Exception("midway cancellation was lost");}
                    catch(DownloadFileDeletionCanceledException error){Check(error.DeletedFiles==1&&error.CancellationToken.IsCancellationRequested,"Close preserves cancellation semantics and the actual partial-deletion count");}
                    await closing;
                    Check(File.Exists(retained)&&(File.GetAttributes(retained)&FileAttributes.ReadOnly)!=0&&service.Items.Contains(canceledItem)&&!canceledItem.Busy&&canceledItem.Hint.Contains("Удалена часть файлов"),"canceled removal retains the locked file, its readonly attribute and a useful partial-deletion warning");
                    Check(new DownloadService().Items.Any(item=>item.Id==canceledItem.Id),"canceled partial deletion keeps a persisted task for retry after restart");
                    using var eventJson=JsonDocument.Parse(File.ReadAllLines(Path.Combine(Preferences.DataDir,"diagnostic.log")).Last(line=>line.Contains("download-remove-failed")));
                    Check(eventJson.RootElement.GetProperty("Details").GetProperty("DeletedFiles").GetInt32()==1,"canceled removal diagnostic reports one file already deleted");
                }
            }
        }
        finally{await service.Close();Environment.SetEnvironmentVariable("KACHALKA_DATA",previous);}
    }
}

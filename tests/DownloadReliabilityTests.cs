using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Kachalka;
using MonoTorrent;
using MonoTorrent.Client;

static class DownloadReliabilityTests
{
    public static async Task Run(string root)
    {
        void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
        Action? printDiagnostics=null;
        static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        EngineSettings Settings(string name,int port)
        {
            var builder=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,name),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,AutoSaveLoadFastResume=true,AutoSaveLoadMagnetLinkMetadata=true};
            builder.ListenEndPoints.Clear();builder.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,port));return builder.ToSettings();
        }
        async Task Until(Func<bool> ready,string message,int seconds=40)
        {
            var until=DateTime.UtcNow.AddSeconds(seconds);while(!ready()&&DateTime.UtcNow<until)await Task.Delay(100);
            if(!ready()){Console.WriteLine("RELIABILITY TIMEOUT: "+message);printDiagnostics?.Invoke();}
            Check(ready(),message);
        }
        static Dictionary<string,TorrentManager> Managers(DownloadService service)=>(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
        static async Task<byte[]> LiveBytes(string path)
        {
            await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var output=new MemoryStream();await input.CopyToAsync(output);return output.ToArray();
        }
        var oldData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"reliability-state"));
        var seedFolder=Path.Combine(root,"reliability-seed");Directory.CreateDirectory(seedFolder);
        var bytes=RandomNumberGenerator.GetBytes(4*1024*1024);var seedFile=Path.Combine(seedFolder,"reliable.bin");await File.WriteAllBytesAsync(seedFile,bytes);
        var tailBytes=RandomNumberGenerator.GetBytes(64*1024);var seedTail=Path.Combine(seedFolder,"recovery-tail.bin");await File.WriteAllBytesAsync(seedTail,tailBytes);
        var torrent=Path.Combine(root,"reliable.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(seedFolder),torrent);
        var seedPort=Port();using var seed=new ClientEngine(Settings("reliability-seed-cache",seedPort));
        long free=4L*1024*1024*1024;
        var space=new DownloadSpace(_=>new("fixture-volume",Volatile.Read(ref free)),path=>File.Exists(path)?new FileInfo(path).Length:0);
        var service=new DownloadService(Settings("reliability-download-cache",Port()),downloadLimitKbps:256,downloadSpace:space);
        var diagnosticService=service;
        var notices=new List<DownloadNotice>();service.Notice+=notices.Add;
        try
        {
            // The multi-file torrent's containing directory is seedFolder;
            // its parent is therefore the seeder's save root.
            var seeder=await seed.AddAsync(torrent,Path.GetDirectoryName(seedFolder)!,new TorrentSettingsBuilder{CreateContainingDirectory=true}.ToSettings());
            printDiagnostics=()=>
            {
                Console.WriteLine($"RELIABILITY SEED: state={seeder.State}, progress={seeder.Progress:F2}, bytesSent={seeder.Monitor.DataBytesSent}, connections={seeder.OpenConnections}, hash={seeder.InfoHashes.V1?.ToHex()}, configuredPort={seedPort}, error={seeder.Error?.Exception?.Message}");
                foreach(var item in diagnosticService.Items)
                {
                    if(!Managers(diagnosticService).TryGetValue(item.Id,out var current)){Console.WriteLine($"RELIABILITY TASK: paused={item.Paused}, diskPaused={item.LowSpacePaused}, manager=absent");continue;}
                    Console.WriteLine($"RELIABILITY DOWNLOAD: state={current.State}, progress={current.Progress:F2}, selectedProgress={current.PartialProgress:F2}, bytesReceived={current.Monitor.DataBytesReceived}, connections={current.OpenConnections}, hash={current.InfoHashes.V1?.ToHex()}, paused={item.Paused}, diskPaused={item.LowSpacePaused}, error={current.Error?.Exception?.Message}");
                }
            };
            await seeder.StartAsync();await Until(()=>seeder.State==TorrentState.Seeding,"recovery fixture is seeding real data");
            // Known .torrent size is checked before its manager starts.
            free=DownloadSpace.SafetyBytes;var blocked=await service.Add(torrent,Path.Combine(root,"space-blocked"));
            Check(blocked.Paused&&blocked.LowSpacePaused&&blocked.Hint.Contains("Освободи место")&&!service.EngineCreated,"insufficient disk budget adds a safely paused task without starting transfer");
            Check(notices.Count(n=>n.Kind==DownloadNoticeKind.LowSpace)==1,"preflight shortage emits one low-space notice");
            await service.Toggle(blocked);Check(blocked.LowSpacePaused&&notices.Count(n=>n.Kind==DownloadNoticeKind.LowSpace)==1,"retry with insufficient space retains pause reason without repeating the notice");
            var restored=new DownloadService(Settings("reliability-blocked-restore",Port()),downloadSpace:space);
            Check(restored.Items.Single().LowSpacePaused&&restored.Items.Single().Hint.Contains("Освободи место")&&restored.PendingResumeCount==0,"disk-pause reason survives restart and never becomes a startup auto-resume");await restored.Close();
            free=4L*1024*1024*1024;await service.Toggle(blocked);Check(!blocked.Paused&&!blocked.LowSpacePaused,"explicit Continue rechecks freed space and resumes the same payload");
            var manager=Managers(service)[blocked.Id];await Until(()=>manager.State==TorrentState.Downloading,"released disk pause enters actual download mode");
            manager.ConnectionAttemptFailed+=(_,failure)=>Console.WriteLine("RELIABILITY PEER FAILURE: "+failure.Reason);
            // Withhold one real file so even an immediate Windows loopback
            // burst cannot finish the whole torrent between two poll samples.
            var tail=manager.Files.Single(f=>f.Path.EndsWith("recovery-tail.bin",StringComparison.OrdinalIgnoreCase));
            await manager.SetFilePriorityAsync(tail,Priority.DoNotDownload);
            await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));
            await Until(()=>manager.Progress>0&&manager.Progress<100,"transfer receives real pieces before simulated network loss");
            // Freeze the receiving side before tearing down the peer. This
            // prevents queued loopback writes from completing a small fixture
            // while the seeder's asynchronous stop is still running.
            await manager.PauseAsync();await manager.StopAsync(TimeSpan.FromSeconds(2));
            await seed.StopAllAsync();service.SetNetworkAvailable(false);service.Update();
            Check(blocked.Status=="Нет подключения к сети"&&!blocked.Paused&&service.GetDiagnostics(blocked).NetworkAvailable==false,"network loss is explained while preserving active queue intent");
            // Another user-paused task must not be restarted with the active one.
            var paused=await service.Add("magnet:?xt=urn:btih:"+new string('b',40)+"&dn=manually-paused",Path.Combine(root,"reliability-paused"));await service.Toggle(paused);
            var removed=await service.Add("magnet:?xt=urn:btih:"+new string('c',40)+"&dn=removed",Path.Combine(root,"reliability-removed"));await service.Remove(removed);
            await Task.Delay(500);var before=manager.Progress;await Task.Delay(750);Check(manager.Progress==before,"stopped local peer sends no new payload while the network is offline");
            Check(await service.RecoverConnectionsAsync(false,force:true)==0,"offline signal never starts new connection attempts");
            // Restore the missing file while stopped, before the service's
            // recovery telemetry can observe a selected-files seeding state.
            await manager.SetFilePriorityAsync(tail,Priority.Normal);
            await seeder.StartAsync();await Until(()=>seeder.State==TorrentState.Seeding,"local peer becomes available after the outage");
            Check(await service.RecoverConnectionsAsync(true)==1,"network return reconnects exactly the active task");
            Check(paused.Paused&&!service.Items.Contains(removed),"automatic recovery preserves manual pauses and deleted tasks");
            await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));await Until(()=>manager.Progress==100,"recovered transfer finishes from retained pieces");service.Update();service.Update();
            var received=blocked.Files.Single(f=>f.Name.EndsWith("reliable.bin",StringComparison.OrdinalIgnoreCase)).FullPath;
            Check(SHA256.HashData(await LiveBytes(received)).SequenceEqual(SHA256.HashData(bytes)),"network recovery retains the selected infohash and produces byte-identical payload");
            var receivedTail=blocked.Files.Single(f=>f.Name.EndsWith("recovery-tail.bin",StringComparison.OrdinalIgnoreCase)).FullPath;
            Check(SHA256.HashData(await LiveBytes(receivedTail)).SequenceEqual(SHA256.HashData(tailBytes)),"network recovery downloads the explicitly retained missing file and verifies its SHA256");
            Check(notices.Count(n=>n.Kind==DownloadNoticeKind.Completed&&n.DownloadId==blocked.Id)==1,"real completion emits one notice despite repeated telemetry refreshes");
            // A link can disappear after a retry stopped its manager but before
            // it restarted. The task's active intent must remain recoverable.
            await manager.StopAsync(TimeSpan.FromSeconds(2));service.SetNetworkAvailable(false);
            Check(!blocked.Paused&&manager.State==TorrentState.Stopped,"interrupted restart fixture retains an active but stopped manager");
            Check(await service.RecoverConnectionsAsync(true)==1,"later network return rescues active stopped managers without a manual-pause bypass");
            service.SetNetworkAvailable(false);Check(await service.RecoverConnectionsAsync(true,autoRecover:false)==0,"disabled recovery still refreshes network observations without restarting tasks");
            var stopwatch=System.Diagnostics.Stopwatch.StartNew();await service.Close();Check(stopwatch.Elapsed<TimeSpan.FromSeconds(3),"recovery-capable service closes within its bounded shutdown budget");
            var startupNotices=new List<DownloadNotice>();var reopened=new DownloadService(Settings("reliability-finished-restore",Port()),downloadSpace:space);reopened.Notice+=startupNotices.Add;
            await reopened.ResumePendingAsync();await Until(()=>Managers(reopened)[blocked.Id].State==TorrentState.Seeding,"restored completed task validates its retained files");reopened.Update();Check(startupNotices.Count==0,"restored completed downloads do not create historical completion notifications");await reopened.Close();

            await CheckBlockedDiskClose(torrent);

            // Magnets receive metadata first. The fixed safety budget permits
            // metadata discovery, then the now-known payload size triggers pause.
            Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"reliability-magnet-state"));
            free=DownloadSpace.SafetyBytes;
            // This fixed synthetic volume has no payload allocation credited.
            // MonoTorrent may preallocate/extend real fixture files while the
            // metadata callback's disk check is queued. Logical file length
            // cannot represent allocation on this independent fake volume.
            var metadataSpace=new DownloadSpace(_=>new("fixture-volume",Volatile.Read(ref free)),_=>0);
            var magnetic=new DownloadService(Settings("reliability-metadata-cache",Port()),downloadLimitKbps:32,downloadSpace:metadataSpace);var magneticNotices=new List<DownloadNotice>();magnetic.Notice+=magneticNotices.Add;diagnosticService=magnetic;
            try
            {
                var magnet="magnet:?xt=urn:btih:"+seeder.InfoHashes.V1!.ToHex()+"&dn=reliable.bin";
                var pending=await magnetic.Add(magnet,Path.Combine(root,"reliability-metadata-download"));var metadata=Managers(magnetic)[pending.Id];
                await metadata.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));
                await Until(()=>pending.LowSpacePaused&&magneticNotices.Count(n=>n.Kind==DownloadNoticeKind.LowSpace)==1,"magnet pauses immediately after discovering a payload that exceeds the available budget");
                Check(pending.Files.Count==2&&pending.Paused&&pending.Progress<100&&magneticNotices.Count(n=>n.Kind==DownloadNoticeKind.LowSpace)==1,"magnet shortage retains file metadata and emits exactly one low-space notice");
                free=4L*1024*1024*1024;Check(await magnetic.RecoverConnectionsAsync(true,force:true)==0&&pending.Paused,"network recovery never resumes a task paused for disk space");
            }
            finally{await magnetic.Close();}
        }
        finally{await service.Close();await seed.StopAllAsync();Environment.SetEnvironmentVariable("KACHALKA_DATA",oldData);}

        async Task CheckBlockedDiskClose(string source)
        {
            var previous=Environment.GetEnvironmentVariable("KACHALKA_DATA");
            Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"reliability-blocking-io-state"));
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();bool block=false;
            var disk=new DownloadSpace(_=>
            {
                if(Volatile.Read(ref block)){entered.Set();release.Wait();}
                return new("slow-volume",4L*1024*1024*1024);
            },_=>0);
            var running=new DownloadService(Settings("reliability-blocking-io-cache",Port()),downloadSpace:disk);
            try
            {
                var transfer=await running.Add(source,Path.Combine(root,"reliability-blocking-io-download"));
                await Until(()=>Managers(running)[transfer.Id].State==TorrentState.Downloading,"blocked-I/O fixture starts an active transfer");
                Volatile.Write(ref block,true);var recovery=running.RecoverConnectionsAsync(true,force:true);
                Check(await Task.Run(()=>entered.Wait(TimeSpan.FromSeconds(5))),"recovery is waiting on a genuinely blocked disk query");
                var queuedAdd=running.Add("magnet:?xt=urn:btih:"+new string('e',40)+"&dn=queued-before-close",Path.Combine(root,"queued-before-close"));
                var watch=System.Diagnostics.Stopwatch.StartNew();await running.Close();
                Check(watch.Elapsed<TimeSpan.FromSeconds(3),"closing cancels a pending disk query without waiting for the removable/network drive");
                try{await recovery;throw new Exception("recovery continued after Close");}catch(OperationCanceledException){Console.WriteLine("PASS: canceled recovery cannot restart a transfer after shutdown");}
                try{await queuedAdd;throw new Exception("queued Add reopened the service during Close");}catch(OperationCanceledException){Console.WriteLine("PASS: a queued download cannot reopen the torrent engine during shutdown");}
                Check(running.Items.Count==1&&!running.EngineCreated,"closing retains the original queue without starting an older pending add");
                release.Set();
                var state=new DownloadService(Settings("reliability-blocking-restored-cache",Port()),downloadSpace:new DownloadSpace(_=>new("same",long.MaxValue),_=>0));
                Check(state.PendingResumeCount==1&&state.Items.Single().Paused,"close during recovery persists active intent for the next launch");await state.Close();
            }
            finally{release.Set();await running.Close();Environment.SetEnvironmentVariable("KACHALKA_DATA",previous);}
        }
    }
}

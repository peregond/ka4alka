using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Kachalka;
using Kachalka.Lan;
using MonoTorrent;
using MonoTorrent.Client;

static class DownloadLanTests
{
    public static async Task Run(string root)
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        EngineSettings Settings(string name)
        {
            var builder=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,name),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false};
            builder.ListenEndPoints.Clear();builder.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,Port()));return builder.ToSettings();
        }
        var previousData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        var state=Path.Combine(root,"lan-native-state");Environment.SetEnvironmentVariable("KACHALKA_DATA",state);
        var folder=Path.Combine(root,"lan-receiver-folder","Ka4alka");Directory.CreateDirectory(folder);
        var senderFolder=Path.Combine(root,"lan-sender-files");Directory.CreateDirectory(senderFolder);
        var seed=Path.Combine(senderFolder,"authored-lan-fixture.bin");await File.WriteAllBytesAsync(seed,RandomNumberGenerator.GetBytes(32*1024));
        var torrent=Path.Combine(senderFolder,"user-original.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(seed),torrent);
        var original=await File.ReadAllBytesAsync(torrent);
        var media=new MediaItem(73901,"Передача на другой ноутбук","Фильмы","драма, комедия",2026,"8.3","7.8","#526B69")
        {PageUrl="https://w6.zona.plus/movies/authored-lan-fixture",ImageUrl="https://img1.zonapic.com/authored-lan-poster.jpg"};
        var release=new SourceEntry("authored-release","Authored LAN fixture 1080p WEB-DL","Локальный тест","https://example.org/authored-release",null,null,32*1024,2);
        var payload=await LanDownloadPayload.CreateAsync(torrent,media,release);
        var wire=JsonSerializer.Serialize(payload);
        Check(payload.TorrentBytes!.SequenceEqual(original)&&payload.Magnet==null&&!wire.Contains(senderFolder)&&!wire.Contains(folder),"LAN payload contains authored torrent bytes and catalog metadata without either computer's filesystem paths");
        var sender=Guid.NewGuid();var fingerprint=new string('A',64);
        var request=payload with{SenderId=sender,SenderFingerprint=fingerprint};
        var prefs=new Preferences{Folder=folder,FolderConfigured=true};
        var inbox=Path.Combine(state,"lan-inbox");
        DownloadService service=new(Settings("lan-native-cache"),publicTrackers:[]);
        try
        {
            var receiver=new LanDownloadReceiver(service,prefs,inbox);
            var unsigned=await receiver.ReceiveAsync(payload);
            Check(!unsigned.Accepted&&service.Items.Count==0&&!service.EngineCreated,"native LAN receiver refuses requests without an authenticated sender before creating a torrent engine");
            prefs.FolderConfigured=false;
            var unconfigured=await receiver.ReceiveAsync(request);
            Check(!unconfigured.Accepted&&service.Items.Count==0&&!service.EngineCreated,"remote download requires an already configured receiver folder and never opens a folder picker");
            prefs.FolderConfigured=true;prefs.Folder="relative-lan-download-folder";
            var relative=await receiver.ReceiveAsync(request);
            Check(!relative.Accepted&&service.Items.Count==0&&!Directory.Exists(prefs.Folder),"remote download refuses a relative persisted receiver folder without creating it");
            prefs.Folder=Path.Combine(root,"missing-receiver-folder","Ka4alka");
            var missing=await receiver.ReceiveAsync(request);
            Check(!missing.Accepted&&service.Items.Count==0&&!Directory.Exists(prefs.Folder),"remote download refuses a missing configured receiver folder without silently replacing the user's choice");
            prefs.Folder=folder;
            var invalid=await receiver.ReceiveAsync(request with{RequestId=Guid.NewGuid(),TorrentBytes=[1,2,3]});
            var ambiguous=await receiver.ReceiveAsync(request with{RequestId=Guid.NewGuid(),Magnet="magnet:?xt=urn:btih:"+new string('b',40)});
            Check(!invalid.Accepted&&!ambiguous.Accepted&&service.Items.Count==0&&!service.EngineCreated,"invalid torrent bytes and ambiguous magnet-plus-file requests do not enter the native queue");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();
                try{await receiver.ReceiveAsync(request,cancellation.Token);throw new Exception("Canceled remote reception accepted.");}
                catch(OperationCanceledException){Check(service.Items.Count==0,"canceled reception cannot add a native queue entry");}
            }
            var receipt=await receiver.ReceiveAsync(request);
            var item=service.Items.Single();
            Check(receipt.Accepted&&receipt.RequestId==request.RequestId&&receipt.DownloadId==item.Id&&item.Folder==folder,"native LAN acknowledgement identifies an actual MonoTorrent queue entry in the receiver's chosen Ka4alka folder");
            Check(item.DisplayName==media.Title&&item.Name==Path.GetFileName(seed)&&item.MediaPageUrl==media.PageUrl&&item.ImageUrl==media.ImageUrl&&item.MediaSection=="Фильмы"&&item.MediaYear==2026&&item.ReleaseTitle==release.Title&&item.ReleaseSource==release.Source,"native reception preserves the Russian card title, poster, type, year, card link and separate raw torrent name");
            Check(service.EngineCreated&&File.Exists(item.Source)&&item.Source!=torrent&&Torrent.Load(item.Source).InfoHashes.V1!.Equals(Torrent.Load(torrent).InfoHashes.V1),"received torrent is validated and durably cached before acknowledgement, independently of the sender's original file");
            Check(!Directory.EnumerateFiles(inbox).Any()&&File.ReadAllBytes(torrent).SequenceEqual(original),"received staging file is cleaned and the sender's original torrent remains intact");
            var saved=JsonSerializer.Deserialize<List<DownloadItem>>(await File.ReadAllTextAsync(Path.Combine(state,"queue.json")))!.Single();
            Check(saved.RemoteSenderId==sender.ToString("N")&&saved.RemoteRequestId==request.RequestId.ToString("N")&&saved.RemoteSenderFingerprint==fingerprint&&saved.RemoteContentDigest?.Length==64,"authenticated sender identity, request ID and content digest are saved in the first durable queue entry before success is acknowledged");
            var repeated=await Task.WhenAll(receiver.ReceiveAsync(request),receiver.ReceiveAsync(request));
            Check(repeated.All(value=>value.Accepted&&value.DownloadId==item.Id)&&service.Items.Count==1,"concurrent retries return the original native receipt without adding duplicate downloads");
            var duplicateHash=await receiver.ReceiveAsync(request with{RequestId=Guid.NewGuid()});
            Check(duplicateHash.Accepted&&duplicateHash.DownloadId==item.Id&&service.Items.Count==1,"another remote request for the same actual torrent hash acknowledges the existing native queue item");
            await service.Toggle(item);await service.Close();
            service=new(Settings("lan-native-restored-cache"),publicTrackers:[]);
            receiver=new(service,prefs,inbox);
            var restarted=await receiver.ReceiveAsync(request);
            Check(restarted.Accepted&&restarted.DownloadId==item.Id&&service.Items.Count==1&&!service.EngineCreated&&service.Items.Single().DisplayName==media.Title,"durable queue identity prevents a retry from duplicating work after restart even without a core receipt ledger");
            var conflicting=await receiver.ReceiveAsync(request with{Title="Подменённое описание"});
            Check(!conflicting.Accepted&&service.Items.Count==1&&!service.EngineCreated,"persisted native request digest refuses a conflicting payload with the same authenticated request ID even after restart");
            Check(File.Exists(service.Items.Single().Source)&&File.ReadAllBytes(torrent).SequenceEqual(original),"receiver restart retains its own usable torrent metadata and never depends on the sender's file path");
            var invalidSource=Path.Combine(senderFolder,"invalid-source.torrent");await File.WriteAllTextAsync(invalidSource,"not a torrent");
            try{await LanDownloadPayload.CreateAsync(invalidSource);throw new Exception("Invalid outgoing torrent accepted.");}
            catch(Exception error)when(error.Message!="Invalid outgoing torrent accepted."){Check(service.Items.Count==1,"sender validates an authored torrent before preparing a remote request");}
            var oversizedSource=Path.Combine(senderFolder,"oversized-source.torrent");
            using(var large=new FileStream(oversizedSource,FileMode.CreateNew))large.SetLength(10*1024*1024+1);
            try{await LanDownloadPayload.CreateAsync(oversizedSource);throw new Exception("Oversized outgoing torrent accepted.");}
            catch(InvalidDataException){Check(service.Items.Count==1,"sender refuses a torrent file above the transfer limit before reading it into a remote payload");}
            var privateMetadata=request with{RequestId=Guid.NewGuid(),TorrentBytes=null,Magnet="magnet:?xt=urn:btih:"+new string('c',40),
                Media=request.Media! with{PageUrl="https://192.168.1.1/private-admin",ImageUrl="https://127.0.0.1/private-image"}};
            var sanitized=await receiver.ReceiveAsync(privateMetadata);var sanitizedItem=service.Items.Single(value=>value.Id==sanitized.DownloadId);
            Check(sanitized.Accepted&&sanitizedItem.DisplayName==media.Title&&sanitizedItem.MediaPageUrl==null&&sanitizedItem.ImageUrl==null&&!sanitizedItem.HasMediaCard,"paired metadata cannot make the receiving app fetch a private image or open a private non-catalog movie page");
        }
        finally{await service.Close();Environment.SetEnvironmentVariable("KACHALKA_DATA",previousData);}
    }
}

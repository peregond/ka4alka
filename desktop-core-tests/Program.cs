using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Kachalka;
using MonoTorrent;
using MonoTorrent.Client;
var root=Path.Combine(Path.GetTempPath(),"kachalka-portable-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
// macOS exposes /var through a system symlink; the deletion tests intentionally
// reject symlink ancestors, so use the canonical physical temporary directory.
if(OperatingSystem.IsMacOS()&&root.StartsWith("/var/",StringComparison.Ordinal))
    root=Path.Combine(new DirectoryInfo("/var").ResolveLinkTarget(true)!.FullName,root[5..]);
Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"state"));
try {
 await MissingDownloadFilesTests.Run(root);
 var upper=Path.Combine(root,"CaseFolder");var lower=Path.Combine(root,"casefolder");
 Directory.CreateDirectory(upper);Directory.CreateDirectory(lower);
 if(!OperatingSystem.IsWindows()) {
   try{DownloadFiles.ValidatePath(upper,Path.Combine(lower,"outside.bin"));throw new Exception("Different case path escaped deletion root");}catch(IOException){}
   Console.WriteLine("PASS: case-sensitive volume paths cannot escape the download folder");
 }
void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
int FreePort(){var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();int port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();return port;}
EngineSettings Settings(string name,int port){var b=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,name),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,MaximumConnections=10,DiskCacheBytes=1024*1024};b.ListenEndPoints.Clear();b.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,port));return b.ToSettings();}
async Task Until(Func<bool> condition,string message){var until=DateTime.UtcNow.AddSeconds(25);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(100);Check(condition(),message);}
var seedDir=Path.Combine(root,"seed");Directory.CreateDirectory(seedDir);var payload=RandomNumberGenerator.GetBytes(256*1024);var original=Path.Combine(seedDir,"test.bin");await File.WriteAllBytesAsync(original,payload);
var torrentPath=Path.Combine(root,"test.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(original),torrentPath);
var port=FreePort();using var seed=new ClientEngine(Settings("seed-cache",port));var seedManager=await seed.AddAsync(torrentPath,seedDir);await seedManager.StartAsync();await Until(()=>seedManager.State==TorrentState.Seeding,"local seeder ready");
var service=new DownloadService(Settings("download-cache",FreePort()));
try {
 await service.SetLimitsAsync(1024,512);Check(!service.EngineCreated,"speed limits configured without creating idle engine");
 var media=new MediaItem(9901,"Передача в локальной сети","Сериалы","драма",2026,"8.0","8.0","#526B69"){ImageUrl="https://example.test/original-poster.jpg",PageUrl="https://example.test/media/local-network"};
 var selectedRelease=new SourceEntry("fixture-release","Передача в локальной сети (2026) FullHD WEB-DL","Локальный тест","https://example.test/fixture-release",null,"");
 var poster=media.ImageUrl!;var beforeAdded=DateTime.UtcNow;
 await service.Add(torrentPath,Path.Combine(root,"download"),media,selectedRelease.ImageUrl,selectedRelease);Check(service.Items.Count==1,"torrent enters actual queue");
 var added=service.Items.Single().AddedUtc;
 Check(added>=beforeAdded&&added<=DateTime.UtcNow&&service.Items[0].MediaTitle==media.Title&&service.Items[0].MediaSection==media.Section&&service.Items[0].ImageUrl==poster,"adding a torrent with an empty source poster retains the selected catalog poster");
 Check(service.Items[0].MediaPageUrl==media.PageUrl&&service.Items[0].MediaYear==media.Year&&service.Items[0].ReleaseTitle==selectedRelease.Title&&service.Items[0].Name=="test.bin","new download keeps Russian card metadata and the actual raw torrent filename separately");
 var magnet="magnet:?xt=urn:btih:"+seedManager.InfoHashes.V1!.ToHex()+"&dn=test.bin";
 try{await service.Add(magnet,Path.Combine(root,"same-torrent"));throw new Exception("same torrent via magnet accepted");}catch(InvalidOperationException){Console.WriteLine("PASS: magnet and .torrent share duplicate identity");}
 Check(service.Items.Count==1,"duplicate did not alter queue");
 var map=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
 var manager=map.Values.Single();
 var engine=(ClientEngine)typeof(DownloadService).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
 Check(engine.Settings.MaximumDownloadRate==1024*1024&&engine.Settings.MaximumUploadRate==512*1024,"configured limits applied on engine creation");
 await service.SetLimitsAsync(2048,1024);Check(engine.Settings.MaximumDownloadRate==2048*1024&&engine.Settings.MaximumUploadRate==1024*1024,"speed limits apply to running engine without restart");
 await Until(()=>manager.State==TorrentState.Downloading,"torrent starts waiting for peers");
 service.Items[0].LastStartedUtc=DateTime.UtcNow.AddSeconds(-30);service.Update();
 Check(service.Items[0].Status=="Ожидание участников"&&service.Items[0].Hint.Contains("никто не подключён"),"stalled download explains missing peers");
 await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
 await Until(()=>manager.Progress==100,"BitTorrent transfer reaches 100%");
 service.Update();Check(service.Items[0].Hint.Length==0,"peer warning clears after transfer");
 Check(service.Items[0].Status=="Готово · раздаётся"&&!service.Items[0].Stats.Contains("↓")&&service.Items[0].Remaining=="","completed real transfer shows no stale download speed or ETA");
 Check(service.Items[0].Files.Count==1&&service.Items[0].Files[0].Progress==100,"completed per-file progress available in download details");
 await service.Add("magnet:?xt=urn:btih:"+new string('f',40)+"&dn=unfinished",Path.Combine(root,"unfinished"));
 var unfinished=service.Items.Single(x=>x.Source.StartsWith("magnet:"));
 Check(await service.SetGroupAsync(true,true)==1&&service.Items[0].Paused&&!unfinished.Paused,"stop seeding affects completed tasks only");
 Check(await service.SetGroupAsync(false,true)==1&&unfinished.Paused,"stop downloads affects unfinished tasks only");
 Check(await service.SetGroupAsync(true,false)==1&&!service.Items[0].Paused&&unfinished.Paused,"start seeding leaves unfinished tasks paused");
 Check(await service.SetGroupAsync(false,false)==1&&!unfinished.Paused,"start downloads resumes unfinished tasks");
 await service.Remove(unfinished);await service.Toggle(service.Items[0]);Check(service.Items[0].Paused,"pause stops torrent");
 var received=Directory.GetFiles(Path.Combine(root,"download"),"test.bin",SearchOption.AllDirectories).Single();Check(SHA256.HashData(await File.ReadAllBytesAsync(received)).SequenceEqual(SHA256.HashData(payload)),"download bytes match SHA256");
 try{await service.Add(torrentPath,Path.Combine(root,"download"));throw new Exception("duplicate accepted");}catch(InvalidOperationException){Console.WriteLine("PASS: duplicate rejected");}
 var restored=new DownloadService(Settings("restore-cache",FreePort()));Check(restored.Items.Count==1&&restored.Items[0].Paused,"queue restores paused without network");
 Check(restored.Items[0].AddedUtc==added&&restored.Items[0].MediaTitle==media.Title&&restored.Items[0].MediaSection==media.Section&&restored.Items[0].ImageUrl==poster,"queue restore retains the original addition date, title, type and poster");
 Check(restored.Items[0].MediaPageUrl==media.PageUrl&&restored.Items[0].MediaYear==media.Year&&restored.Items[0].ReleaseTitle==selectedRelease.Title&&restored.Items[0].Name=="test.bin","card identity and separate release labels persist through a real queue restart");
 Check(restored.Items[0].Files.Count==1&&restored.Items[0].Files[0].Progress==100,"saved file list and per-file progress survive restart");
 await service.Toggle(service.Items[0]);Check(!service.Items[0].Paused,"resume restarts torrent");
 Check(service.Items[0].AddedUtc==added,"resuming a torrent does not change its addition order");
 await service.Remove(service.Items[0]);Check(File.Exists(received)&&service.Items.Count==0,"remove keeps downloaded files");
 await service.Add(magnet,Path.Combine(root,"magnet-download"),media,"https://example.test/source-poster.jpg");Check(service.Items.Single().ImageUrl==media.ImageUrl,"selected catalog artwork takes precedence over a torrent source poster");var magnetManager=map.Values.Single();await magnetManager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
 await Until(()=>magnetManager.Progress==100,"magnet transfer reaches 100%");await service.Toggle(service.Items[0]);
 var magnetFile=Directory.GetFiles(Path.Combine(root,"magnet-download"),"test.bin",SearchOption.AllDirectories).Single();Check(SHA256.HashData(await File.ReadAllBytesAsync(magnetFile)).SequenceEqual(SHA256.HashData(payload)),"magnet download matches SHA256");
 try{await service.Add("magnet:?xt=invalid",Path.Combine(root,"download"));throw new Exception("invalid magnet accepted");}catch(FormatException){Console.WriteLine("PASS: invalid magnet rejected");}
 Check(service.Items.Count==1,"invalid link did not alter queue");
 var keep=Path.Combine(service.Items[0].Folder,"unrelated.txt");await File.WriteAllTextAsync(keep,"keep");
 var escaped=false;try{DownloadFiles.ValidatePath(service.Items[0].Folder,keep+"/../../escape");}catch(IOException){escaped=true;}
 Check(escaped,"file deletion rejects paths outside download folder");
 await service.Remove(service.Items[0],true);Check(!File.Exists(magnetFile)&&File.Exists(keep)&&service.Items.Count==0,"delete with files removes only torrent payload and preserves unrelated files");

}finally{await service.Close();await seedManager.StopAsync();}
}finally{try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}

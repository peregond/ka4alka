using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using MonoTorrent;
using MonoTorrent.Client;
using Kachalka;
var root=Path.GetFullPath(Path.Combine("test-output","transfer-"+Guid.NewGuid().ToString("N")));
try
{
Directory.CreateDirectory(root);Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"state"));
if(args.Contains("--verify-failure-report"))throw new InvalidOperationException("Intentional test-runner failure probe.");
ErrorLog.Write(new InvalidOperationException("diagnostic-test",new IOException("nested-detail")));
var diagnosticPath=Path.Combine(Preferences.DataDir,"error.log");
if(!File.ReadAllText(diagnosticPath).Contains("nested-detail"))throw new Exception("Diagnostic log lost exception details.");
File.WriteAllText(diagnosticPath,new string('x',1024*1024+1));ErrorLog.Write(new Exception("after-rotation"));
if(!File.Exists(diagnosticPath+".previous")||!File.ReadAllText(diagnosticPath).Contains("after-rotation"))throw new Exception("Diagnostic log rotation failed.");
Console.WriteLine("PASS: exception diagnostics retain details and rotate oversized log");
if(args.Contains("--probe-public"))
{
 using var sourceClient=new SourceClient();
 var release=await sourceClient.ResolveArchive(new SourceEntry("BigBuckBunny_328","Big Buck Bunny","Internet Archive","https://archive.org/details/BigBuckBunny_328",null,null),CancellationToken.None);
 var probeTorrentPath=await sourceClient.TorrentFile(release,CancellationToken.None);
 Console.WriteLine($"Public probe: source={release.Source}, size={new FileInfo(probeTorrentPath).Length} torrent bytes");
 var probe=new DownloadService(new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,"public-cache"),MaximumDownloadRate=256*1024,AllowPortForwarding=false}.ToSettings());
 try
 {
  await probe.Add(probeTorrentPath,Path.Combine(root,"public-download"));
  var probeManagers=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(probe)!;
  for(var i=0;i<80;i++)
  {
   await Task.Delay(500);probe.Update();var manager=probeManagers.Values.Single();
   if(i%10==9)Console.WriteLine($"{(i+1)/2}s state={manager.State} progress={manager.Progress:F2}% peers={manager.OpenConnections} rate={manager.Monitor.DownloadRate} B/s bytes={manager.Monitor.DataBytesReceived}");
   if(manager.Monitor.DataBytesReceived>1024*1024)break;
  }
  var finalManager=probeManagers.Values.Single();
  var result=new{BytesReceived=finalManager.Monitor.DataBytesReceived,Progress=finalManager.Progress,Connections=finalManager.OpenConnections,State=finalManager.State.ToString(),Status=probe.Items.Single().Status,Stats=probe.Items.Single().Stats,FirstFileBytesReceived=finalManager.Monitor.DataBytesReceived>0};
  await File.WriteAllTextAsync(Path.Combine(root,"public-probe.json"),System.Text.Json.JsonSerializer.Serialize(result,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
  if(!result.FirstFileBytesReceived)Environment.ExitCode=2;
 }
 finally{await probe.Close();}
 return;
}
DownloadTests.Run();
await CatalogBatchTests.Run();
CatalogPagingTests.Run();
if(args.Contains("--catalog-only"))return;
await ReleaseSearchTests.Run();
await SourceTests.Run(args.Contains("--live-sources")||args.Contains("--all-live-sources"),args.Contains("--all-live-sources"));
void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
int FreePort(){var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();int port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();return port;}
EngineSettings Settings(string name,int port){var b=new EngineSettingsBuilder{CacheDirectory=Path.Combine(root,name),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,MaximumConnections=10,DiskCacheBytes=1024*1024};b.ListenEndPoints.Clear();b.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,port));return b.ToSettings();}
async Task Until(Func<bool> condition,string message){var until=DateTime.UtcNow.AddSeconds(25);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(100);Check(condition(),message);}
var seedDir=Path.Combine(root,"seed");Directory.CreateDirectory(seedDir);var payload=RandomNumberGenerator.GetBytes(256*1024);var original=Path.Combine(seedDir,"test.bin");await File.WriteAllBytesAsync(original,payload);
var torrentPath=Path.Combine(root,"test.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(original),torrentPath);
var port=FreePort();using var seed=new ClientEngine(Settings("seed-cache",port));var seedManager=await seed.AddAsync(torrentPath,seedDir);await seedManager.StartAsync();await Until(()=>seedManager.State==TorrentState.Seeding,"local seeder ready");
var service=new DownloadService(Settings("download-cache",FreePort()));
try {
 await service.Add(torrentPath,Path.Combine(root,"download"));Check(service.Items.Count==1,"torrent enters actual queue");
 var magnet="magnet:?xt=urn:btih:"+seedManager.InfoHashes.V1!.ToHex()+"&dn=test.bin";
 try{await service.Add(magnet,Path.Combine(root,"same-torrent"));throw new Exception("same torrent via magnet accepted");}catch(InvalidOperationException){Console.WriteLine("PASS: magnet and .torrent share duplicate identity");}
 Check(service.Items.Count==1,"duplicate did not alter queue");
 var map=(Dictionary<string,TorrentManager>)typeof(DownloadService).GetField("managers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
 var manager=map.Values.Single();await Until(()=>manager.State==TorrentState.Downloading,"torrent starts waiting for peers");
 service.Items[0].LastStartedUtc=DateTime.UtcNow.AddSeconds(-30);service.Update();
 Check(service.Items[0].Status=="Ожидание участников"&&service.Items[0].Hint.Contains("никто не подключён"),"stalled download explains missing peers");
 await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
 await Until(()=>manager.Progress==100,"BitTorrent transfer reaches 100%");
 service.Update();Check(service.Items[0].Hint.Length==0,"peer warning clears after transfer");
 Check(service.Items[0].Status=="Готово · раздаётся"&&!service.Items[0].Stats.Contains("↓")&&service.Items[0].Remaining=="","completed real transfer shows no stale download speed or ETA");
 await service.Toggle(service.Items[0]);Check(service.Items[0].Paused,"pause stops torrent");
 var received=Directory.GetFiles(Path.Combine(root,"download"),"test.bin",SearchOption.AllDirectories).Single();Check(SHA256.HashData(await File.ReadAllBytesAsync(received)).SequenceEqual(SHA256.HashData(payload)),"download bytes match SHA256");
 try{await service.Add(torrentPath,Path.Combine(root,"download"));throw new Exception("duplicate accepted");}catch(InvalidOperationException){Console.WriteLine("PASS: duplicate rejected");}
 var restored=new DownloadService(Settings("restore-cache",FreePort()));Check(restored.Items.Count==1&&restored.Items[0].Paused,"queue restores paused without network");
 await service.Toggle(service.Items[0]);Check(!service.Items[0].Paused,"resume restarts torrent");
 await service.Remove(service.Items[0]);Check(File.Exists(received)&&service.Items.Count==0,"remove keeps downloaded files");
 await service.Add(magnet,Path.Combine(root,"magnet-download"));var magnetManager=map.Values.Single();await magnetManager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
 await Until(()=>magnetManager.Progress==100,"magnet transfer reaches 100%");await service.Toggle(service.Items[0]);
 var magnetFile=Directory.GetFiles(Path.Combine(root,"magnet-download"),"test.bin",SearchOption.AllDirectories).Single();Check(SHA256.HashData(await File.ReadAllBytesAsync(magnetFile)).SequenceEqual(SHA256.HashData(payload)),"magnet download matches SHA256");
 try{await service.Add("magnet:?xt=invalid",Path.Combine(root,"download"));throw new Exception("invalid magnet accepted");}catch(FormatException){Console.WriteLine("PASS: invalid magnet rejected");}
 Check(service.Items.Count==1,"invalid link did not alter queue");
 var invalidTorrent=Path.Combine(root,"invalid.torrent");await File.WriteAllTextAsync(invalidTorrent,"not a torrent");
 try{await service.Add(invalidTorrent,Path.Combine(root,"download"));throw new Exception("invalid torrent accepted");}catch(Exception error)when(error is not InvalidOperationException||error.Message!="invalid torrent accepted"){Console.WriteLine("PASS: invalid torrent rejected");}
 Check(service.Items.Count==1,"invalid torrent did not alter queue");

 Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"auto-state"));
 var autoService=new DownloadService(Settings("auto-cache",FreePort()));
 await autoService.Add(torrentPath,Path.Combine(root,"auto-download"));
 await autoService.Close();
 var autoRestored=new DownloadService(Settings("auto-restore-cache",FreePort()));
 Check(autoRestored.PendingResumeCount==1&&autoRestored.Items.Single().Paused,"active task queued for startup resume");
 Check(await autoRestored.ResumePendingAsync()==1&&!autoRestored.Items.Single().Paused,"startup resumes active task");
 await autoRestored.Toggle(autoRestored.Items.Single());await autoRestored.Close();
 var pausedRestored=new DownloadService(Settings("paused-restore-cache",FreePort()));
 Check(pausedRestored.PendingResumeCount==0&&pausedRestored.Items.Single().Paused,"deliberately paused task stays paused");
 Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"state"));
 Check(Catalog.Releases.Count(r=>r.Quality=="2160p")==3,"quality filter data");
 Check(Catalog.Releases.All(r=>r.Quality!="2160p"||r.Format!="MP4"),"combined empty filter case");
}finally{await service.Close();await seed.StopAllAsync();}
Console.WriteLine("All integration checks passed. "+root);
}
catch(Exception error)
{
 Console.Error.WriteLine(error);
 var report=Path.Combine(root,"failure.txt");
 try{await File.WriteAllTextAsync(report,error.ToString());Console.Error.WriteLine("Failure report: "+report);}
 catch(Exception writeError){Console.Error.WriteLine("Cannot write failure report: "+writeError.Message);}
 Environment.ExitCode=1;
}

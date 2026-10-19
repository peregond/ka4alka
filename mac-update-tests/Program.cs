using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka.MacUpdates;
using Kachalka.Updates;
var root=Path.Combine(Path.GetTempPath(),"kachalka-mac-update-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);var checks=new List<string>();
void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
async Task Reject(Func<Task> test,string name){try{await test();}catch(Exception e)when(e is InvalidDataException or IOException or CryptographicException){checks.Add(name);return;}throw new Exception("Accepted unsafe update: "+name);}
try
{
    const string version="0.44.0";using var key=RSA.Create(2048);var publicKey=key.ExportSubjectPublicKeyInfoPem();
    var archive=Path.Combine(root,"app.zip");var installed=Path.Combine(root,"installed");Directory.CreateDirectory(installed);
    var files=new Dictionary<string,byte[]>{["Contents/Info.plist"]=Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><plist><dict><key>CFBundleIdentifier</key><string>org.ka4alka.app</string><key>CFBundleShortVersionString</key><string>0.44.0</string><key>CFBundleExecutable</key><string>Ka4alka</string></dict></plist>"),["Contents/MacOS/Ka4alka"]=Encoding.UTF8.GetBytes("new UI"),["Contents/Resources/engine/Kachalka.Engine"]=Encoding.UTF8.GetBytes("engine"),["Contents/Resources/engine/Kachalka.Engine.dll"]=Encoding.UTF8.GetBytes("shared engine"),["Contents/Resources/engine/Kachalka.DesktopCore.dll"]=RandomNumberGenerator.GetBytes(96*1024),["Contents/Resources/update-public.pem"]=Encoding.UTF8.GetBytes(publicKey)};
    using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))foreach(var pair in files){var entry=zip.CreateEntry("Ka4alka.app/"+pair.Key,CompressionLevel.SmallestSize);entry.ExternalAttributes=(0x8000|(pair.Key.EndsWith("Ka4alka")||pair.Key.EndsWith("Kachalka.Engine")?493:420))<<16;using var stream=entry.Open();stream.Write(pair.Value);}
    foreach(var pair in files){var path=Path.Combine(installed,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,pair.Key.EndsWith("MacOS/Ka4alka")?Encoding.UTF8.GetBytes("old UI"):pair.Value);}
    var catalog=MacComponents.Create(archive,version);var index=JsonSerializer.SerializeToUtf8Bytes(catalog,json);var zipBytes=File.ReadAllBytes(archive);
    string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    MacPackage Package(string arch)=>new($"https://github.com/peregond/ka4alka/releases/download/v{version}/Kachalka-macos-{arch}.zip",$"Kachalka-macos-{arch}.zip",zipBytes.Length,Hash(zipBytes),new($"https://github.com/peregond/ka4alka/releases/download/v{version}/macos-components-{arch}.json",index.Length,Hash(index)));
    var manifest=new MacManifest(1,"kachalka","stable",version,"macos","14.0",new(){["arm64"]=Package("arm64"),["x64"]=Package("x64")},"https://github.com/peregond/ka4alka/releases/tag/v"+version);
    var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest,json);var signature=key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    Check(MacManifest.Verify(bytes,signature,publicKey).Version==version,"Signed Mac manifest matches release version");
    var invalid=(byte[])bytes.Clone();invalid[0]^=1;await Reject(()=>Task.Run(()=>MacManifest.Verify(invalid,signature,publicKey)),"Reject altered manifest");
    var unsafeCatalog=catalog with{Files=catalog.Files.Select((file,i)=>i==0?file with{Path="Contents/../../outside"}:file).ToArray()};await Reject(()=>Task.Run(()=>unsafeCatalog.Validate(Package("arm64"),version)),"Reject component path traversal");
    await Reject(()=>Task.Run(()=>MacUpdateClient.PathFor(installed,"../outside")),"Reject filesystem path escape");
    var duplicate=catalog with{Files=[..catalog.Files,catalog.Files[0] with{Path=catalog.Files[0].Path.ToUpperInvariant()}]};await Reject(()=>Task.Run(()=>duplicate.Validate(Package("arm64"),version)),"Reject case-folded duplicate paths");
    // Functional HTTP ranges and hash verification, rather than a mocked update plan.
    if(!OperatingSystem.IsMacOS())
    {
        using var transport=new FixtureTransport(index,zipBytes);using var client=new MacUpdateClient(publicKey,transport);
        var result=await client.PrepareAsync(bytes,signature,"arm64",installed,Path.Combine(root,"delta"));
        Check(result.ReusedFiles==files.Count-1&&!result.FullPackage&&result.DownloadedBytes<zipBytes.Length/10,"Only changed UI bytes download; the shared engine is reused");
        Check(File.ReadAllBytes(Path.Combine(result.Application,"Contents/MacOS/Ka4alka")).SequenceEqual(files["Contents/MacOS/Ka4alka"]),"Candidate contains verified new UI");
        using var fullTransport=new FixtureTransport(index,zipBytes){NoRanges=true};using var fallback=new MacUpdateClient(publicKey,fullTransport);
        var full=await fallback.PrepareAsync(bytes,signature,"x64",installed,Path.Combine(root,"full"));Check(full.FullPackage&&full.DownloadedBytes==zipBytes.Length,"Full package fallback is hash verified when ranges are unavailable");
        using var corruptTransport=new FixtureTransport(index,zipBytes){Corrupt=true};using var corrupt=new MacUpdateClient(publicKey,corruptTransport);
        await Reject(()=>corrupt.PrepareAsync(bytes,signature,"arm64",installed,Path.Combine(root,"corrupt")),"Reject corrupted range contents");Check(!Directory.Exists(Path.Combine(root,"corrupt")),"Failed update staging is removed");
        using var shortTransport=new FixtureTransport(index,zipBytes){Truncate=true};using var truncated=new MacUpdateClient(publicKey,shortTransport);
        await Reject(()=>truncated.PrepareAsync(bytes,signature,"arm64",installed,Path.Combine(root,"truncated")),"Reject truncated range contents");
        using var cancelTransport=new FixtureTransport(index,zipBytes);using var cancelled=new MacUpdateClient(publicKey,cancelTransport);using var ct=new CancellationTokenSource();ct.Cancel();
        try{await cancelled.PrepareAsync(bytes,signature,"arm64",installed,Path.Combine(root,"cancelled"),ct:ct.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){checks.Add("Cancellation leaves installed application unchanged");}
    }
    Check(File.ReadAllText(Path.Combine(installed,"Contents/MacOS/Ka4alka"))=="old UI","Preparation never replaces the running application");
    foreach(var check in checks)Console.WriteLine("PASS "+check);
}
finally{Directory.Delete(root,true);}
sealed class FixtureTransport(byte[] index,byte[] zip):HttpMessageHandler
{
    public bool NoRanges,Corrupt,Truncate;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(request.RequestUri!.AbsolutePath.EndsWith(".json"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(index)});
        if(NoRanges||request.Headers.Range==null)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(zip)});
        var range=request.Headers.Range.Ranges.Single();var from=range.From!.Value;var to=range.To!.Value;var bytes=zip[(int)from..((int)to+1)];
        if(Corrupt&&bytes.Length>0)bytes[0]^=0xff;
        if(Truncate&&bytes.Length>0)bytes=bytes[..^1];
        var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(bytes)};response.Content.Headers.ContentRange=new ContentRangeHeaderValue(from,to,zip.Length);return Task.FromResult(response);
    }
}

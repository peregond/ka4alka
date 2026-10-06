using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka.Updates;

var root = Path.Combine(Path.GetTempPath(), "kachalka-update-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
if (args.Length == 2 && args[0] == "--verify-release")
{
    try
    {
        var release=Path.GetFullPath(args[1]);
        var manifest=UpdateManifest.Verify(File.ReadAllBytes(Path.Combine(release,"latest.json")),File.ReadAllBytes(Path.Combine(release,"latest.sig")),UpdateTrust.PublicKey);
        UpdateArchive.ExtractVerified(Path.Combine(release,"Kachalka-win-x64.zip"),Path.Combine(root,"release"),manifest);
        Console.WriteLine("PASS: published package signature, size, SHA256, archive paths and assembly version "+manifest.Version);
        return 0;
    }
    catch(Exception e){Console.Error.WriteLine(e);return 1;}
    finally{Directory.Delete(root,true);}
}
int checks = 0;
using var key = RSA.Create(2048);
var pub = key.ExportSubjectPublicKeyInfoPem();
UpdateManifest Manifest(byte[] payload) => new(1, "kachalka", "stable", "0.20.0", "win-x64", "10",
    new($"https://github.com/{UpdateManifest.Repository}/releases/download/v0.20.0/Kachalka-win-x64.zip", "Kachalka-win-x64.zip", payload.Length, Convert.ToHexString(SHA256.HashData(payload)), "portable-zip"),
    $"https://github.com/{UpdateManifest.Repository}/releases/tag/v0.20.0", true);
UpdateOffer Sign(UpdateManifest m)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(m);
    return new(m, bytes, key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
async Task Reject(Func<Task> action, string label)
{
    bool rejected = false;
    try { await action(); } catch (Exception e) when (e is InvalidDataException or IOException or CryptographicException or OperationCanceledException or HttpRequestException) { rejected = true; }
    Check(rejected, label);
}
try
{
    var payload = Encoding.UTF8.GetBytes("release fixture"); var offer = Sign(Manifest(payload));
    Check(UpdateManifest.Verify(offer.ManifestBytes, offer.Signature, pub).Version == "0.20.0", "valid signature accepted");
    var altered = (byte[])offer.ManifestBytes.Clone(); altered[0] ^= 1;
    await Reject(() => { UpdateManifest.Verify(altered, offer.Signature, pub); return Task.CompletedTask; }, "tampered manifest rejected");
    using var wrongKey = RSA.Create(2048);
    await Reject(() => { UpdateManifest.Verify(offer.ManifestBytes, offer.Signature, wrongKey.ExportSubjectPublicKeyInfoPem()); return Task.CompletedTask; }, "wrong publisher rejected");
    Check(!offer.Manifest.IsNewerThan(new(0,20,0,0)) && !offer.Manifest.IsNewerThan(new(0,21,0)), "equal and older versions not offered");
    Check(offer.Manifest.IsNewerThan(new(0,19,0)), "new version offered");
    foreach(var invalid in new[]{offer.Manifest with { Channel="beta" },offer.Manifest with { Version="0.20.0-preview" },offer.Manifest with { Package=offer.Manifest.Package with { Url="https://example.com/update.zip" } },offer.Manifest with { Package=offer.Manifest.Package with { Size=UpdateManifest.MaxPackageBytes+1 } }})
        await Reject(() => { var o=Sign(invalid);UpdateManifest.Verify(o.ManifestBytes,o.Signature,pub);return Task.CompletedTask; }, "incompatible manifest rejected");
    Check(!UpdateClient.AllowedUri(new("http://github.com/")) && !UpdateClient.AllowedUri(new("https://github.com/evil/releases/x")) && !UpdateClient.AllowedUri(new("https://github.com:8443/peregond/kachalka-releases/releases/x")), "foreign and insecure URLs rejected");
    using var http = new UpdateClient(pub, new FakeHandler(req =>
    {
        var p=req.RequestUri!.AbsolutePath;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(p.EndsWith("latest.json")?offer.ManifestBytes:p.EndsWith("latest.sig")?offer.Signature:payload) };
    }));
    Check(await http.CheckAsync(new(0,19,0),default) is not null, "check via GitHub metadata responses");
    Check(await http.CheckAsync(new(0,20,0),default) is null, "up-to-date check returns no update");
    var downloaded=Path.Combine(root,"download.zip");await http.DownloadAsync(offer,downloaded,null,default);
    Check(File.ReadAllBytes(downloaded).SequenceEqual(payload), "download hash and size verified");
    using var corrupt = new UpdateClient(pub,new FakeHandler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Encoding.UTF8.GetBytes("release corrupt"))}));
    await Reject(()=>corrupt.DownloadAsync(offer,Path.Combine(root,"corrupt.zip"),null,default), "corrupt payload rejected");
    Check(!File.Exists(Path.Combine(root,"corrupt.zip.partial")) && !File.Exists(Path.Combine(root,"corrupt.zip")), "failed download leaves no installable file");
    using var redirect = new UpdateClient(pub,new FakeHandler(_=>{var r=new HttpResponseMessage(HttpStatusCode.Redirect);r.Headers.Location=new("https://example.com/update.zip");return r;}));
    await Reject(()=>redirect.CheckAsync(new(0,19,0),default), "foreign redirect rejected");
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    await Reject(()=>http.DownloadAsync(offer,Path.Combine(root,"cancel.zip"),null,cancel.Token), "cancelled download rejected");
    using var noNetwork=new UpdateClient(pub,new FakeHandler(_=>throw new HttpRequestException("offline")));
    await Reject(()=>noNetwork.CheckAsync(new(0,19,0),default), "offline check terminates without install");

    byte[] Zip(string extra="",bool symlink=false)
    {
        using var bytes=new MemoryStream();
        using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
        {
            foreach(var name in new[]{"Kachalka.exe","Kachalka.dll","Kachalka.runtimeconfig.json","Kachalka.Updater.exe"})
            {
                var e=zip.CreateEntry("Kachalka-0.20/"+name);using var s=e.Open();
                var b=name.EndsWith(".dll")?File.ReadAllBytes(typeof(FakeHandler).Assembly.Location):Encoding.UTF8.GetBytes("fixture");s.Write(b);
            }
            if(extra!="") {var e=zip.CreateEntry(extra);if(symlink)e.ExternalAttributes=0xA000<<16;using var s=e.Open();s.WriteByte(1);}
        }
        return bytes.ToArray();
    }
    async Task Extract(byte[] zip,string destination)
    {
        var path=Path.Combine(root,Guid.NewGuid()+".zip");await File.WriteAllBytesAsync(path,zip);UpdateArchive.ExtractVerified(path,destination,Manifest(zip));
    }
    var extracted=Path.Combine(root,"valid");await Extract(Zip(),extracted);
    Check(File.Exists(Path.Combine(extracted,"Kachalka.exe")), "valid archive and assembly version extracted");
    foreach(var path in new[]{"Kachalka-0.20/../../escaped.txt","Kachalka-0.20/C:/bad","Kachalka-0.20/CON.txt","Other/file","Kachalka-0.20/kachalka.EXE","Kachalka-0.20/folder./file","Kachalka-0.20/a\\..\\escape"})
        await Reject(()=>Extract(Zip(path),Path.Combine(root,Guid.NewGuid().ToString())), "unsafe ZIP rejected: "+path);
    await Reject(()=>Extract(Zip("Kachalka-0.20/link",true),Path.Combine(root,"link")), "ZIP symlink rejected");
    Check(!File.Exists(Path.Combine(root,"escaped.txt")), "no writes escaped extraction root");
    var damaged=Zip();var damagedFile=Path.Combine(root,"damaged.zip");await File.WriteAllBytesAsync(damagedFile,damaged);var expected=Manifest(damaged);damaged[10]^=1;await File.WriteAllBytesAsync(damagedFile,damaged);
    await Reject(()=>{UpdateArchive.ExtractVerified(damagedFile,Path.Combine(root,"damaged"),expected);return Task.CompletedTask;}, "archive reverified before installation");
    async Task Transaction(bool healthy)
    {
        var area=Path.Combine(root,Guid.NewGuid().ToString());Directory.CreateDirectory(area);
        var install=Path.Combine(area,"app");var candidate=Path.Combine(area,"candidate");var backup=Path.Combine(area,"backup");
        Directory.CreateDirectory(install);File.WriteAllText(Path.Combine(install,"version"),"old");Directory.CreateDirectory(candidate);File.WriteAllText(Path.Combine(candidate,"version"),"new");
        var data=Path.Combine(area,"queue.json");File.WriteAllText(data,"user queue");bool stopped=false,restarted=false;
        async Task Apply()=>await UpdateInstaller.ApplyAsync(install,candidate,backup,()=>Task.FromResult(healthy),()=>{stopped=true;return Task.CompletedTask;},()=>restarted=true);
        if(healthy){await Apply();Check(File.ReadAllText(Path.Combine(install,"version"))=="new" && File.ReadAllText(Path.Combine(backup,"version"))=="old", "healthy install retains backup");}
        else{await Reject(Apply,"failed health causes rollback");Check(stopped && restarted && File.ReadAllText(Path.Combine(install,"version"))=="old", "old installation restored and restarted");}
        Check(File.ReadAllText(data)=="user queue", "queue outside installation preserved");
    }
    await Transaction(true);await Transaction(false);
    Check(UpdateTrust.PublicKey.Contains("BEGIN PUBLIC KEY"), "publisher key embedded in shared core");
    Console.WriteLine($"All {checks} updater checks passed.");return 0;
}
catch(Exception e){Console.Error.WriteLine(e);return 1;}
finally { Directory.Delete(root,true); }

sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {cancellationToken.ThrowIfCancellationRequested();var response=respond(request);response.RequestMessage=request;return Task.FromResult(response);}
}

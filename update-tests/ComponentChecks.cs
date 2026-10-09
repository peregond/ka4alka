using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kachalka.Updates;

static class ComponentChecks
{
    public static async Task<int> Run(string root)
    {
        int checks=0;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
        async Task Reject(Func<Task> action,string label)
        {
            bool failed=false;try{await action();}catch(Exception e) when(e is InvalidDataException or IOException or CryptographicException or OperationCanceledException){failed=true;}Check(failed,label);
        }
        using var key=RSA.Create(2048);var pub=key.ExportSubjectPublicKeyInfoPem();
        var zipPath=Path.Combine(root,"components.zip");var runtime=RandomNumberGenerator.GetBytes(1024*1024);
        using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))
        {
            foreach(var name in new[]{"Kachalka.exe","Kachalka.dll","Kachalka.runtimeconfig.json","Kachalka.Updater.exe","runtime.bin","empty.bin","stored.bin"})
            {
                var entry=zip.CreateEntry("app/"+name,name=="stored.bin"?CompressionLevel.NoCompression:CompressionLevel.Optimal);
                using var output=entry.Open();output.Write(name switch {"Kachalka.dll"=>File.ReadAllBytes(typeof(ComponentChecks).Assembly.Location),"runtime.bin"=>runtime,"empty.bin"=>[],_=>"updated file"u8.ToArray()});
            }
        }
        var archive=File.ReadAllBytes(zipPath);var catalog=ComponentCatalog.Create(zipPath,"0.20.0");var index=JsonSerializer.SerializeToUtf8Bytes(catalog);
        var manifest=new UpdateManifest(1,"kachalka","stable","0.20.0","win-x64","10",new($"https://github.com/{UpdateManifest.Repository}/releases/download/v0.20.0/Kachalka-win-x64.zip","Kachalka-win-x64.zip",archive.Length,Convert.ToHexString(SHA256.HashData(archive)),"portable-zip"),$"https://github.com/{UpdateManifest.Repository}/releases/tag/v0.20.0",true,new($"https://github.com/{UpdateManifest.Repository}/releases/download/v0.20.0/components.json",index.Length,Convert.ToHexString(SHA256.HashData(index))));
        UpdateOffer Sign(UpdateManifest m){var b=JsonSerializer.SerializeToUtf8Bytes(m);return new(m,b,key.SignData(b,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));}
        var offer=Sign(manifest);var install=Path.Combine(root,"component-install");UpdateArchive.ExtractVerified(zipPath,install,manifest);
        File.WriteAllText(Path.Combine(install,"Kachalka.exe"),"old executable");File.WriteAllText(Path.Combine(install,"obsolete.dll"),"remove me");
        File.Delete(Path.Combine(install,"empty.bin"));File.Delete(Path.Combine(install,"stored.bin"));
        long transferred=0;int calls=0;
        HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("components.json"))return new(HttpStatusCode.OK){Content=new ByteArrayContent(index)};
            var range=request.Headers.Range!.Ranges.Single();var from=range.From!.Value;var to=range.To!.Value;
            var data=archive.AsSpan((int)from,(int)(to-from+1)).ToArray();transferred+=data.Length;calls++;
            return ComponentTools.Partial(data,from,to,archive.Length);
        }
        using var client=new UpdateClient(pub,new FakeHandler(Respond));
        var plan=await client.PlanComponentsAsync(offer,install,default);
        Check(plan.Changed.Length==3 && plan.ReusedFiles==4,"only changed and missing files enter download plan");
        var staged=Path.Combine(root,"component-stage");await client.DownloadComponentsAsync(offer,plan,staged,null,default);
        Check(transferred==plan.DownloadBytes && transferred<archive.Length/100 && calls==plan.Changed.Count(x=>x.PackedSize>0),"range downloads transfer under 1% of fixture ZIP, unchanged runtime omitted");
        var candidate=Path.Combine(root,"component-candidate");await UpdateComponents.AssembleAsync(install,staged,candidate,catalog,manifest,default);
        Check(!File.Exists(Path.Combine(candidate,"obsolete.dll")) && File.ReadAllBytes(Path.Combine(candidate,"runtime.bin")).SequenceEqual(runtime),"candidate reuses verified runtime and removes obsolete files");
        Check(catalog.Files.All(f=>UpdateComponents.MatchesAsync(UpdateComponents.FilePath(candidate,f.Path),f,default).GetAwaiter().GetResult()),"every assembled file matches signed hash, including stored and empty components");
        await Reject(()=>UpdateInstaller.ApplyAsync(install,candidate,Path.Combine(root,"component-backup"),()=>Task.FromResult(false),()=>Task.CompletedTask,()=>{}),"component candidate supports health-check rollback");
        Check(File.ReadAllText(Path.Combine(install,"Kachalka.exe"))=="old executable","component rollback restores original installation");
        File.WriteAllText(Path.Combine(install,"runtime.bin"),"bad runtime");var repair=await client.PlanComponentsAsync(offer,install,default);
        Check(repair.Changed.Any(f=>f.Path=="runtime.bin"),"damaged installed component is downloaded for repair");
        var failedCandidate=Path.Combine(root,"component-failed");await Reject(()=>UpdateComponents.AssembleAsync(install,staged,failedCandidate,catalog,manifest,default),"reused file modified after planning is rejected at installation");
        Check(!Directory.Exists(failedCandidate),"failed assembly cleans candidate without modifying installation");
        var altered=(byte[])index.Clone();altered[0]^=1;
        await Reject(()=>{ComponentCatalog.Verify(altered,manifest);return Task.CompletedTask;},"tampered component index rejected by signed hash");
        foreach(var invalid in new[]{catalog with {Files=[..catalog.Files,catalog.Files[0] with {Path="../escape"}]},catalog with {Files=[..catalog.Files,catalog.Files[0] with {Path="kachalka.EXE"}]},catalog with {Files=catalog.Files.Select((f,i)=>i==0?f with {Offset=archive.Length}:f).ToArray()},catalog with {Files=[..catalog.Files,catalog.Files[0] with {Path="CON.txt"}]}})
            await Reject(()=>{invalid.Validate(manifest);return Task.CompletedTask;},"unsafe component catalog rejected");
        var forged=plan with {Changed=[plan.Changed[0] with {Offset=0}]};await Reject(()=>client.DownloadComponentsAsync(offer,forged,Path.Combine(root,"forged-stage"),null,default),"download plan cannot override signed offsets");
        using var noRange=new UpdateClient(pub,new FakeHandler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(archive)}));
        bool fallback=false;try{await noRange.DownloadComponentsAsync(offer,plan,Path.Combine(root,"no-range"),null,default);}catch(RangeNotSupportedException){fallback=true;}
        Check(fallback && !Directory.Exists(Path.Combine(root,"no-range")),"server without range support requests legacy fallback and cleans staging");
        using var wrongRange=new UpdateClient(pub,new FakeHandler(r=>{var result=Respond(r);result.Content.Headers.ContentRange=new(0,0,archive.Length);return result;}));
        await Reject(()=>wrongRange.DownloadComponentsAsync(offer,plan,Path.Combine(root,"wrong-range"),null,default),"wrong Content-Range rejected");
        using var corrupt=new UpdateClient(pub,new FakeHandler(r=>{var result=Respond(r);var data=result.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();data[0]^=0xff;var header=result.Content.Headers.ContentRange;result.Content=new ByteArrayContent(data);result.Content.Headers.ContentRange=header;return result;}));
        await Reject(()=>corrupt.DownloadComponentsAsync(offer,plan,Path.Combine(root,"corrupt-component"),null,default),"corrupt compressed component rejected");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Reject(()=>client.DownloadComponentsAsync(offer,plan,Path.Combine(root,"cancel-component"),null,cancellation.Token),"cancelled component update leaves no installable candidate");
        Check(!Directory.Exists(Path.Combine(root,"cancel-component")),"cancelled transfer cleans staging");
        // A deflated payload may expand beyond its signed size; reject before writing excess bytes.
        var runtimeEntry=catalog.Files.Single(f=>f.Path=="runtime.bin") with {Size=1};
        await Reject(()=>UpdateComponents.WriteVerifiedAsync(new MemoryStream(runtime),Path.Combine(root,"oversize.bin"),runtimeEntry,default),"component output bounded by signed uncompressed size");
        var legacy=Sign(manifest with {Components=null});UpdateManifest.Verify(legacy.ManifestBytes,legacy.Signature,pub);
        Check(legacy.Manifest.Components==null,"legacy manifests without component inventory still accepted");
        return checks;
    }
}

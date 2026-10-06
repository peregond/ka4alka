using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Kachalka.Updates;

static class ComponentTools
{
    public static async Task<int> Run(string[] args)
    {
        if(args.Length==4 && args[0]=="--create-component-index")
        {
            File.WriteAllBytes(args[3],JsonSerializer.SerializeToUtf8Bytes(ComponentCatalog.Create(args[1],args[2])));
            return 0;
        }
        if(args.Length!=4 || args[0]!="--prepare-components")throw new ArgumentException("Invalid component command");
        var install=Path.GetFullPath(args[1]);var release=Path.GetFullPath(args[2]);var work=Path.GetFullPath(args[3]);
        var bytes=File.ReadAllBytes(Path.Combine(release,"latest.json"));var signature=File.ReadAllBytes(Path.Combine(release,"latest.sig"));
        var manifest=UpdateManifest.Verify(bytes,signature,UpdateTrust.PublicKey);var offer=new UpdateOffer(manifest,bytes,signature);
        var index=File.ReadAllBytes(Path.Combine(release,"components.json"));long transferred=0;int requests=0;
        using var client=new UpdateClient(UpdateTrust.PublicKey,new FakeHandler(request=>
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("components.json"))return new(HttpStatusCode.OK){Content=new ByteArrayContent(index)};
            var range=request.Headers.Range?.Ranges.Single()??throw new Exception("Full ZIP download requested");
            using var archive=File.OpenRead(Path.Combine(release,"Kachalka-win-x64.zip"));
            var from=range.From!.Value;var to=range.To!.Value;var data=new byte[checked((int)(to-from+1))];archive.Position=from;archive.ReadExactly(data);
            transferred+=data.Length;requests++;
            return Partial(data,from,to,archive.Length);
        }));
        var plan=await client.PlanComponentsAsync(offer,install,default);
        await client.DownloadComponentsAsync(offer,plan,Path.Combine(work,"components"),null,default);
        File.WriteAllBytes(Path.Combine(work,"components.json"),plan.CatalogBytes);
        if(transferred!=plan.DownloadBytes || requests!=plan.Changed.Count(x=>x.PackedSize>0))throw new Exception("Component transfer mismatch");
        var metrics=new {Changed=plan.Changed.Length,Reused=plan.ReusedFiles,Downloaded=transferred,Package=manifest.Package.Size,Requests=requests};
        File.WriteAllText(Path.Combine(work,"metrics.json"),JsonSerializer.Serialize(metrics));
        Console.WriteLine("PASS: component transfer "+JsonSerializer.Serialize(metrics));return 0;
    }
    public static HttpResponseMessage Partial(byte[] data,long from,long to,long length)
    {
        var result=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(data)};
        result.Content.Headers.ContentRange=new ContentRangeHeaderValue(from,to,length);return result;
    }
}

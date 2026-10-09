using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace Kachalka.Updates;
public sealed record ComponentProgress(long Received,long Total,int ChangedFiles,int ReusedFiles);
public sealed partial class UpdateClient
{
    public async Task<ComponentPlan> PlanComponentsAsync(UpdateOffer offer,string install,CancellationToken cancellation)
    {
        var manifest=UpdateManifest.Verify(offer.ManifestBytes,offer.Signature,publicKey);
        var index=manifest.Components??throw new InvalidDataException("Описание компонентов отсутствует.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var bytes=await ReadSmallAsync(index.Url,ComponentCatalog.MaxIndexBytes,timeout.Token);
        var catalog=ComponentCatalog.Verify(bytes,manifest);var changed=new List<UpdateComponent>();
        foreach(var component in catalog.Files)
            if(!await UpdateComponents.MatchesAsync(UpdateComponents.FilePath(install,component.Path),component,timeout.Token))changed.Add(component);
        return new(bytes,changed.ToArray(),catalog.Files.Length-changed.Count);
    }
    public async Task DownloadComponentsAsync(UpdateOffer offer,ComponentPlan plan,string destination,IProgress<ComponentProgress>? progress,CancellationToken cancellation)
    {
        var manifest=UpdateManifest.Verify(offer.ManifestBytes,offer.Signature,publicKey);var catalog=ComponentCatalog.Verify(plan.CatalogBytes,manifest);
        var files=catalog.Files.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var component in plan.Changed)
            if(!files.TryGetValue(component.Path,out var signed)||signed!=component||!unique.Add(component.Path))throw new InvalidDataException("План обновления не соответствует подписанному каталогу.");
        if(Directory.Exists(destination))throw new IOException("Папка компонентов уже существует.");
        _=UpdateComponents.FilePath(destination,"Kachalka.dll");Directory.CreateDirectory(destination);long received=0;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(15));
        progress?.Report(new(0,plan.DownloadBytes,plan.Changed.Length,plan.ReusedFiles));
        try
        {
            foreach(var component in plan.Changed)
            {
                timeout.Token.ThrowIfCancellationRequested();var file=UpdateComponents.FilePath(destination,component.Path);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var packed=file+".compressed-"+Guid.NewGuid().ToString("N");
                try
                {
                    if(component.PackedSize>0)
                    {
                        using var response=await OpenAsync(manifest.Package.Url,timeout.Token,new RangeHeaderValue(component.Offset,component.Offset+component.PackedSize-1));
                        if(response.StatusCode==HttpStatusCode.OK)throw new RangeNotSupportedException();
                        var range=response.Content.Headers.ContentRange;
                        if(response.StatusCode!=HttpStatusCode.PartialContent||range is null||range.Unit!="bytes"||range.From!=component.Offset||range.To!=component.Offset+component.PackedSize-1||range.Length!=manifest.Package.Size||response.Content.Headers.ContentLength is {} length&&length!=component.PackedSize)
                            throw new InvalidDataException("Неверный диапазон компонента.");
                        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);
                        await using(var output=new FileStream(packed,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        {
                            long total=0;var buffer=new byte[65536];int count;
                            while((count=await input.ReadAsync(buffer,timeout.Token))>0)
                            {
                                total+=count;if(total>component.PackedSize)throw new InvalidDataException("Компонент превышает заявленный размер.");
                                await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);received+=count;progress?.Report(new(received,plan.DownloadBytes,plan.Changed.Length,plan.ReusedFiles));
                            }
                            if(total!=component.PackedSize)throw new InvalidDataException("Компонент скачан не полностью.");
                        }
                    }
                    else await File.WriteAllBytesAsync(packed,[],timeout.Token);
                    using var compressed=File.OpenRead(packed);
                    using var decoded=component.Method==8?(Stream)new DeflateStream(compressed,CompressionMode.Decompress,true):compressed;
                    await UpdateComponents.WriteVerifiedAsync(decoded,file,component,timeout.Token);
                }
                finally{File.Delete(packed);}
            }
        }
        catch{Directory.Delete(destination,true);throw;}
    }
}

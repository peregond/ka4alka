using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Kachalka.Updates;
namespace Kachalka.MacUpdates;

public sealed record MacUpdateProgress(long Received,long Total,int ChangedFiles,int ReusedFiles);
public sealed record PreparedMacUpdate(string Application,string CatalogPath,string ManifestPath,string SignaturePath,long DownloadedBytes,long PackageBytes,int ReusedFiles,bool FullPackage);
// Separate from the shipped Windows installer. Only the public hash-checked
// streaming writer and ZIP component offsets are reused from UpdateCore.
public sealed class MacUpdateClient:IDisposable
{
    readonly HttpClient http;readonly string publicKey;
    public MacUpdateClient(string key,HttpMessageHandler? handler=null){publicKey=key;http=new(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("Ka4alka-macOS-Updater/1");}
    async Task<HttpResponseMessage> Open(string address,CancellationToken ct,RangeHeaderValue? range=null)
    {
        var uri=new Uri(address);
        for(var attempt=0;attempt<8;attempt++)
        {
            if(!UpdateClient.AllowedUri(uri))throw new InvalidDataException("Untrusted macOS update address.");
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.Range=range;
            var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            if((int)response.StatusCode is >=300 and <400&&response.Headers.Location is {} location){uri=location.IsAbsoluteUri?location:new Uri(uri,location);response.Dispose();continue;}
            return response;
        }
        throw new IOException("Too many update redirects.");
    }
    async Task<byte[]> Small(string uri,int max,CancellationToken ct)
    {
        using var response=await Open(uri,ct);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>max)throw new InvalidDataException("Oversized update index.");
        using var output=new MemoryStream();await using var stream=await response.Content.ReadAsStreamAsync(ct);var buffer=new byte[16384];int count;
        while((count=await stream.ReadAsync(buffer,ct))>0){if(output.Length+count>max)throw new InvalidDataException("Oversized update index.");output.Write(buffer,0,count);}return output.ToArray();
    }
    public async Task<PreparedMacUpdate> PrepareAsync(byte[] manifestBytes,byte[] signature,string architecture,string installed,string destination,IProgress<MacUpdateProgress>? progress=null,CancellationToken ct=default)
    {
        var manifest=MacManifest.Verify(manifestBytes,signature,publicKey);
        if(architecture is not ("arm64" or "x64"))throw new InvalidDataException("Unsupported macOS architecture.");
        if(Directory.Exists(destination))throw new IOException("The staging directory already exists.");
        Directory.CreateDirectory(destination);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(destination,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(15));ct=deadline.Token;
        try
        {
            var package=manifest.Packages[architecture];var indexBytes=await Small(package.Components.Url,ComponentCatalog.MaxIndexBytes,ct);var catalog=MacComponents.Verify(indexBytes,package,manifest.Version);
            var changed=new List<MacComponent>();
            foreach(var file in catalog.Files)if(!await UpdateComponents.MatchesAsync(PathFor(installed,file.Path),file.Data,ct))changed.Add(file);
            var reused=catalog.Files.Length-changed.Count;var total=changed.Sum(file=>file.PackedSize);long received=0;bool full=false;
            var staged=Path.Combine(destination,"components");Directory.CreateDirectory(staged);
            progress?.Report(new(0,total,changed.Count,reused));
            try
            {
                foreach(var file in changed)
                {
                    var path=PathFor(staged,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var packed=path+".deflate";
                    try
                    {
                        using(var output=File.Create(packed))
                        {
                            if(file.PackedSize>0)
                            {
                                using var response=await Open(package.Url,ct,new(file.Offset,file.Offset+file.PackedSize-1));
                                if(response.StatusCode==HttpStatusCode.OK)throw new RangeNotSupportedException();
                                var range=response.Content.Headers.ContentRange;
                                if(response.StatusCode!=HttpStatusCode.PartialContent||range?.Unit!="bytes"||range.From!=file.Offset||range.To!=file.Offset+file.PackedSize-1||range.Length!=package.Size||response.Content.Headers.ContentLength is {} length&&length!=file.PackedSize)throw new InvalidDataException("Incorrect update byte range.");
                                await using var input=await response.Content.ReadAsStreamAsync(ct);var buffer=new byte[65536];long bytes=0;int count;
                                while((count=await input.ReadAsync(buffer,ct))>0){bytes+=count;if(bytes>file.PackedSize)throw new InvalidDataException("Oversized component.");await output.WriteAsync(buffer.AsMemory(0,count),ct);received+=count;progress?.Report(new(received,total,changed.Count,reused));}
                                if(bytes!=file.PackedSize)throw new InvalidDataException("Truncated component.");
                            }
                        }
                        using var data=File.OpenRead(packed);using var decoded=file.Method==8?(Stream)new DeflateStream(data,CompressionMode.Decompress,true):data;
                        await UpdateComponents.WriteVerifiedAsync(decoded,path,file.Data,ct);SetMode(path,file.Mode);
                    }
                    finally{File.Delete(packed);}
                }
            }
            catch(RangeNotSupportedException)
            {
                Directory.Delete(staged,true);Directory.CreateDirectory(staged);full=true;reused=0;
                var archive=Path.Combine(destination,package.FileName);received=await Full(package,archive,progress,ct);
                using var zip=ZipFile.OpenRead(archive);var known=catalog.Files.ToDictionary(file=>file.Path,StringComparer.Ordinal);
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries)
                {
                    if(entry.FullName.EndsWith('/'))continue;
                    if(!entry.FullName.StartsWith("Ka4alka.app/",StringComparison.Ordinal)||!known.TryGetValue(entry.FullName[12..],out var file)||!seen.Add(file.Path)||entry.Length!=file.Size||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Unsafe macOS application archive.");
                    using var input=entry.Open();var path=PathFor(staged,file.Path);await UpdateComponents.WriteVerifiedAsync(input,path,file.Data,ct);SetMode(path,file.Mode);
                }
                if(seen.Count!=known.Count)throw new InvalidDataException("Incomplete application archive.");
            }
            var candidate=Path.Combine(destination,"Ka4alka.app");Directory.CreateDirectory(candidate);
            foreach(var file in catalog.Files)
            {
                var downloaded=PathFor(staged,file.Path);var source=File.Exists(downloaded)?downloaded:PathFor(installed,file.Path);var target=PathFor(candidate,file.Path);
                using var input=File.OpenRead(source);await UpdateComponents.WriteVerifiedAsync(input,target,file.Data,ct);SetMode(target,file.Mode);
            }
            ValidateIdentity(candidate,manifest.Version);
            if(OperatingSystem.IsMacOS())await VerifyCodeSignature(candidate,ct);
            var catalogPath=Path.Combine(destination,"components.json");var manifestPath=Path.Combine(destination,"manifest.json");var signaturePath=Path.Combine(destination,"manifest.sig");
            await File.WriteAllBytesAsync(catalogPath,indexBytes,ct);await File.WriteAllBytesAsync(manifestPath,manifestBytes,ct);await File.WriteAllBytesAsync(signaturePath,signature,ct);
            return new(candidate,catalogPath,manifestPath,signaturePath,received,package.Size,reused,full);
        }
        catch{try{Directory.Delete(destination,true);}catch(IOException){}throw;}
    }
    async Task<long> Full(MacPackage package,string file,IProgress<MacUpdateProgress>? progress,CancellationToken ct)
    {
        using var response=await Open(package.Url,ct);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength is {} size&&size!=package.Size)throw new InvalidDataException("Wrong update package size.");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);await using var output=File.Create(file);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long total=0;int count;
        while((count=await stream.ReadAsync(buffer,ct))>0){total+=count;if(total>package.Size)throw new InvalidDataException("Oversized macOS package.");hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),ct);progress?.Report(new(total,package.Size,1,0));}
        if(total!=package.Size||Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()!=package.Sha256)throw new InvalidDataException("Corrupt macOS package.");return total;
    }
    public static string PathFor(string root,string relative)
    {
        if(!ComponentCatalog.SafePath(relative))throw new InvalidDataException("Unsafe macOS component path.");
        var path=Path.GetFullPath(Path.Combine(root,relative));var parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidDataException("Component outside its app bundle.");
        for(var current=path;current!=null;current=Path.GetDirectoryName(current))if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Symlink in macOS component path.");
        return path;
    }
    static void SetMode(string path,int mode){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,(UnixFileMode)mode);}
    public static void ValidateIdentity(string application,string version)
    {
        var root=XDocument.Load(Path.Combine(application,"Contents/Info.plist")).Root?.Element("dict")?.Elements().ToArray()??[];
        string? Read(string key)=>root.Select((node,i)=>(node,i)).Where(row=>row.node.Name=="key"&&row.node.Value==key&&row.i+1<root.Length).Select(row=>root[row.i+1].Value).FirstOrDefault();
        if(Read("CFBundleIdentifier")!="org.ka4alka.app"||Read("CFBundleShortVersionString")!=version||Read("CFBundleExecutable")!="Ka4alka")throw new InvalidDataException("Incorrect macOS application identity.");
    }
    static async Task VerifyCodeSignature(string app,CancellationToken ct)
    {
        using var process=new Process{StartInfo=new("/usr/bin/codesign"){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true}};
        process.StartInfo.ArgumentList.Add("--verify");process.StartInfo.ArgumentList.Add("--deep");process.StartInfo.ArgumentList.Add("--strict");process.StartInfo.ArgumentList.Add(app);process.Start();
        var error=process.StandardError.ReadToEndAsync(ct);await process.WaitForExitAsync(ct);if(process.ExitCode!=0)throw new InvalidDataException("macOS application code signature is invalid: "+await error);
    }
    public void Dispose()=>http.Dispose();
}

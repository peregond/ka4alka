using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kachalka.Updates;
namespace Kachalka.MacUpdates;

public sealed record MacPackage(string Url,string FileName,long Size,string Sha256,ComponentIndex Components);
public sealed record MacManifest(int SchemaVersion,string AppId,string Channel,string Version,string Platform,string MinimumMacOS,Dictionary<string,MacPackage> Packages,string ReleaseNotesUrl)
{
    public const long MaxBytes=512L*1024*1024;
    public static MacManifest Verify(byte[] bytes,byte[] signature,string publicKey)
    {
        if(bytes.Length>64*1024||signature.Length>1024)throw new InvalidDataException("Invalid macOS update envelope.");
        using var rsa=RSA.Create();rsa.ImportFromPem(publicKey);
        if(!rsa.VerifyData(bytes,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("Invalid macOS update signature.");
        var value=JsonSerializer.Deserialize<MacManifest>(bytes,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Empty macOS update manifest.");
        value.Validate();return value;
    }
    public void Validate()
    {
        if(SchemaVersion!=1||AppId!="kachalka"||Channel!="stable"||Platform!="macos"||MinimumMacOS!="14.0"||Version==null||!Regex.IsMatch(Version,@"\A\d+\.\d+\.\d+\z")||Packages==null||Packages.Count!=2||ReleaseNotesUrl!="https://github.com/peregond/ka4alka/releases/tag/v"+Version)throw new InvalidDataException("Invalid macOS update manifest.");
        foreach(var arch in new[]{"arm64","x64"})
        {
            if(!Packages.TryGetValue(arch,out var p)||p==null||p.FileName!="Kachalka-macos-"+arch+".zip"||p.Url!="https://github.com/peregond/ka4alka/releases/download/v"+Version+"/"+p.FileName||p.Size<=0||p.Size>MaxBytes||!Hash(p.Sha256)||p.Components is not {} c||c.Url!="https://github.com/peregond/ka4alka/releases/download/v"+Version+"/macos-components-"+arch+".json"||c.Size<=0||c.Size>ComponentCatalog.MaxIndexBytes||!Hash(c.Sha256))throw new InvalidDataException("Invalid macOS update package.");
        }
    }
    internal static bool Hash(string? value)=>value!=null&&Regex.IsMatch(value,@"\A[a-f0-9]{64}\z");
}
public sealed record MacComponent(string Path,long Size,string Sha256,long Offset,long PackedSize,int Method,int Mode)
{
    public UpdateComponent Data=>new(Path,Size,Sha256,Offset,PackedSize,Method);
}
public sealed record MacComponents(int SchemaVersion,string Version,MacComponent[] Files)
{
    public static MacComponents Verify(byte[] bytes,MacPackage package,string version)
    {
        if(bytes.Length!=package.Components.Size||Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=package.Components.Sha256)throw new InvalidDataException("Invalid macOS component index checksum.");
        var catalog=JsonSerializer.Deserialize<MacComponents>(bytes,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Empty component catalog.");
        catalog.Validate(package,version);return catalog;
    }
    public void Validate(MacPackage package,string version)
    {
        if(SchemaVersion!=1||Version!=version||Files==null||Files.Length is 0 or >4000)throw new InvalidDataException("Invalid macOS component index.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in Files)
        {
            if(file==null||file.Path==null||!ComponentCatalog.SafePath(file.Path)||!file.Path.StartsWith("Contents/",StringComparison.Ordinal)||!paths.Add(file.Path)||file.Size<0||file.Size>MacManifest.MaxBytes||file.Offset<0||file.PackedSize<0||file.Offset>package.Size-file.PackedSize||file.Method is not (0 or 8)||file.Method==0&&file.PackedSize!=file.Size||!MacManifest.Hash(file.Sha256)||file.Mode is not (420 or 493))throw new InvalidDataException("Unsafe macOS component.");
            total=checked(total+file.Size);if(total>2L*1024*1024*1024)throw new InvalidDataException("The application is too large.");
        }
        foreach(var path in paths){var parent=path;while(parent.Contains('/')){parent=parent[..parent.LastIndexOf('/')];if(paths.Contains(parent))throw new InvalidDataException("Overlapping component paths.");}}
        foreach(var required in new[]{"Contents/Info.plist","Contents/MacOS/Ka4alka","Contents/Resources/engine/Kachalka.Engine","Contents/Resources/engine/Kachalka.Engine.dll","Contents/Resources/engine/Kachalka.DesktopCore.dll","Contents/Resources/update-public.pem"})if(!paths.Contains(required))throw new InvalidDataException("Missing macOS component: "+required);
    }
    public static MacComponents Create(string archive,string version)
    {
        var offsets=ComponentCatalog.Create(archive,version);
        using var zip=ZipFile.OpenRead(archive);
        var modes=zip.Entries.Where(entry=>!entry.FullName.EndsWith('/')).ToDictionary(entry=>entry.FullName[(entry.FullName.IndexOf('/')+1)..],entry=>(entry.ExternalAttributes>>16)&511,StringComparer.Ordinal);
        return new(1,version,offsets.Files.Select(file=>new MacComponent(file.Path,file.Size,file.Sha256,file.Offset,file.PackedSize,file.Method,modes[file.Path])).ToArray());
    }
}

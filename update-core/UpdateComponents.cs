using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka.Updates;

public sealed record ComponentIndex(string Url,long Size,string Sha256);
public sealed record UpdateComponent(string Path,long Size,string Sha256,long Offset,long PackedSize,int Method);
public sealed record ComponentCatalog(int SchemaVersion,string Version,UpdateComponent[] Files)
{
    public const int MaxIndexBytes=2*1024*1024;
    public static bool SafePath(string path)=>path.Length is >0 and <=512&&!path.Contains('\\')&&!path.StartsWith('/')&&path.Split('/').All(p=>p.Length>0&&p is not ("." or "..")&&!p.EndsWith('.')&&!p.EndsWith(' ')&&!p.Any(c=>char.IsControl(c)||"<>:\"|?*".Contains(c))&&!Regex.IsMatch(p,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase));
    public void Validate(UpdateManifest manifest)
    {
        if(SchemaVersion!=1||Version!=manifest.Version||Files is null||Files.Length is 0 or >10000)throw new InvalidDataException("Неверный список компонентов.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;
        foreach(var file in Files)
        {
            if(file is null||file.Path is null||!SafePath(file.Path)||!paths.Add(file.Path)||file.Size<0||file.Size>UpdateManifest.MaxPackageBytes||file.PackedSize<0||file.PackedSize>manifest.Package.Size||file.Offset<0||file.Offset>manifest.Package.Size-file.PackedSize||file.Method is not (0 or 8)||file.Method==0&&file.PackedSize!=file.Size||!ValidHash(file.Sha256))throw new InvalidDataException("Неверный компонент обновления.");
            size=checked(size+file.Size);if(size>2L*1024*1024*1024)throw new InvalidDataException("Обновление слишком большое.");
        }
        foreach(var path in paths)
        {
            var parent=path;while(parent.Contains('/')){parent=parent[..parent.LastIndexOf('/')];if(paths.Contains(parent))throw new InvalidDataException("Пересекающиеся пути компонентов.");}
        }
        foreach(var required in new[]{"Kachalka.exe","Kachalka.dll","Kachalka.runtimeconfig.json","Kachalka.Updater.exe"})if(!paths.Contains(required))throw new InvalidDataException("Отсутствует компонент "+required);
    }
    internal static bool ValidHash(string? hash)=>hash!=null&&Regex.IsMatch(hash,@"\A[0-9a-fA-F]{64}\z");
    public static ComponentCatalog Verify(byte[] bytes,UpdateManifest manifest)
    {
        var index=manifest.Components??throw new InvalidDataException("Список компонентов отсутствует.");
        if(bytes.Length!=index.Size||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(index.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Список компонентов повреждён.");
        var catalog=JsonSerializer.Deserialize<ComponentCatalog>(bytes,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Пустой список компонентов.");
        catalog.Validate(manifest);return catalog;
    }
    public static ComponentCatalog Create(string archive,string version)
    {
        // Read ZIP's central directory to locate each deflated stream, without an extra asset per file.
        using var input=File.OpenRead(archive);using var reader=new BinaryReader(input,Encoding.UTF8,true);
        var tailLength=(int)Math.Min(65557,input.Length);input.Position=input.Length-tailLength;var tail=reader.ReadBytes(tailLength);int end=-1;
        for(var i=tail.Length-22;i>=0;i--)if(BitConverter.ToUInt32(tail,i)==0x06054b50&&i+22+BitConverter.ToUInt16(tail,i+20)==tail.Length){end=i;break;}
        if(end<0||BitConverter.ToUInt16(tail,end+4)!=0||BitConverter.ToUInt16(tail,end+6)!=0)throw new InvalidDataException("Неподдерживаемый ZIP.");
        var count=BitConverter.ToUInt16(tail,end+10);var directory=BitConverter.ToUInt32(tail,end+16);
        if(count is 0 or >10000||directory==uint.MaxValue)throw new InvalidDataException("Неподдерживаемый ZIP.");
        var positions=new Dictionary<string,(long Offset,long Size,int Method)>();input.Position=directory;
        for(var n=0;n<count;n++)
        {
            if(reader.ReadUInt32()!=0x02014b50)throw new InvalidDataException("Неверный каталог ZIP.");
            input.Position+=4;var flags=reader.ReadUInt16();var method=reader.ReadUInt16();input.Position+=8;var packed=reader.ReadUInt32();input.Position+=4;
            var nameLength=reader.ReadUInt16();var extraLength=reader.ReadUInt16();var commentLength=reader.ReadUInt16();input.Position+=8;var local=reader.ReadUInt32();
            var name=Encoding.UTF8.GetString(reader.ReadBytes(nameLength));var next=input.Position+extraLength+commentLength;
            if((flags&1)!=0||local==uint.MaxValue||packed==uint.MaxValue||method is not (0 or 8))throw new InvalidDataException("Неподдерживаемый компонент ZIP.");
            input.Position=local;if(reader.ReadUInt32()!=0x04034b50)throw new InvalidDataException("Неверный заголовок ZIP.");
            input.Position=local+26;var localName=reader.ReadUInt16();var localExtra=reader.ReadUInt16();
            positions.Add(name,(local+30L+localName+localExtra,packed,method));input.Position=next;
        }
        using var zip=ZipFile.OpenRead(archive);string? root=null;var files=new List<UpdateComponent>();
        foreach(var entry in zip.Entries)
        {
            var parts=entry.FullName.Split('/',2);root??=parts[0];if(parts[0]!=root)throw new InvalidDataException("Несколько папок в ZIP.");
            if(entry.FullName.EndsWith('/'))continue;
            if(parts.Length!=2||!SafePath(parts[1])||(entry.ExternalAttributes&0x400)!=0||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Неверный путь ZIP.");
            using var data=entry.Open();var location=positions[entry.FullName];
            files.Add(new(parts[1],entry.Length,Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),location.Offset,location.Size,location.Method));
        }
        return new(1,version,files.ToArray());
    }
}

public sealed record ComponentPlan(byte[] CatalogBytes,UpdateComponent[] Changed,int ReusedFiles)
{
    public long DownloadBytes=>Changed.Sum(x=>x.PackedSize);
}
public sealed class RangeNotSupportedException():IOException("Сервер не поддерживает загрузку компонентов.");
public static class UpdateComponents
{
    public static string FilePath(string root,string relative)
    {
        if(!ComponentCatalog.SafePath(relative))throw new InvalidDataException("Недопустимый путь компонента.");
        var path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!UpdateArchive.Inside(path,root))throw new InvalidDataException("Путь компонента вне папки.");
        for(var current=path;current!=null;current=Path.GetDirectoryName(current))
        {
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Ссылка вместо компонента.");
        }
        return path;
    }
    public static async Task<bool> MatchesAsync(string file,UpdateComponent component,CancellationToken cancellation)
    {
        if(!File.Exists(file)||new FileInfo(file).Length!=component.Size)return false;
        using var input=File.OpenRead(file);var hash=await SHA256.HashDataAsync(input,cancellation);return Convert.ToHexString(hash).Equals(component.Sha256,StringComparison.OrdinalIgnoreCase);
    }
    public static async Task WriteVerifiedAsync(Stream input,string destination,UpdateComponent component,CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);long total=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var buffer=new byte[65536];int count;
                while((count=await input.ReadAsync(buffer,cancellation))>0)
                {
                    total+=count;if(total>component.Size)throw new InvalidDataException("Размер компонента превышен.");
                    hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),cancellation);
                }
            }
            if(total!=component.Size||!Convert.ToHexString(hash.GetHashAndReset()).Equals(component.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Компонент повреждён: "+component.Path);
        }
        catch{File.Delete(destination);throw;}
    }
    public static async Task AssembleAsync(string install,string staged,string destination,ComponentCatalog catalog,UpdateManifest manifest,CancellationToken cancellation)
    {
        catalog.Validate(manifest);if(Directory.Exists(destination))throw new IOException("Папка новой версии уже существует.");
        _=FilePath(destination,"Kachalka.dll");Directory.CreateDirectory(destination);
        try
        {
            foreach(var component in catalog.Files)
            {
                var downloaded=FilePath(staged,component.Path);var source=File.Exists(downloaded)?downloaded:FilePath(install,component.Path);
                using var input=File.OpenRead(source);await WriteVerifiedAsync(input,FilePath(destination,component.Path),component,cancellation);
            }
            UpdateArchive.ValidateApplication(destination,manifest);
        }
        catch{Directory.Delete(destination,true);throw;}
    }
}

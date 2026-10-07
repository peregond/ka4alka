using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Kachalka;

public record CacheUsage(long Bytes,int Files,int Removed=0,long FreedBytes=0,int Failed=0);

// Only reproducible catalog data belongs here. Transfer state, downloads,
// preferences, diagnostics and signed update packages are never candidates.
public sealed class CacheStorage
{
    public const long DefaultLimit=1024L*1024*1024;
    static readonly string[] Folders=["covers","portraits","catalog","details","people","release-index"];
    static readonly string[] RootFiles=["catalog-index.json","catalog-shared.json"];
    readonly string root,statePath;
    readonly long limit;
    readonly Func<DateTime> clock;
    readonly SemaphoreSlim gate=new(1,1);
    readonly ConcurrentDictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    sealed record Entry(long Bytes,DateTime Used);
    bool indexed;
    DateTime lastWeekly;
    public CacheStorage(string dataDir,long maxBytes=DefaultLimit,Func<DateTime>? utcNow=null)
    {
        root=Path.GetFullPath(dataDir);statePath=Path.Combine(root,"cache-maintenance.json");
        if(maxBytes<=0)throw new ArgumentOutOfRangeException(nameof(maxBytes));limit=maxBytes;clock=utcNow??(()=>DateTime.UtcNow);
        try{if(new FileInfo(statePath).Length<=256)lastWeekly=JsonSerializer.Deserialize<DateTime>(File.ReadAllText(statePath));}catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){}
    }
    public bool Owns(string path)
    {
        var relative=Path.GetRelativePath(root,Path.GetFullPath(path));
        if(RootFiles.Contains(relative,StringComparer.OrdinalIgnoreCase))return true;
        var parts=relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return parts.Length==2&&Folders.Contains(parts[0],StringComparer.OrdinalIgnoreCase)&&!parts.Contains("..");
    }
    bool Safe(string path)
    {
        if(!Owns(path))return false;
        try
        {
            for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
                if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase))return true;
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        return false;
    }
    public void Touch(string path)
    {
        path=Path.GetFullPath(path);
        if(!Safe(path))return;
        try
        {
            var info=new FileInfo(path);if(!info.Exists)return;
            var now=clock();entries[path]=new(info.Length,now);
            // Explicit access times work even when the OS disables access-time updates.
            if(now-info.LastAccessTimeUtc>TimeSpan.FromMinutes(10))File.SetLastAccessTimeUtc(path,now);
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void Index()
    {
        if(indexed)return;indexed=true;
        foreach(var directory in Folders.Select(name=>Path.Combine(root,name)))
        {
            if(!Safe(Path.Combine(directory,"probe"))||!Directory.Exists(directory))continue;
            try
            {
                foreach(var path in Directory.EnumerateFiles(directory,"*",new EnumerationOptions{RecurseSubdirectories=false,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint}))Add(path);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        foreach(var name in RootFiles)Add(Path.Combine(root,name));
    }
    void Add(string path)
    {
        if(!Safe(path))return;
        try
        {
            var info=new FileInfo(path);if(!info.Exists)return;
            // A concurrent legacy writer may still be filling its temporary file.
            if(path.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)&&clock()-info.LastWriteTimeUtc<TimeSpan.FromHours(1))return;
            var used=info.LastAccessTimeUtc>info.LastWriteTimeUtc?info.LastAccessTimeUtc:info.LastWriteTimeUtc;
            entries.AddOrUpdate(path,new Entry(info.Length,used),(_,prior)=>new(info.Length,prior.Used>used?prior.Used:used));
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    long Size=>entries.Values.Sum(entry=>entry.Bytes);
    bool Delete(string path,Entry expected)
    {
        if(!Safe(path))return false;
        // A read may have refreshed this entry after the eviction list was built.
        if(entries.TryGetValue(path,out var latest)&&latest!=expected)return false;
        try{File.Delete(path);entries.TryRemove(path,out _);return true;}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    void SaveSchedule()
    {
        try
        {
            if(Directory.Exists(root)&&(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0||File.Exists(statePath)&&(File.GetAttributes(statePath)&FileAttributes.ReparsePoint)!=0)return;
            Directory.CreateDirectory(root);var temp=statePath+".tmp";
            if(File.Exists(temp)&&(File.GetAttributes(temp)&FileAttributes.ReparsePoint)!=0)return;
            File.WriteAllText(temp,JsonSerializer.Serialize(lastWeekly));File.Move(temp,statePath,true);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    CacheUsage Prune(bool clear,bool weekly)
    {
        var before=Size;var removed=0;var failed=0;var now=clock();
        if(clear||weekly)foreach(var pair in entries.OrderBy(pair=>pair.Value.Used).ToArray())
        {
            if(!clear&&pair.Value.Used>=now.AddDays(-7))continue;
            if(Delete(pair.Key,pair.Value))removed++;else failed++;
        }
        var remaining=Size;
        if(!clear&&remaining>limit)
        {
            foreach(var pair in entries.OrderBy(pair=>pair.Value.Used).ToArray())
            {
                if(remaining<=limit*9/10)break;
                if(Delete(pair.Key,pair.Value)){removed++;remaining-=pair.Value.Bytes;}else failed++;
            }
        }
        if(clear||weekly){lastWeekly=failed>0?now.AddDays(-7):now;SaveSchedule();}
        return new(Size,entries.Count,removed,Math.Max(0,before-Size),failed);
    }
    public async Task<CacheUsage> MaintainAsync(bool clear=false,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            // Rescan on maintenance to recover sizes after legacy/external writes.
            indexed=false;foreach(var path in entries.Keys)if(!File.Exists(path))entries.TryRemove(path,out _);Index();
            return Prune(clear,clock()-lastWeekly>=TimeSpan.FromDays(7)||lastWeekly>clock());
        }
        finally{gate.Release();}
    }
    public async Task<CacheUsage> UsageAsync(CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);try{Index();return new(Size,entries.Count);}finally{gate.Release();}
    }
    public async Task<bool> WriteAsync(string path,byte[] bytes,CancellationToken ct=default)
    {
        path=Path.GetFullPath(path);if(bytes.LongLength>limit||!Safe(path))return false;
        await gate.WaitAsync(ct);
        string? temp=null;
        try
        {
            Index();if(clock()-lastWeekly>=TimeSpan.FromDays(7)||lastWeekly>clock())Prune(false,true);
            var previous=entries.TryGetValue(path,out var old)?old.Bytes:0;
            var total=Size;
            if(total-previous+bytes.LongLength>limit)
            {
                var target=Math.Min(limit*9/10,limit-bytes.LongLength+previous);
                foreach(var pair in entries.OrderBy(pair=>pair.Value.Used).ToArray())
                {
                    if(total<=target)break;
                    if(!string.Equals(pair.Key,path,StringComparison.OrdinalIgnoreCase)&&Delete(pair.Key,pair.Value))total-=pair.Value.Bytes;
                }
                if(total-previous+bytes.LongLength>limit)return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(!Safe(path))return false;
            temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            await File.WriteAllBytesAsync(temp,bytes,ct);ct.ThrowIfCancellationRequested();
            if(!Safe(path))return false;File.Move(temp,path,true);
            entries[path]=new(bytes.LongLength,clock());
            try{File.SetLastAccessTimeUtc(path,clock());}catch(IOException){}catch(UnauthorizedAccessException){}
            return true;
        }
        finally
        {
            if(temp!=null)try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}
            gate.Release();
        }
    }
}

public static class CacheFiles
{
    static readonly ConcurrentDictionary<string,CacheStorage> stores=new(StringComparer.OrdinalIgnoreCase);
    public static CacheStorage Current=>stores.GetOrAdd(Path.GetFullPath(Preferences.DataDir),root=>new(root));
    public static void Touch(string path)=>Current.Touch(Path.GetFullPath(path));
    public static byte[] ReadAllBytes(string path){var bytes=File.ReadAllBytes(path);Touch(path);return bytes;}
    public static string ReadAllText(string path){var text=File.ReadAllText(path);Touch(path);return text;}
    public static async Task<byte[]> ReadAllBytesAsync(string path,CancellationToken ct=default){var bytes=await File.ReadAllBytesAsync(path,ct);Touch(path);return bytes;}
    public static async Task<string> ReadAllTextAsync(string path,CancellationToken ct=default){var text=await File.ReadAllTextAsync(path,ct);Touch(path);return text;}
    public static async Task WriteAllBytesAsync(string path,byte[] bytes,CancellationToken ct=default)
    {
        if(Current.Owns(path)){await Task.Run(()=>Current.WriteAsync(path,bytes,ct),ct);return;}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllBytesAsync(temporary,bytes,ct);ct.ThrowIfCancellationRequested();File.Move(temporary,path,true);}
        finally{try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    public static Task WriteAllTextAsync(string path,string text,CancellationToken ct=default)=>WriteAllBytesAsync(path,Encoding.UTF8.GetBytes(text),ct);
}

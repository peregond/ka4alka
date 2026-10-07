using Kachalka;
public static class CacheStorageTests
{
    public static async Task Run(string parent)
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var now=DateTime.UtcNow;var root=Path.Combine(parent,"cache-policy");Directory.CreateDirectory(root);
        string Seed(string folder,string name,int bytes,DateTime used)
        {
            var path=Path.Combine(root,folder,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,new byte[bytes]);File.SetLastWriteTimeUtc(path,used);File.SetLastAccessTimeUtc(path,used);return path;
        }
        var protectedFiles=new[]{"settings.json","queue.json","torrents/active.torrent","cache/fastresume","updates/package.zip","covers/Ka4alka/downloaded.mkv"}.Select(relative=>Seed(Path.GetDirectoryName(relative)??"",Path.GetFileName(relative),12,now.AddDays(-30))).ToArray();
        var old=Seed("covers","old.img",40,now.AddDays(-8));var recent=Seed("details","recent.json",20,now.AddDays(-8));
        var cache=new CacheStorage(root,100,()=>now);cache.Touch(recent);
        var first=await cache.MaintainAsync();
        Check(!File.Exists(old)&&File.Exists(recent)&&first.Bytes==20,"weekly cache cleanup removes unused data and preserves recently accessed metadata");
        Check(protectedFiles.All(File.Exists),"cache cleanup preserves settings, queue, torrent resume, signed updates and downloaded files");
        var untouched=Seed("covers","scheduled.img",10,now.AddDays(-8));
        var restarted=new CacheStorage(root,100,()=>now.AddDays(1));await restarted.MaintainAsync();
        Check(File.Exists(untouched),"weekly cache schedule survives an application restart");
        now=now.AddDays(8);await new CacheStorage(root,100,()=>now).MaintainAsync();
        Check(!File.Exists(untouched)&&!File.Exists(recent),"missed weekly cleanup runs on the next application startup");
        var lru=Path.Combine(parent,"cache-lru");var budget=new CacheStorage(lru,100,()=>now);
        var cold=Path.Combine(lru,"covers","cold.img");var hot=Path.Combine(lru,"portraits","hot.img");var next=Path.Combine(lru,"people","next.json");
        await budget.WriteAsync(cold,new byte[40]);now=now.AddMinutes(1);await budget.WriteAsync(hot,new byte[40]);now=now.AddMinutes(1);budget.Touch(cold);now=now.AddMinutes(1);
        await budget.WriteAsync(next,new byte[40]);
        Check(File.Exists(cold)&&!File.Exists(hot)&&File.Exists(next)&&(await budget.UsageAsync()).Bytes<=100,"cache pressure evicts the least recently accessed file before the week expires");
        Check(!await budget.WriteAsync(Path.Combine(lru,"covers","oversize.img"),new byte[101])&&(await budget.UsageAsync()).Bytes<=100,"an oversized entry cannot exceed the global cache budget");
        await Task.WhenAll(Enumerable.Range(0,12).Select(i=>budget.WriteAsync(Path.Combine(lru,"covers",i+".img"),new byte[20])));
        Check((await budget.UsageAsync()).Bytes<=100,"concurrent cache writers stay within the global budget");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();try{await budget.WriteAsync(cold,new byte[5],canceled.Token);throw new Exception("Canceled cache write succeeded");}catch(OperationCanceledException){}
        }
        var cleared=await budget.MaintainAsync(clear:true);
        Check(cleared.Bytes==0&&cleared.Removed>0&&cleared.FreedBytes>0,"forced cache cleanup reports freed space and deletes all disposable cache entries");
        Check(!budget.Owns(Path.Combine(lru,"queue.json"))&&!budget.Owns(Path.Combine(lru,"covers","Ka4alka","movie.mkv")),"cache boundaries exclude transfer state and nested download folders");
        if(!OperatingSystem.IsWindows())
        {
            var outside=Path.Combine(parent,"cache-outside");Directory.CreateDirectory(outside);var safe=Path.Combine(outside,"keep.img");await File.WriteAllBytesAsync(safe,new byte[30]);
            var links=Path.Combine(parent,"cache-links");Directory.CreateDirectory(links);Directory.CreateSymbolicLink(Path.Combine(links,"covers"),outside);
            var isolated=new CacheStorage(links,10,()=>now);await isolated.MaintainAsync(clear:true);
            Check(File.Exists(safe)&&!await isolated.WriteAsync(Path.Combine(links,"covers","keep.img"),new byte[1]),"cache maintenance never follows a directory symlink into user files");
        }
        Check(CacheStorage.DefaultLimit==1024L*1024*1024,"production cache is capped at one gigabyte");
    }
}

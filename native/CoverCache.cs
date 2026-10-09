using System.IO;

namespace Kachalka;

public static class CoverCache
{
    // Cache hits may finish synchronously. Keep both file metadata checks and
    // image decoding off the caller's Dispatcher, even in that fast path.
    // WPF callers freeze their decoded bitmap before it crosses this boundary.
    public static Task<(T Image,bool Downloaded)> Load<T>(
        string? path,int maxBytes,Func<CancellationToken,Task<byte[]>> fetch,Func<byte[],T> decode,CancellationToken ct)
        =>Task.Run(()=>LoadCore(path,maxBytes,fetch,decode,ct),ct);

    static async Task<(T Image,bool Downloaded)> LoadCore<T>(
        string? path,int maxBytes,Func<CancellationToken,Task<byte[]>> fetch,Func<byte[],T> decode,CancellationToken ct)
    {
        if(path!=null&&File.Exists(path))
        {
            try
            {
                var length=new FileInfo(path).Length;
                if(length<=0||length>maxBytes)throw new InvalidDataException("Повреждённый кэш постера.");
                var cached=await CacheFiles.ReadAllBytesAsync(path,ct).ConfigureAwait(false);
                if(cached.Length==0||cached.Length>maxBytes)throw new InvalidDataException("Повреждённый кэш постера.");
                ct.ThrowIfCancellationRequested();
                var cachedImage=decode(cached);ct.ThrowIfCancellationRequested();
                return (cachedImage,false);
            }
            catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
            {
                try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }

        var bytes=await fetch(ct).ConfigureAwait(false);
        if(bytes.Length==0||bytes.Length>maxBytes)throw new InvalidDataException("Источник вернул неверный размер постера.");
        ct.ThrowIfCancellationRequested();
        var image=decode(bytes);
        ct.ThrowIfCancellationRequested();
        if(path!=null)
        {
            try{await CacheFiles.WriteAllBytesAsync(path,bytes,ct).ConfigureAwait(false);}
            catch(IOException){}catch(UnauthorizedAccessException){}

        }
        return (image,true);
    }
}

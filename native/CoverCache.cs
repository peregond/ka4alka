using System.IO;

namespace Kachalka;

public static class CoverCache
{
    public static async Task<(T Image,bool Downloaded)> Load<T>(
        string? path,int maxBytes,Func<CancellationToken,Task<byte[]>> fetch,Func<byte[],T> decode,CancellationToken ct)
    {
        if(path!=null&&File.Exists(path))
        {
            try
            {
                var length=new FileInfo(path).Length;
                if(length<=0||length>maxBytes)throw new InvalidDataException("Повреждённый кэш постера.");
                var cached=await CacheFiles.ReadAllBytesAsync(path,ct);
                if(cached.Length==0||cached.Length>maxBytes)throw new InvalidDataException("Повреждённый кэш постера.");
                return (decode(cached),false);
            }
            catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
            {
                try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }

        var bytes=await fetch(ct);
        if(bytes.Length==0||bytes.Length>maxBytes)throw new InvalidDataException("Источник вернул неверный размер постера.");
        var image=decode(bytes);
        if(path!=null)
        {
            try{await CacheFiles.WriteAllBytesAsync(path,bytes,ct);}
            catch(IOException){}catch(UnauthorizedAccessException){}

        }
        return (image,true);
    }
}

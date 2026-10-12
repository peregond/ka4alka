using System.IO;
using System.Windows.Media.Imaging;
namespace Kachalka;

internal static class PosterBitmapCache
{
    internal static Task<(BitmapImage Image,bool Downloaded)> Load(string? originalPath,int width,Func<CancellationToken,Task<byte[]>> fetch,CancellationToken ct)
        =>Task.Run(()=>LoadCore(originalPath,width,fetch,ct),ct);

    static async Task<(BitmapImage Image,bool Downloaded)> LoadCore(string? originalPath,int width,Func<CancellationToken,Task<byte[]>> fetch,CancellationToken ct)
    {
        BitmapImage Decode(byte[] bytes)
        {
            using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=width;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }
        var thumbnailPath=originalPath==null?null:originalPath+".w"+width+".png";
        if(thumbnailPath!=null&&File.Exists(thumbnailPath))
        {
            try
            {
                if(new FileInfo(thumbnailPath).Length is <=0 or >2*1024*1024)throw new InvalidDataException("Повреждённая миниатюра.");
                var bytes=await CacheFiles.ReadAllBytesAsync(thumbnailPath,ct).ConfigureAwait(false);
                if(bytes.Length is <=0 or >2*1024*1024)throw new InvalidDataException("Повреждённая миниатюра.");
                ct.ThrowIfCancellationRequested();var bitmap=Decode(bytes);ct.ThrowIfCancellationRequested();return(bitmap,false);
            }
            catch(Exception error)when(error is not OperationCanceledException and not OutOfMemoryException)
            {try{File.Delete(thumbnailPath);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        var result=await CoverCache.Load(originalPath,1024*1024,fetch,Decode,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(thumbnailPath!=null)
        {
            try
            {
                // Decode once on a cold load; persist that same immutable
                // bitmap for future starts instead of decoding the PNG again.
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result.Image));using var output=new MemoryStream();encoder.Save(output);
                ct.ThrowIfCancellationRequested();
                if(output.Length<=2*1024*1024)await CacheFiles.WriteAllBytesAsync(thumbnailPath,output.ToArray(),ct).ConfigureAwait(false);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return result;
    }
}

using System.IO;
using System.Windows.Media.Imaging;
namespace Kachalka;

internal static class PosterBitmapCache
{
    internal static Task<(BitmapImage Image,bool Downloaded)> Load(string? originalPath,int width,Func<CancellationToken,Task<byte[]>> fetch,CancellationToken ct)
    {
        BitmapImage Decode(byte[] bytes)
        {
            using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=width;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }
        // Derived images are reproducible cache files, covered by the existing
        // disk quota. The original stays usable for other DPI/size variants.
        if(originalPath==null)return CoverCache.Load<BitmapImage>(null,1024*1024,fetch,Decode,ct);
        return CoverCache.Load(originalPath+".w"+width+".png",2*1024*1024,async token=>
        {
            var (bitmap,_)=await CoverCache.Load(originalPath,1024*1024,fetch,Decode,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
        },Decode,ct);
    }
}

using Kachalka;

public static class CoverCachePerformanceTests
{
    sealed class CallerContext:SynchronizationContext;

    public static async Task Run(string root)
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var path=Path.Combine(root,"worker-cover.img");var valid=new byte[]{1,2,3};await File.WriteAllBytesAsync(path,valid);
        var caller=new CallerContext();var fetched=0;var decodedOutsideCaller=false;
        byte[] Decode(byte[] bytes)
        {
            decodedOutsideCaller=!ReferenceEquals(SynchronizationContext.Current,caller);
            if(!bytes.SequenceEqual(valid))throw new InvalidDataException("Damaged fixture image");
            return bytes;
        }
        Task<(byte[] Image,bool Downloaded)> Start(string? cache)
        {
            var previous=SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(caller);
                return CoverCache.Load(cache,128,_=>{Interlocked.Increment(ref fetched);return Task.FromResult(valid);},Decode,CancellationToken.None);
            }
            finally{SynchronizationContext.SetSynchronizationContext(previous);}
        }
        var fetchedImage=await Start(null);
        Check(decodedOutsideCaller&&fetchedImage.Downloaded&&fetched==1,"a synchronously completed image fetch still decodes outside the caller context");
        fetched=0;decodedOutsideCaller=false;var cached=await Start(path);
        Check(decodedOutsideCaller&&!cached.Downloaded&&fetched==0&&cached.Image.SequenceEqual(valid),"a cached image decodes outside the caller context without downloading again");
        decodedOutsideCaller=false;await File.WriteAllBytesAsync(path,[9]);var repaired=await Start(path);
        Check(decodedOutsideCaller&&repaired.Downloaded&&fetched==1&&repaired.Image.SequenceEqual(valid),"a corrupt cached image still repairs through the background image pipeline");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();var called=false;
            try{await CoverCache.Load<byte[]>(null,128,_=>{called=true;return Task.FromResult(valid);},Decode,canceled.Token);throw new Exception("Canceled image pipeline succeeded");}catch(OperationCanceledException){}
            Check(!called,"a canceled unloaded image does not start a fetch or decode");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var latePath=Path.Combine(root,"canceled-cover.img");
            try{await CoverCache.Load(latePath,128,_=>Task.FromResult(valid),bytes=>{cancellation.Cancel();return bytes;},cancellation.Token);throw new Exception("Canceled decode was published");}catch(OperationCanceledException){}
            Check(!File.Exists(latePath),"cancellation during decoding prevents publishing or storing an obsolete image");
        }
        var posterPath=Path.Combine(root,"thumbnail-original.img");var posterFetches=0;
        var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(420,630,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[420*630*4],420*4);bitmap.Freeze();
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=new MemoryStream();png.Save(output);var posterBytes=output.ToArray();
        Task<byte[]> FetchPoster(CancellationToken token){Interlocked.Increment(ref posterFetches);return Task.FromResult(posterBytes);}
        // The implementation is internal to the app; invoke through a public
        // test seam in the same native assembly via reflection.
        var posterCache=typeof(CoverCache).Assembly.GetType("Kachalka.PosterBitmapCache")!;
        var load=posterCache.GetMethod("Load",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        async Task<System.Windows.Media.Imaging.BitmapImage> Poster(int width)
        {
            var task=(Task<(System.Windows.Media.Imaging.BitmapImage Image,bool Downloaded)>)load.Invoke(null,[posterPath,width,(Func<CancellationToken,Task<byte[]>>)FetchPoster,CancellationToken.None])!;
            return (await task).Image;
        }
        var small=await Poster(100);var repeated=await Poster(100);var large=await Poster(280);
        Check(posterFetches==1&&small.PixelWidth==100&&repeated.PixelWidth==100&&large.PixelWidth==280&&small.IsFrozen&&large.IsFrozen,
            "disk thumbnails survive repeated loads and DPI variants reuse one original download");
        Check(File.Exists(posterPath+".w100.png")&&File.Exists(posterPath+".w280.png"),"both decoded-size variants are actually persisted on disk");
        await File.WriteAllBytesAsync(posterPath+".w100.png",[9]);var repairedThumbnail=await Poster(100);
        Check(posterFetches==1&&repairedThumbnail.PixelWidth==100,"a corrupt thumbnail regenerates from the original without another network request");
    }
}

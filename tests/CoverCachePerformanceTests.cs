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
    }
}

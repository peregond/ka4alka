using Kachalka;

static class BundledCatalogTests
{
    public static async Task Run()
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        using var release=new ManualResetEventSlim();
        var workerStarted=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starterAvailable=new TaskCompletionSource<IReadOnlyList<MediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callerThread=0;var blockedRows=0;
        var caller=new Thread(()=>
        {
            callerThread=Environment.CurrentManagedThreadId;
            OnlineIndexClient.BeforeMedia=()=>
            {
                var parsingThread=Environment.CurrentManagedThreadId;
                if(parsingThread==callerThread)return;
                Interlocked.Increment(ref blockedRows);workerStarted.TrySetResult(parsingThread);
                if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Full-seed test gate timed out.");
            };
            try{starterAvailable.TrySetResult(BundledCatalog.All);}
            catch(Exception error){starterAvailable.TrySetException(error);}
        });
        caller.Start();
        try
        {
            var starter=await starterAvailable.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var workerThread=await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var loading=BundledCatalog.EnsureLoadedAsync();var second=BundledCatalog.EnsureLoadedAsync();
            Check(workerThread!=callerThread&&starter.Count>0&&starter.Count<=80&&!loading.IsCompleted,"real embedded full-seed parsing runs on a worker while the caller receives bounded starter cards");
            Check(ReferenceEquals(loading,second)&&blockedRows==1,"concurrent full-seed waits join one parser task");
            using var cancel=new CancellationTokenSource();cancel.Cancel();var cancelled=false;
            try{await BundledCatalog.EnsureLoadedAsync(cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&!loading.IsCompleted,"cancelled navigation stops its seed wait without cancelling the shared parser");
            release.Set();await loading.WaitAsync(TimeSpan.FromSeconds(10));
            Check(BundledCatalog.Count("Фильмы")>=2000&&BundledCatalog.Page("Фильмы",50).Items.Length==40&&BundledCatalog.All.Count>starter.Count,"completed embedded seed replaces starter cards and provides the full local library");
        }
        finally{release.Set();OnlineIndexClient.BeforeMedia=null;caller.Join();}
    }
}

using System.Diagnostics;
using System.Windows.Threading;

namespace Kachalka;

// Keep image arrivals below input/render priority, with one timer only while work exists.
// Replacing a queued key drops the obsolete bitmap instead of accumulating callbacks.
internal sealed class UiWorkQueue<TKey>(Dispatcher dispatcher,int limit):IDisposable where TKey:notnull
{
    readonly Dictionary<TKey,Action> work=[];
    readonly DispatcherTimer timer=new(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromMilliseconds(16)};
    bool listening,disposed;
    internal int Pending=>work.Count;
    internal int MaxBatch {get;private set;}
    internal int Batches {get;private set;}
    internal void Set(TKey key,Action action)
    {
        if(disposed)return;
        work[key]=action;
        if(!listening){timer.Tick+=Tick;listening=true;}
        timer.Start();
    }
    void Tick(object? sender,EventArgs args)
    {
        var watch=Stopwatch.StartNew();var count=0;
        while(work.Count>0&&count<Math.Max(1,limit)&&(count==0||watch.Elapsed.TotalMilliseconds<2))
        {
            var next=work.First();work.Remove(next.Key);next.Value();count++;
        }
        Batches++;MaxBatch=Math.Max(MaxBatch,count);
        if(work.Count==0)timer.Stop();
    }
    public void Dispose(){disposed=true;timer.Stop();timer.Tick-=Tick;work.Clear();}
}

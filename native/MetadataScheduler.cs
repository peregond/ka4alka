namespace Kachalka;

// An opened film must not wait behind the ratings for every card in the catalog.
public sealed class MetadataScheduler(int? backgroundConcurrency=null)
{
    readonly SemaphoreSlim catalog=new(backgroundConcurrency??(PerformanceBudget.HardwareConstrained?1:2)),detail=new(2),liteCatalog=new(1);
    public async Task<T> Run<T>(bool priority,Func<Task<T>> load,CancellationToken ct=default)
    {
        var slots=priority?detail:!backgroundConcurrency.HasValue&&PerformanceBudget.LiteMode?liteCatalog:catalog;
        await slots.WaitAsync(ct);
        try{return await load();}
        finally{slots.Release();}
    }
}

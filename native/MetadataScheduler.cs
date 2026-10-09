namespace Kachalka;

// An opened film must not wait behind the ratings for every card in the catalog.
public sealed class MetadataScheduler
{
    readonly SemaphoreSlim catalog=new(2),detail=new(2);
    public async Task<T> Run<T>(bool priority,Func<Task<T>> load,CancellationToken ct=default)
    {
        var slots=priority?detail:catalog;
        await slots.WaitAsync(ct);
        try{return await load();}
        finally{slots.Release();}
    }
}

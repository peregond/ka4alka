namespace Kachalka;

// An opened film must not wait behind the ratings for every card in the catalog.
public sealed class MetadataScheduler
{
    readonly SemaphoreSlim catalog=new(2),detail=new(1);
    public async Task<T> Run<T>(bool priority,Func<Task<T>> load)
    {
        var slots=priority?detail:catalog;
        await slots.WaitAsync();
        try{return await load();}
        finally{slots.Release();}
    }
}

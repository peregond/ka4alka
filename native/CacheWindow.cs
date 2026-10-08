using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    bool cacheMaintenanceRunning;
    static string CacheSize(long bytes)=>bytes>=1024L*1024*1024?$"{bytes/(1024d*1024*1024):0.##} ГБ":bytes>=1024*1024?$"{bytes/(1024d*1024):0.#} МБ":bytes>=1024?$"{bytes/1024d:0.#} КБ":$"{bytes} Б";
    async Task MaintainCacheAsync()
    {
        if(cacheMaintenanceRunning||closed)return;cacheMaintenanceRunning=true;
        try{await Task.Run(()=>CacheFiles.Current.MaintainAsync());}
        catch(Exception error){ErrorLog.Write(error);}
        finally{cacheMaintenanceRunning=false;}
    }
    async Task ShowCacheSizeAsync(TextBlock label)
    {
        try{var usage=await Task.Run(()=>CacheFiles.Current.UsageAsync());label.Text="Занято "+CacheSize(usage.Bytes)+" из 1 ГБ";}
        catch{label.Text="Не удалось подсчитать размер кэша.";}
    }
    async Task<CacheUsage> ClearCacheAsync()
    {
        ResetCatalogQualityChecks();ResetDiscoveryData();
        liveRequest?.Cancel();personRequest?.Cancel();CancelDetailMetadata(restart:true);foreach(var view in releaseViews.Values)view.Request?.Cancel();
        var result=await Task.Run(()=>CacheFiles.Current.MaintainAsync(clear:true));
        coverCache.Clear();featurePosters.Clear();cardMetadata.Clear();priorityMetadata.Clear();requestedDetails.Clear();
        catalogPages.Clear();liveReleases.Clear();cachedReleaseViews.Clear();releaseViews.Clear();liveItems=[];liveKey="";catalogLastPage=null;liveLoading=false;
        await catalogIndex.ClearMemoryAsync();await sharedCatalog.ClearMemoryAsync();return result;
    }
}

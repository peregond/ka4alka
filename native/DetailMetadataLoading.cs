using System.IO;

namespace Kachalka;

public partial class MainWindow
{
    sealed class DetailMetadataView
    {
        public bool Loading,Failed;
        public CancellationTokenSource Request=new();
        public Task<MediaItem> Task=null!;
    }
    readonly Dictionary<int,DetailMetadataView> detailMetadataViews=[];
    readonly HashSet<int> resolvedPriorityMetadata=[];
    CancellationTokenSource metadataLifetime=new();

    bool DetailMetadataLoading(int id)=>detailMetadataViews.TryGetValue(id,out var view)&&view.Loading;
    bool DetailMetadataNeedsRetry(int id)=>detailMetadataViews.TryGetValue(id,out var view)?!view.Loading&&view.Failed:current?.Id==id&&(!MediaMetadata.HasDescription(current)||current.People.Length==0);
    string DetailDescriptionText(MediaItem item)=>MediaMetadata.HasDescription(item)?item.Description!:DetailMetadataLoading(item.Id)?"Загружаем описание…":"Описание пока недоступно.";

    void CancelDetailMetadata(bool restart=false)
    {
        metadataLifetime.Cancel();foreach(var view in detailMetadataViews.Values)view.Request.Cancel();
        if(!restart)return;
        metadataLifetime.Dispose();metadataLifetime=new();detailMetadataViews.Clear();resolvedPriorityMetadata.Clear();
    }
    void RetryDetailMetadata(MediaItem item)
    {
        if(DetailMetadataLoading(item.Id))return;
        onlineIndex.RetryNow();_ = FetchDetailMetadata(item,true);RefreshDetail(item.Id);
    }
    Task<MediaItem> FetchDetailMetadata(MediaItem item,bool force=false)
    {
        if(detailMetadataViews.TryGetValue(item.Id,out var prior)&&!force)return prior.Task;
        prior?.Request.Cancel();
        var view=new DetailMetadataView{Loading=true,Request=CancellationTokenSource.CreateLinkedTokenSource(metadataLifetime.Token)};
        detailMetadataViews[item.Id]=view;
        bool IsCurrent()=>!closed&&!view.Request.IsCancellationRequested&&detailMetadataViews.TryGetValue(item.Id,out var active)&&ReferenceEquals(active,view);
        void Publish(MediaItem fresh)
        {
            if(!IsCurrent())return;
            item.SetScores(fresh.Kinopoisk,fresh.Imdb,fresh.Genre);
            liveItems=liveItems.Select(x=>x.Id==fresh.Id?fresh:x).ToArray();
            if(current?.Id==item.Id){current=MediaMetadata.Merge(current,fresh);ApplyKnownQuality(current);}
            RefreshDetail(item.Id);
        }
        async Task<MediaItem> Load()
        {
            await Task.Yield();var data=item;
            try
            {
                data=await Metadata(item,true,view.Request.Token,Publish,force);
                if(!IsCurrent())return data;
                view.Failed=!MediaMetadata.HasDescription(data)||data.People.Length==0;
                view.Loading=false;Publish(data);
                if(prefs.LiveFavorites.Any(x=>x.Id==data.Id)){prefs.LiveFavorites=prefs.LiveFavorites.Select(x=>x.Id==data.Id?MediaMetadata.Merge(x,data):x).ToList();prefs.Save();}
                try{await catalogIndex.AddAsync([data],view.Request.Token);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            catch(OperationCanceledException) when(view.Request.IsCancellationRequested){}
            catch(Exception error)
            {
                if(IsCurrent()){view.Failed=true;cardMetadata.Remove(item.Id);resolvedPriorityMetadata.Remove(item.Id);ErrorLog.Write(error);}
            }
            finally{if(IsCurrent()){view.Loading=false;RefreshDetail(item.Id);}}
            return data;
        }
        view.Task=Load();return view.Task;
    }
}

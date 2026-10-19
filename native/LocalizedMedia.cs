using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Kachalka;


public partial class MainWindow
{
    Task<MediaItem> EnglishMetadata(MediaItem item,bool priority,CancellationToken ct,Action<MediaItem>? updated,bool force)
    {
        if(force)cardMetadata.Remove(item.Id);
        if(cardMetadata.TryGetValue(item.Id,out var cached)&&!cached.IsCanceled&&!cached.IsFaulted)return cached;
        if(!ct.CanBeCanceled)ct=metadataLifetime.Token;
        async Task<MediaItem> Load()
        {
            var result=await metadataScheduler.Run(priority,async()=>
            {
                return item with{English=await EnglishMedia.LoadAsync(onlineIndex,item,force,ct)};
            },ct);
            ct.ThrowIfCancellationRequested();updated?.Invoke(result);return result;
        }
        var task=Load();if(cardMetadata.Count>=100)cardMetadata.Remove(cardMetadata.Keys.First());cardMetadata[item.Id]=task;return task;
    }
}

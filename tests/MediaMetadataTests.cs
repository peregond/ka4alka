using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;

static class MediaMetadataTests
{
    sealed class Handler(string html):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Calls++;return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(html)});}
    }
    public static async Task Run()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var item=new MediaItem(-73991,"Фильм","Фильмы","",2026,"—","—","#526B69"){PageUrl="https://w6.zona.plus/movies/detail-metadata-"+Guid.NewGuid().ToString("N"),OriginalTitle="Movie"};
        var actor=new CinemaPerson("Участник","Актёры","");
        var complete=item with{Description="Описание фильма из источника.",People=[actor],Kinopoisk="7.9"};
        Check(!MediaMetadata.HasFullDetails(item)&&MediaMetadata.HasFullDetails(complete),"an original title in a catalog task cannot stand in for an opened film's description and credits");
        var never=new TaskCompletionSource<MediaItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=new TaskCompletionSource<MediaItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request=MediaMetadata.Load(item,_=>never.Task,_=>Task.FromResult(complete),fresh=>first.TrySetResult(fresh),timeout:TimeSpan.FromMilliseconds(250));
        var early=await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(early.Description==complete.Description&&early.People.Length==1&&!request.IsCompleted,"description and actors appear before a stalled index settles");
        var finished=await request.WaitAsync(TimeSpan.FromSeconds(2));
        Check(finished.Available&&finished.Item.People.Length==1&&finished.Item.Description==complete.Description,"deadline preserves completed source metadata even when another provider ignores cancellation");
        var failed=await MediaMetadata.Load(item,_=>Task.FromException<MediaItem>(new HttpRequestException("offline")),_=>Task.FromException<MediaItem>(new IOException("offline")));
        Check(!failed.Available&&failed.Item.Description==null,"failed details settle without persisting a loading or error placeholder as a film description");
        var retried=await MediaMetadata.Load(item,_=>Task.FromException<MediaItem>(new HttpRequestException("offline")),_=>Task.FromResult(complete));
        Check(retried.Available&&MediaMetadata.HasFullDetails(retried.Item),"a retry can recover complete details after both sources failed");
        var merged=MediaMetadata.Merge(complete,item with{Kinopoisk="0",Imdb="0",Description="Описание временно недоступно."});
        Check(merged.Description==complete.Description&&merged.People.Length==1&&merged.Kinopoisk=="7.9","partial metadata and temporary placeholders cannot overwrite a confirmed biography, credits or rating");
        using(var cancel=new CancellationTokenSource())
        {
            async Task<MediaItem> Waiting(CancellationToken ct){await Task.Delay(Timeout.InfiniteTimeSpan,ct);return item;}
            var canceled=MediaMetadata.Load(item,Waiting,Waiting,ct:cancel.Token);cancel.Cancel();
            try{await canceled;throw new Exception("Metadata cancellation was swallowed");}catch(OperationCanceledException){Console.WriteLine("PASS: canceling a detail request stops both metadata providers");}
        }
        var scheduler=new MetadataScheduler();var held=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active=new[]{scheduler.Run(false,()=>held.Task),scheduler.Run(false,()=>held.Task)};
        using(var cancel=new CancellationTokenSource())
        {
            var entered=false;var queued=scheduler.Run(false,()=>{entered=true;return Task.FromResult(1);},cancel.Token);cancel.Cancel();
            try{await queued;throw new Exception("Queued metadata cancellation was swallowed");}catch(OperationCanceledException){}
            Check(!entered,"canceling a queued metadata task does not start a network request");
        }
        held.SetResult(1);await Task.WhenAll(active);

        var path=Path.Combine(Preferences.DataDir,"details",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.PageUrl!)))+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Kinopoisk="7.9",Imdb="—",Description=complete.Description,OriginalTitle="Movie",People=new[]{actor}}));
        var cachedHandler=new Handler("<html>Checking your browser…</html>");using(var client=new SourceClient(cachedHandler))
        {
            var cached=await new LiveCatalog(client).Detail(item,CancellationToken.None);
            Check(cachedHandler.Calls==0&&cached.Description==complete.Description&&cached.People.Length==1,"a complete recent metadata cache opens immediately without repeating a source request");
            File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddHours(-7));
            var before=await File.ReadAllTextAsync(path);var recovered=await new LiveCatalog(client).Detail(item,CancellationToken.None);
            Check(cachedHandler.Calls==1&&recovered.People.Length==1&&await File.ReadAllTextAsync(path)==before,"a source challenge cannot replace confirmed cached film metadata with an empty page");
        }
        await File.WriteAllTextAsync(path,"invalid-json");
        var repairedHandler=new Handler("<div itemprop='description'>Новая история.</div><span itemprop='actor'><span itemprop='name'>Участник</span></span>");using(var client=new SourceClient(repairedHandler))
        {
            var repaired=await new LiveCatalog(client).Detail(item,CancellationToken.None);
            Check(repairedHandler.Calls==1&&repaired.Description=="Новая история."&&repaired.People.Length==1,"a corrupt detail snapshot is bypassed and repaired from usable source metadata");
        }
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Kinopoisk="7.9",Imdb="—",Description="",OriginalTitle="Movie"}));
        using(var client=new SourceClient(repairedHandler))
        {
            var repaired=await new LiveCatalog(client).Detail(item,CancellationToken.None);
            Check(repaired.People.Length==1&&repaired.Description=="Новая история.","a fresh partial snapshot is supplemented instead of freezing an empty description and actor list");
        }
    }
}

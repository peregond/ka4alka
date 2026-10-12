using Kachalka;
static class UnifiedSearchTests
{
    public static async Task Run()
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        MediaItem Item(string kind,int id,string title)=>new(id,title,kind,"драма",2024,"8.0","8.0","#526B69");
        var film=Item("Фильмы",1,"Искра");var series=Item("Сериалы",1,"Искра: сериал");var called=new HashSet<string>();
        var result=await UnifiedSearch.FindAsync("Искра",(kind,_)=>{called.Add(kind);return Task.FromResult<IReadOnlyList<MediaItem>>([kind=="Фильмы"?film:series]);},kind=>[kind=="Фильмы"?film:series],default);
        Check(called.SetEquals(["Фильмы","Сериалы"])&&result.Items.Length==2,"unified search queries both catalogs and preserves cross-category identities");
        Check(result.Items[0]==film&&!result.Offline,"exact title ranks first and duplicate cached records merge");
        Check(UnifiedSearch.Filter(result.Items,"").Length==2&&UnifiedSearch.Filter(result.Items,"Сериалы").Single()==series,"result type filters do not run another search");
        var partial=await UnifiedSearch.FindAsync("Искра",(kind,_)=>kind=="Сериалы"?throw new IOException("offline"):Task.FromResult<IReadOnlyList<MediaItem>>([film]),kind=>kind=="Сериалы"?[series]:[],default);
        Check(partial.Offline&&partial.Items.Length==2,"one catalog failure retains working and saved search results");
        using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
        try{await UnifiedSearch.FindAsync("Искра",(_,token)=>Task.FromCanceled<IReadOnlyList<MediaItem>>(token),_=>[],cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled,"cancelled query cannot publish fallback results");
        var remoteGate=new TaskCompletionSource<IReadOnlyList<MediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        SearchResult? immediate=null;
        var pending=UnifiedSearch.FindAsync("Искра",(_,_)=>remoteGate.Task,kind=>kind=="Фильмы"?[film]:[series],default,updated:value=>immediate=value);
        Check(immediate?.Items.Length==2&&!pending.IsCompleted,"local catalog results appear while remote search is still pending");
        remoteGate.SetResult([]);await pending;
        var slowSeries=new TaskCompletionSource<IReadOnlyList<MediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fastFilm=new TaskCompletionSource<SearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var streaming=UnifiedSearch.FindAsync("Искра",(kind,_)=>kind=="Фильмы"?Task.FromResult<IReadOnlyList<MediaItem>>([film]):slowSeries.Task,_=>[],default,
            updated:update=>{if(update.Items.Any(x=>x.Section=="Фильмы"))fastFilm.TrySetResult(update);});
        try{await fastFilm.Task.WaitAsync(TimeSpan.FromSeconds(2));Check(!streaming.IsCompleted,"fast movie results appear without waiting for the series catalog");}
        finally{slowSeries.TrySetResult([series]);}
        Check((await streaming).Items.Length==2,"streamed results retain both categories after the slow reply");
        using var obsolete=new CancellationTokenSource();var never=new TaskCompletionSource<IReadOnlyList<MediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);var publishes=0;
        var obsoleteSearch=UnifiedSearch.FindAsync("Искра",(_,_)=>never.Task,_=>[],obsolete.Token,updated:_=>publishes++);
        obsolete.Cancel();try{await obsoleteSearch.WaitAsync(TimeSpan.FromSeconds(2));throw new Exception("Obsolete search remained alive");}catch(OperationCanceledException){}
        never.SetResult([film]);await Task.Delay(20);Check(publishes==0,"canceled searches stop waiting even when a provider ignores cancellation and never publish late rows");
    }
}


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
    }
}

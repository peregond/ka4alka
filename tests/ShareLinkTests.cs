using Kachalka;
using Microsoft.Win32;

static class ShareLinkTests
{
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var film=new MediaItem(-1,"Мстители: Финал","Фильмы","боевик",2019,"8.0","8.4","#526B69"){PageUrl=LiveCatalog.Base+"/movies/avengers-endgame-2019"};
        var link=ShareLink.Create(film);
        Check(link=="kachalka://movie/avengers-endgame-2019?title=Мстители%3A%20Финал&year=2019","a film link keeps its catalog page, a readable title and the year in one unbroken word: "+link);
        var parsed=ShareLink.Parse(link);
        Check(parsed is {Section:"Фильмы",Slug:"avengers-endgame-2019",Title:"Мстители: Финал",Year:2019}&&parsed.PageUrl==film.PageUrl,"a film link reads back to the same page, title and year");
        var series=new MediaItem(-2,"Чёрное зеркало","Сериалы","",0,"—","—","#526B69"){PageUrl=LiveCatalog.Base+"/tvseries/black-mirror"};
        Check(ShareLink.Create(series)=="kachalka://series/black-mirror?title=Чёрное%20зеркало","a series link without a year leaves the year out");
        Check(ShareLink.Parse(ShareLink.Create(series))?.PageUrl==LiveCatalog.Base+"/tvseries/black-mirror","a series link opens the series page");
        Check(ShareLink.Parse("kachalka://movie/avengers-endgame-2019?title=%D0%9C%D1%81%D1%82%D0%B8%D1%82%D0%B5%D0%BB%D0%B8&year=2019/") is {Title:"Мстители",Year:2019},"a link a browser percent-encoded still reads back");
        Check(ShareLink.Parse("Посмотри: Курьер (2026) KACHALKA://movie/kurer-2026?title=Курьер&year=2026.")?.Slug=="kurer-2026","a link inside a short message is found, whatever the letter case and trailing punctuation");
        Check(ShareLink.Parse("kachalka://movie/kurer-2026") is {Title:"",Year:0},"the title and year are optional");
        foreach(var bad in new[]{"","Курьер","https://w6.zona.plus/movies/kurer-2026","kachalka://game/kurer-2026","kachalka://movie/","kachalka://movie/a/b","kachalka://movie/..%2F..%2Fsettings","kachalka://movie/kurer%3F2026","kachalka://movie/"+new string('a',121),"kachalka://movie/%ZZ"})
            Check(ShareLink.Parse(bad)==null,"not a share link: "+(bad.Length>40?bad[..40]+"…":bad));
        var hostile=ShareLink.Parse("kachalka://movie/kurer-2026?title=%0D%0A%E2%80%AEevil%09name&year=99999");
        Check(hostile is {Title:"evil name",Year:0},"a hostile title loses control and direction-override characters and an impossible year is dropped: "+hostile?.Title);
        Check(ShareLink.Create(film with{PageUrl="https://example.test/movies/avengers-endgame-2019"})==null&&ShareLink.Create(film with{PageUrl=null})==null&&ShareLink.Create(film with{Section="Музыка"})==null,"only catalog films and series have links");

        var key="share-test-"+Guid.NewGuid().ToString("N")[..8];
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var received=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);var seen=new List<string>();
        var listening=Task.Run(()=>ShareLinkPipe.Listen(key,text=>{lock(seen)seen.Add(text);received.TrySetResult(text);},stop.Token));
        var sent=false;for(var attempt=0;attempt<20&&!sent;attempt++){sent=ShareLinkPipe.Send(key,"не ссылка");if(!sent)await Task.Delay(100);}
        Check(sent&&ShareLinkPipe.Send(key,link!),"a second copy hands its link to the running copy");
        Check(await received.Task.WaitAsync(TimeSpan.FromSeconds(10))==link&&seen.Count==1,"the running copy receives the link and ignores text that is not a share link");
        Check(!ShareLinkPipe.Send("share-test-missing-"+Guid.NewGuid().ToString("N")[..8],link!),"without a running copy the link is not sent, so the new copy opens it itself");
        stop.Cancel();await listening;

        using var classes=Registry.CurrentUser.OpenSubKey(@"Software\Classes",true)!;
        var existed=classes.OpenSubKey(ShareLink.Scheme)!=null;
        var backup=existed?Export(classes,ShareLink.Scheme):null;
        try
        {
            classes.DeleteSubKeyTree(ShareLink.Scheme,false);
            WindowsIntegration.RegisterShareLinks();
            using(var scheme=Registry.CurrentUser.OpenSubKey(WindowsIntegration.ShareLinkKey)!)
                Check(scheme.GetValue("URL Protocol") is "" &&WindowsIntegration.ShareLinksRegistered&&WindowsIntegration.ShareLinkCommand=="\""+WindowsIntegration.Executable+"\" \"%1\"","kachalka:// is registered for the current user to open this copy with the link as its only argument");
            WindowsIntegration.RegisterShareLinks();
            Check(WindowsIntegration.ShareLinksRegistered,"registering again on the next start is harmless");
        }
        finally{classes.DeleteSubKeyTree(ShareLink.Scheme,false);if(backup!=null)Import(classes,ShareLink.Scheme,backup);}
    }

    sealed record RegistryCopy(Dictionary<string,(object Value,RegistryValueKind Kind)> Values,Dictionary<string,RegistryCopy> Keys);
    static RegistryCopy Export(RegistryKey parent,string name)
    {
        using var key=parent.OpenSubKey(name)!;
        return new(key.GetValueNames().ToDictionary(x=>x,x=>(key.GetValue(x,null,RegistryValueOptions.DoNotExpandEnvironmentNames)!,key.GetValueKind(x))),key.GetSubKeyNames().ToDictionary(x=>x,x=>Export(key,x)));
    }
    static void Import(RegistryKey parent,string name,RegistryCopy copy)
    {
        using var key=parent.CreateSubKey(name,true);
        foreach(var (value,(data,kind)) in copy.Values)key.SetValue(value,data,kind);
        foreach(var (child,inner) in copy.Keys)Import(key,child,inner);
    }
}

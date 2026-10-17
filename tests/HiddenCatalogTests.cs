using Kachalka;

static class HiddenCatalogTests
{
    public static void Run()
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        var item=new MediaItem(-501,"Южный парк","Сериалы","Комедия, мультфильм",1997,"8","8","#000000"){ImdbId="tt0121955",PageUrl="https://w6.zona.plus/tvseries/south-park",GenreKeys=["komediia"]};
        var prefs=new Preferences();prefs.HiddenTitles.Add(HiddenCatalog.Title(item));
        Check(HiddenCatalog.Hidden(item with{Id=-502,Title="South Park"},prefs),"hidden titles follow the same IMDb identity across translated catalog cards");
        Check(!HiddenCatalog.Hidden(item with{Id=-503,ImdbId="tt9999999",PageUrl="https://w6.zona.plus/tvseries/another-work"},prefs),"another work does not inherit the hidden-title choice");
        prefs.HiddenTitles.Clear();prefs.HiddenGenres.Add("komediia");
        Check(HiddenCatalog.Hidden(item,prefs)&&HiddenCatalog.Hidden(item with{GenreKeys=[]},prefs),"hidden genres match stable keys and localized labels");
        Check(!HiddenCatalog.Hidden(item with{Genre="драма",GenreKeys=[]},prefs),"unknown or different genres are not guessed to be hidden");
        prefs.HiddenGenres.Clear();Check(!HiddenCatalog.Hidden(item,prefs),"restoring a genre returns its titles without changing the catalog");
        IEnumerable<CatalogChoice> ExpensiveChoices()=>Enumerable.Range(0,1).Select<int,CatalogChoice>(_=>throw new Exception("Unused genre choices were enumerated"));
        Check(!HiddenCatalog.Hidden(item,prefs,ExpensiveChoices()),"empty personal filters skip identity and genre matching entirely");
        var first=item with{Id=1,Title="Локальная карточка A",ImdbId=null,PageUrl=null};var second=first with{Id=2,Title="Локальная карточка B"};prefs.HiddenTitles.Add(HiddenCatalog.Title(first));
        Check(!HiddenCatalog.Hidden(second,prefs),"local cards without a page still keep independent hidden identities");
        var path=Path.Combine(Preferences.DataDir,"settings.json");var original=File.Exists(path)?File.ReadAllBytes(path):null;
        try
        {
            prefs.HiddenTitles.Clear();prefs.HiddenTitles.Add(HiddenCatalog.Title(item));prefs.HiddenGenres.Add("uzhasy");prefs.Save();var restored=Preferences.Load();
            Check(HiddenCatalog.Hidden(item,restored)&&restored.HiddenGenres.Contains("uzhasy"),"hidden titles and genres survive application restart");
        }
        finally{if(original==null)File.Delete(path);else File.WriteAllBytes(path,original);}
    }
}

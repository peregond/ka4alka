using System.Diagnostics;
using Kachalka;

static class SearchIndexerTests
{
    public static async Task Run()
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: indexer "+label);}
        MediaItem Item(int id,string title,int year=2026,string section="Фильмы")=>new(id,title,section,"",year,"—","—","#526B69"){PageUrl="https://w6.zona.plus/movies/fixture-"+id};
        var exact=Item(1,"Backrooms") with{ImdbId="tt22000001",TitleAliases=["Закулисье реальности"]};
        var verified=MediaIdentity.Enrich(Item(90,"Закулисье реальности"));
        Check(verified.ImdbId=="tt26657236"&&MediaIdentity.Queries(verified).Contains("Backrooms")&&MediaIdentity.Queries(verified).Contains("Закулисье реальности"),"Backrooms has verified Russian/English queries tied to the same 2026 film");
        Check(MediaIdentity.Enrich(Item(91,"Закулисье реальности",2022)).ImdbId==null,"the verified Backrooms alias cannot identify a different year's film");
        var related=Item(2,"Backrooms: Behind the Scenes");var wrong=Item(3,"The Courier");var old=Item(4,"Backrooms",2022);
        var rows=new[]{wrong,related,old,exact};
        Check(CatalogSearch.Find(rows,"Закулисье реальности").Single()==exact,"verified translated title finds canonical work");
        Check(CatalogSearch.Find(rows,"Backrooms").First().Title=="Backrooms"&&!CatalogSearch.Find(rows,"Backrooms").Contains(wrong),"exact names lead and irrelevant provider replies are discarded");
        Check(CatalogSearch.Find(rows,"Backrooms 2026").All(x=>x.Year==2026)&&CatalogSearch.Find(rows,"Backrooms 2026").First()==exact,"explicit year excludes a different release of the same name");
        Check(CatalogSearch.Find(rows,"tt22000001").Single()==exact&&CatalogSearch.Find(rows,"https://www.imdb.com/title/tt22000001/").Single()==exact,"known IMDb id and title URL resolve exactly");
        var south=Item(5,"Южный парк",1997,"Сериалы");
        Check(CatalogSearch.Find([south],".;ysq gfhr").Single()==south&&CatalogSearch.Find([south],"Ыщгер Зфкл").Single()==south,"wrong keyboard layout searches both Russian and English names");
        Check(CatalogSearch.Find([exact],"backroms").Single()==exact&&CatalogSearch.Find([exact],"bakcrooms").Single()==exact,"a single missing letter or adjacent transposition can recover an otherwise empty search");
        Check(CatalogSearch.Find([exact],"blacrrms").Length==0,"several misspellings do not produce arbitrary suggestions");
        var typo=Item(6,"Backroms");
        Check(CatalogSearch.Find([exact,typo],"Backrooms").SequenceEqual([exact]),"typo fallback never crowds out an exact result");
        Check(MediaIdentity.Merge([exact,typo]).Length==2,"search tolerance never merges different works");
        var portal2=Item(7,"Portal 2",2011,"Игры");var portal3=Item(8,"Portal 3",2026,"Игры");
        Check(CatalogSearch.Find([portal3,portal2],"Portal 2").Single()==portal2,"numbered sequels are not fuzzy matches");
        var future=Item(9,"Blade Runner 2049",2017);
        Check(CatalogSearch.Find([future],"Blade Runner 2049").Single()==future,"a year-shaped title word remains part of the title");
        var large=Enumerable.Range(10,100).Select(id=>Item(id,"Unrelated "+id)).Append(exact).ToArray();
        var ranked=await UnifiedSearch.FindAsync("Backrooms",(_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>(large),_=>[],default);
        Check(ranked.Items.Single().Id==exact.Id,"a relevant provider result beyond the old 80-row cutoff is retained");
        var one=exact with{Title="Original",TitleAliases=[]};var two=one with{TitleAliases=["Закулисье"]};
        var joined=await UnifiedSearch.FindAsync("Закулисье",(_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>([one,two]),_=>[],default);
        Check(joined.Items.Single().TitleAliases.Contains("Закулисье"),"same-id metadata is merged before query filtering");

        var knabenUri=KnabenSource.QueryUri("Backrooms");
        Check(knabenUri.AbsolutePath=="/v2/search"&&knabenUri.Query.Contains("t=rutracker")&&knabenUri.Query.Contains("s=150")&&!knabenUri.Query.Contains("unsafe"),"Knaben v2 searches the target tracker with a bounded larger page and safe defaults");
        var captured=KnabenSource.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","knaben-v2-backrooms-2026.json")),MediaIdentity.Enrich(Item(90,"Закулисье реальности")));
        Check(captured.Count>0&&captured.All(x=>x.Source=="RuTracker"&&x.Title.Contains("2026")&&ReleaseQuality.Downloadable(x)),"real v2 response preserves downloadable Backrooms releases and rejects different works");
        var hash=new string('a',40);
        SourceEntry Entry(string id)=>new(id,"Fixture","Fixture","https://example.test/"+id,"magnet:?xt=urn:btih:"+hash,null);
        var never=new TaskCompletionSource<IReadOnlyList<SourceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer=Stopwatch.StartNew();
        var partial=await ReleaseAliasSearch.Run(["Good","Slow"],(query,_)=>query=="Good"?Task.FromResult<IReadOnlyList<SourceEntry>>([Entry("good")]):never.Task,default,TimeSpan.FromMilliseconds(80));
        Check(partial.Count==1&&timer.Elapsed<TimeSpan.FromSeconds(2),"a stalled alias cannot erase a successful answer or keep its source alive");
        never.SetResult([]);
        var active=0;var peak=0;var calls=0;
        var collected=await ReleaseAliasSearch.Run(["a","b","c","d","e"],async(query,ct)=>
        {
            calls++;var count=Interlocked.Increment(ref active);peak=Math.Max(peak,count);
            try{await Task.Delay(10,ct);return (IReadOnlyList<SourceEntry>)[new(query,"Fixture","Fixture","https://example.test/"+query,null,null)];}
            finally{Interlocked.Decrement(ref active);}
        },default);
        Check(collected.Count==4&&calls==4&&peak==2,"all alternate-name batches are retained with at most two concurrent requests and four queries");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();try{await ReleaseAliasSearch.Run(["a"],(_,_)=>Task.FromResult<IReadOnlyList<SourceEntry>>([]),cancel.Token);throw new Exception("Cancellation swallowed.");}catch(OperationCanceledException){}
        }
        var catalog=Enumerable.Range(1,15000).Select(id=>Item(id,"Каталожная запись номер "+id)).Append(exact).ToArray();
        _=CatalogSearch.Find(catalog,"Backrooms");
        var allocated=GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();for(var i=0;i<8;i++)if(CatalogSearch.Find(catalog,"Backrooms").Single().Id!=1)throw new Exception("Large catalog lost exact match.");
        var average=timer.Elapsed.TotalMilliseconds/8;
        var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(bytes<2_000_000,"eight warm 15000-item searches keep temporary allocation below 2 MB: "+bytes+" bytes");
        Check(average<120,"warm 15000-item search stays bounded: "+average.ToString("F1")+" ms");
    }
}

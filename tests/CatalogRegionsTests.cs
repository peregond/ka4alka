using System.Net;
using System.Text;
using System.Text.Json;
using Kachalka;

static class CatalogRegionsTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> read):HttpMessageHandler
    {
        public int Calls,DetailCalls,Concurrent,Maximum;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);var detail=!request.RequestUri!.AbsolutePath.Contains("/filter/",StringComparison.Ordinal);
            if(detail)Interlocked.Increment(ref DetailCalls);
            var active=Interlocked.Increment(ref Concurrent);
            int old;do{old=Maximum;if(old>=active)break;}while(Interlocked.CompareExchange(ref Maximum,active,old)!=old);
            try{return await read(request,ct);}finally{Interlocked.Decrement(ref Concurrent);}
        }
    }
    static HttpResponseMessage Reply(string body,string type="application/json")=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,type)};
    static MediaItem Film(string name,params string[] countries)=>new(-name.GetHashCode(),name,"Фильмы","",2024,"—","—","#526B69"){CountryKeys=countries,PageUrl="https://w6.zona.plus/movies/"+name};
    static string Html(IEnumerable<MediaItem> rows,bool native=false)=>
        (native?"<select><option value='country-rossiia' selected>Россия</option></select>":"")+string.Concat(rows.Select(item=>$"<li class='results-item-wrap'><a itemprop='url' href='/movies/{item.Title}'><span itemprop='name'>{item.Title}</span></a><span class='results-item-year'>{item.Year}</span></li>"));
    static string Json(MediaItem item,string countryIds,bool wrong=false)=>JsonSerializer.Serialize(new
    {
        movie=new{name_rus=wrong?"Другой фильм":item.Title,year=item.Year,country_id=countryIds},
        countries=new[]{new{id=1,name="США",translit="ssha"},new{id=2,name="Россия",translit="rossiia"},new{id=6,name="Канада",translit="kanada"}}
    });
    public static async Task Run()
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var native=Film("native","rossiia");var foreign=Film("foreign","ssha");var mixed=Film("mixed","rossiia","ssha");var unknown=Film("unknown");
        Check(CatalogRegions.HomeCountry(null)=="rossiia"&&CatalogRegions.HomeCountry("fixture-country")=="rossiia"&&CatalogRegions.HomeCountry(" KANADA ")=="kanada","home country defaults to Russia and rejects unknown persisted selections");
        Check(CatalogRegions.IsNative(native,"rossiia")&&!CatalogRegions.IsForeign(native,"rossiia")&&CatalogRegions.IsForeign(foreign,"rossiia"),"regional shelves classify actual country keys relative to the selected home country");
        Check(CatalogRegions.IsNative(mixed,"rossiia")&&!CatalogRegions.IsForeign(mixed,"rossiia"),"a coproduction with the home country appears only in its native shelf");
        Check(CatalogRegions.IsNative(native with{CountryKeysComplete=false},"rossiia")&&!CatalogRegions.IsForeign(native with{CountryKeysComplete=false},"ssha"),"a single confirmed country membership cannot prove a film is foreign after the home country changes");
        Check(!CatalogRegions.IsNative(unknown with{Country="Россия"},"rossiia")&&!CatalogRegions.IsForeign(unknown with{Title="English title",Country="США"},"rossiia"),"country labels and film titles cannot invent missing source country keys");
        Check(!CatalogRegions.IsForeign(Film("invalid","../rossiia"),"rossiia")&&CatalogRegions.IsNative(Film("normalization"," ROSSiIA ","rossiia"),"rossiia"),"invalid country keys are ignored while repeated source keys normalize safely");
        Check(CatalogRegions.IsNative(foreign,"ssha")&&CatalogRegions.IsForeign(native,"ssha"),"changing the home country reverses the classification without changing film data");
        Check(CatalogRegions.Select([foreign,foreign,native,mixed,unknown],"foreign","rossiia").SequenceEqual([foreign]),"regional selection preserves source order and removes duplicate and unclassified films");
        var selection=new CatalogSelection(Genre:"drama",Region:"native",HomeCountry:"kanada");
        Check(selection.Filter=="genre-drama/country-kanada/sort-date"&&!selection.IsDefault&&new CatalogSelection(Region:"foreign",HomeCountry:"kanada").Filter=="sort-date","native source browsing uses an exact country feed while foreign browsing never invents an unsupported negation filter");
        Check(!new CatalogSelection(Region:"foreign",HomeCountry:"rossiia").Matches(mixed)&&new CatalogSelection(Region:"native",HomeCountry:"rossiia").Matches(mixed),"saved filters enforce the regional classification as well as their existing criteria");
        var proven=LiveCatalog.ParsePage(Encoding.UTF8.GetBytes(Html([unknown],true)),"Фильмы",1,new(Region:"native",HomeCountry:"rossiia"));
        Check(proven.Items.Single().CountryKeys.SequenceEqual(["rossiia"])&&!proven.Items.Single().CountryKeysComplete,"a selected source country filter proves only native membership without inventing a complete production-country list");
        Check(LiveCatalog.ParsePage(Encoding.UTF8.GetBytes(Html([unknown])),"Фильмы",1,new(Region:"native",HomeCountry:"rossiia")).Items.Single().CountryKeys.Length==0,"requesting a country filter alone cannot establish membership when the source does not confirm the selected filter");

        var suffix=Guid.NewGuid().ToString("N");
        var rows=new[]{Film("us-"+suffix),Film("ru-"+suffix),Film("mixed-"+suffix),Film("unknown-"+suffix),Film("wrong-"+suffix),Film("partial-"+suffix)};
        var ids=new Dictionary<string,string>{{rows[0].Title,"1"},{rows[1].Title,"2"},{rows[2].Title,"1 2"},{rows[3].Title,""},{rows[4].Title,"1"},{rows[5].Title,"1 99"}};
        var handler=new Handler(async(request,token)=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/filter/"))return Reply(Html(rows),"text/html");
            await Task.Delay(15,token);var name=request.RequestUri.AbsolutePath.Split('/').Last();var item=rows.Single(x=>x.Title==name);
            return Reply(Json(item,ids[name],name==rows[4].Title));
        });
        using(var client=new SourceClient(handler))
        {
            var loader=new RegionalCatalog(client);var updates=new List<int>();
            var page=await loader.BrowsePage("Фильмы",1,"foreign","rossiia",CancellationToken.None,forceRefresh:true,progress:partial=>updates.Add(partial.Items.Length));
            Check(page.Items.Select(item=>item.Title).SequenceEqual([rows[0].Title])&&!page.HasNext&&!File.Exists(RegionalCatalog.CachePath(rows[5])),"regional loading excludes coproductions, unknown countries, incomplete country dictionaries and mismatched metadata while retaining source pagination");
            Check(handler.Maximum<=2&&handler.DetailCalls==rows.Length&&updates.Count>1,"country enrichment limits concurrent requests and publishes confirmed partial shelves");
            var before=handler.DetailCalls;
            var cached=await loader.BrowsePage("Фильмы",1,"foreign","rossiia",CancellationToken.None);
            Check(cached.Items.Single().Title==rows[0].Title&&handler.DetailCalls==before+2,"positive country snapshots and confirmed unknown metadata are reused while incomplete or wrong-identity responses remain retryable");
        }
        var nativeHandler=new Handler((_,_)=>Task.FromResult(Reply(Html([unknown],true),"text/html")));
        using(var client=new SourceClient(nativeHandler))
        {
            var page=await new RegionalCatalog(client).BrowsePage("Фильмы",1,"native","rossiia",CancellationToken.None,forceRefresh:false,selection:new(Genre:"drama"));
            Check(page.Items.Length==1&&nativeHandler.DetailCalls==0,"a verified native feed avoids per-film detail requests");
        }
        var existing=Film("existing-"+suffix);
        var detailPath=Path.Combine(Preferences.DataDir,"details",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(existing.PageUrl!)))+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(detailPath)!);await File.WriteAllTextAsync(detailPath,JsonSerializer.Serialize(new{Country="США, Канада",CountryKeys=new[]{"ssha","kanada"}}));
        var detailCacheHandler=new Handler((request,_)=>request.RequestUri!.AbsolutePath.Contains("/filter/")?Task.FromResult(Reply(Html([existing]),"text/html")):throw new HttpRequestException("offline"));
        using(var client=new SourceClient(detailCacheHandler))
        {
            var page=await new RegionalCatalog(client).BrowsePage("Фильмы",1,"foreign","rossiia",CancellationToken.None,selection:new(Genre:"komediia"));
            Check(page.Items.Single().CountryKeys.SequenceEqual(["ssha","kanada"])&&detailCacheHandler.DetailCalls==0,"existing detail snapshots supply verified countries offline without repeating metadata requests");
        }
        var partialCached=Film("partial-cache-"+suffix);
        var partialPath=Path.Combine(Preferences.DataDir,"details",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(partialCached.PageUrl!)))+".json");
        await File.WriteAllTextAsync(partialPath,JsonSerializer.Serialize(new{Country="США",CountryKeys=new[]{"ssha"},CountryKeysComplete=false}));
        var restoredPartial=(await RegionalCatalog.WithCachedCountries([partialCached],CancellationToken.None)).Single();
        Check(!restoredPartial.CountryKeysComplete&&CatalogRegions.IsNative(restoredPartial,"ssha")&&!CatalogRegions.IsForeign(restoredPartial,"rossiia"),"a cached native-feed country stamp preserves its partial provenance and never becomes foreign proof");
        var partialHandler=new Handler((request,_)=>Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/filter/")?Reply(Html([partialCached]),"text/html"):Reply(Json(partialCached,"1 2"))));
        using(var client=new SourceClient(partialHandler))
        {
            var page=await new RegionalCatalog(client).BrowsePage("Фильмы",1,"foreign","rossiia",CancellationToken.None,selection:new(Genre:"uzhasy"));
            Check(page.Items.Length==0&&partialHandler.DetailCalls==1,"a partial detail cache still requests complete countries and excludes a confirmed home-country coproduction");
        }
        var online=JsonSerializer.SerializeToElement(new{id="movies:country-index",section="movies",title="Страна",year=2024,pageUrl="https://w6.zona.plus/movies/country-index",country="США",countryKeys=new[]{"ssha","../rossiia"},countryKeysComplete=false});
        using(var client=new SourceClient(new Handler((_,_)=>Task.FromResult(Reply(JsonSerializer.Serialize(new{items=new[]{online},hasMore=false}))))))
        {
            var indexed=(await new OnlineIndexClient(client).BrowsePage("Фильмы",1,CancellationToken.None)).Items.Single();
            Check(indexed is {Country:"США",CountryKeysComplete:false}&&indexed.CountryKeys.SequenceEqual(["ssha"])&&!CatalogRegions.IsForeign(indexed,"rossiia"),"shared catalog metadata retains country provenance and rejects invalid country keys");
        }
        var canceledRows=new[]{Film("cancel-"+suffix),Film("cancel-two-"+suffix)};
        var started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledHandler=new Handler(async(request,token)=>
        {
            if(request.RequestUri!.AbsolutePath.Contains("/filter/"))return Reply(Html(canceledRows),"text/html");
            started.TrySetResult(true);await Task.Delay(Timeout.Infinite,token);return Reply("{}");
        });
        using(var client=new SourceClient(canceledHandler))using(var cancel=new CancellationTokenSource())
        {
            var pending=new RegionalCatalog(client).BrowsePage("Фильмы",1,"foreign","rossiia",cancel.Token,forceRefresh:true);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));cancel.Cancel();
            try{await pending.WaitAsync(TimeSpan.FromSeconds(3));throw new Exception("Regional page ignored caller cancellation");}catch(OperationCanceledException){}
            Check(canceledHandler.DetailCalls<=2&&!File.Exists(RegionalCatalog.CachePath(canceledRows[0])),"leaving a regional page cancels pending metadata work without writing canceled snapshots");
        }
    }
}

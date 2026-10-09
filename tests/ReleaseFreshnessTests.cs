using System.Text.Json;
using Kachalka;

static class ReleaseFreshnessTests
{
    public static async Task Run(string root)
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var now=DateTime.UtcNow;
        var cached=new SourceEntry("freshness-old","Тестовый фильм (2026) WEB-DL 1080p RUS DUB","RuTor","https://example.invalid/private/passkey-value","magnet:?xt=urn:btih:"+new string('a',40),null,1024,800)
            {DataReceivedUtc=now.AddDays(-2),DataProvider="RuTor"};
        Check(ReleaseFreshness.Age(cached.DataReceivedUtc,now)==cached.DataReceivedUtc!.Value.ToLocalTime().ToString("dd.MM.yyyy"),"release age uses its original receipt time");
        Check(ReleaseFreshness.Age(now.AddMinutes(-7),now)=="7 мин назад"&&ReleaseFreshness.Age(now,now)=="сейчас","release age follows the supplied clock");
        Check(ReleaseFreshness.Age(null,now)=="время неизвестно"&&ReleaseFreshness.Age(now.AddDays(2),now)=="время неизвестно","legacy and invalid future receipts do not look fresh");

        var before=DateTime.UtcNow;
        var freshReply=await ReleaseSearch.RunAsync([new("RuTor",_=>Task.FromResult<IReadOnlyList<SourceEntry>>([cached]))]);
        var fresh=freshReply.Items.Single();
        Check(fresh is {DataProvider:"RuTor",DataReceivedUtc:not null}&&fresh.DataReceivedUtc>=before&&fresh.DataReceivedUtc<=DateTime.UtcNow,"successful source reply stamps the received row at the actual response time");
        Check(cached.DataReceivedUtc==now.AddDays(-2),"source search does not mutate an older cache row");

        var indexedReply=await ReleaseSearch.RunAsync([new("Онлайн-индекс",_=>Task.FromResult<IReadOnlyList<SourceEntry>>([cached]))]);
        var indexed=indexedReply.Items.Single();
        Check(ReleaseFreshness.Caption(indexed,DateTime.UtcNow).Contains("индекс")&&ReleaseFreshness.Details(indexed,DateTime.UtcNow).StartsWith("Данные онлайн-индекса"),"index receipt is visibly distinct from a direct tracker response");
        Check(ReleaseFreshness.Details(fresh,DateTime.UtcNow).Contains("Подключение проверяется при скачивании")&&!ReleaseFreshness.Details(fresh,DateTime.UtcNow).Contains("passkey-value"),"freshness does not claim peer availability or expose release URLs");

        var failed=await ReleaseSearch.RunAsync([new("RuTor",_=>Task.FromException<IReadOnlyList<SourceEntry>>(new HttpRequestException("https://tracker.invalid/private/path?passkey=secret-fixture")))]);
        var failedCheck=failed.Sources.Single();
        Check(failedCheck is {State:SourceState.Unavailable,CheckedUtc:not null,LastSuccessUtc:null}&&failed.Items.Length==0,"failed source records an attempt without a fresh successful reply");
        var failedText=ReleaseFreshness.SourceStateText(failedCheck,DateTime.UtcNow);
        Check(failedText=="Временно недоступен · попытка сейчас"&&!failedText.Contains("secret-fixture")&&!failedText.Contains("https://"),"failed source display shows attempt age while omitting raw exception URLs and passkeys");
        Check(ReleaseFreshness.SourceStateText(failedCheck with{LastSuccessUtc=now.AddHours(-3)},now).EndsWith("успешный ответ 3 ч назад"),"failed refresh keeps the earlier successful response distinct from its new attempt");

        var audio=ReleaseFreshness.Audio(cached);
        Check(audio=="Русская · дубляж"&&ReleaseFreshness.Audio(cached with{Title="Film 1080p ENG subs RUS",Source="The Pirate Bay"})=="Оригинал","Russian audio is visible while Russian subtitles alone do not become audio");
        Check(ReleaseFreshness.AudioNote.Contains("по названию")&&ReleaseFreshness.ConnectionNote.Contains("по данным источника"),"audio and participant hints state their evidence limits");

        var movie=new MediaItem(-88037,"Тестовый фильм","Фильмы","",2026,"—","—","#526B69"){PageUrl="https://w6.zona.plus/movies/freshness-fixture"};
        var cacheDirectory=Path.Combine(root,"release-freshness-cache");var index=new CatalogIndex(cacheDirectory);
        var other=fresh with{Id="freshness-new",TorrentUrl="magnet:?xt=urn:btih:"+new string('b',40),Source="NNM-Club",DataProvider="NNM-Club"};
        var merged=ReleaseSearch.WithSaved([other],[cached]);
        await index.CacheReleasesAsync(movie,merged,[new("NNM-Club",SourceState.Ready,1,other.DataReceivedUtc,other.DataReceivedUtc),failedCheck]);
        var restored=new CatalogIndex(cacheDirectory).CachedReleaseSnapshot(movie)!;
        Check(restored.SavedUtc>cached.DataReceivedUtc&&restored.Items.Single(x=>x.Id==cached.Id).DataReceivedUtc==cached.DataReceivedUtc&&restored.Items.Single(x=>x.Id==other.Id).DataProvider=="NNM-Club","partial source success rewrites cache without refreshing retained older rows");
        await index.CacheReleasesAsync(movie,[],failed.Sources);
        Check(new CatalogIndex(cacheDirectory).CachedReleaseSnapshot(movie)?.SavedUtc==restored.SavedUtc,"an all-failed request preserves useful cached dates");
        var path=Directory.GetFiles(Path.Combine(cacheDirectory,"release-index"),"*.json").Single();
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(restored with{SavedUtc=now.AddDays(-8)}));
        Check(new CatalogIndex(cacheDirectory).CachedReleaseSnapshot(movie)==null,"expired cache evidence is rejected instead of looking current");
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(restored with{Items=[cached with{DataReceivedUtc=null,DataProvider=null}]}));
        var legacy=new CatalogIndex(cacheDirectory).CachedReleaseSnapshot(movie)!.Items.Single();
        Check(ReleaseFreshness.Caption(legacy,now).EndsWith("время неизвестно"),"a rewritten legacy cache envelope does not invent a per-row receipt date");
    }
}

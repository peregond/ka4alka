using Kachalka;
using System.Text.Json;

static class UXReliabilityTests
{
    public static async Task Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var now=DateTime.UtcNow;var index=new CatalogIndex(Path.Combine(root,"availability-index"));
        var movie=new MediaItem(-80001,"Новый фильм","Фильмы","",2026,"—","—","#526B69"){PageUrl="https://w6.zona.plus/movies/availability-test"};
        SourceCheck[] empty=[new("Index",SourceState.Empty,CheckedUtc:now),new("Russian",SourceState.Empty,CheckedUtc:now)];
        await index.CacheReleasesAsync(movie,[],empty);
        var saved=index.CachedReleaseSnapshot(movie);
        Check(ReleaseAvailability.Hidden(saved,now),"successfully checked film without any releases is hidden from the feed");
        Check(ReleaseAvailability.Hidden(new CatalogIndex(Path.Combine(root,"availability-index")).CachedReleaseSnapshot(movie),now),"empty-release evidence survives restart");
        Check(!ReleaseAvailability.Hidden(saved,now.AddDays(1).AddSeconds(1)),"a hidden film becomes eligible for a fresh check after one day");
        Check(!ReleaseAvailability.Hidden(saved! with{SavedUtc=now.AddDays(3)},now),"future cache timestamps cannot hide catalog cards");
        foreach(var state in new[]{SourceState.Unavailable,SourceState.TimedOut,SourceState.Searching,SourceState.Saved})
            Check(!ReleaseAvailability.ConfirmedEmpty([],empty.Append(new("Offline",state)).ToArray()),"a "+state+" provider is not mistaken for an absent release");
        var release=new SourceEntry("fixture","Новый фильм (2026) 1080p RUS","RuTor","","magnet:?xt=urn:btih:"+new string('a',40),null,Seeds:0);
        await index.CacheReleasesAsync(movie,[release]);
        Check(!ReleaseAvailability.Hidden(index.CachedReleaseSnapshot(movie),now),"a found release restores the film even when indexed seed count is zero");
        var prior=index.CachedReleaseSnapshot(movie);
        await index.CacheReleasesAsync(movie,[],[new("Offline",SourceState.Unavailable)]);
        Check(index.CachedReleaseSnapshot(movie)?.SavedUtc==prior!.SavedUtc,"network failure preserves known download options and their timestamp");
        var english=release with{Id="english",Source="The Pirate Bay",Title="Movie (2026) ENG 1080p",Seeds=900};
        var russian=release with{Id="russian",Seeds=10};
        var subtitleOnly=english with{Id="subs",Title="Movie (2026) ENG 1080p Subs: RUS"};
        var unknown=english with{Id="unknown",Title="Movie (2026) 1080p",Seeds=null};
        var dead=russian with{Id="dead",Seeds=0};
        Check(RussianAudio.Order([english,dead,unknown,russian,subtitleOnly]).Select(x=>x.Id).SequenceEqual(["russian","unknown","english","subs","dead"]),"default ordering prefers evidenced Russian audio while retaining alternatives and deprioritizing known zero-seed options");
        Check(RussianAudio.Rank(english with{Title="Movie (2026) DUB 1080p"})<3,"foreign dubbing without Russian language evidence is not labeled Russian");
        Check(RussianAudio.Rank(release with{Title="Фильм (2026) MVO 1080p"})==3,"Russian-source voice markers are preferred over an unknown language");
        var secret="private-value";
        var paths=new[]{@"C:\Users\Person Name",@"D:\Кино и сериалы\Ka4alka"};
        var raw=string.Join("\n",paths)+"\n"+JsonSerializer.Serialize(new{Path=paths[1],api_key=secret})+"\nhttps://u:"+secret+"@example.test/announce?passkey="+secret+"&token="+secret+"\nAuthorization: Bearer "+secret;
        var cleaned=DiagnosticReport.Redact(raw,paths);
        Check(!cleaned.Contains(secret)&&!cleaned.Contains("Person Name")&&!cleaned.Contains("Кино и сериалы")&&!cleaned.Contains("\\u041A"),"copied logs redact plaintext and JSON paths, credentials, passkeys and bearer tokens");
        var old=Environment.GetEnvironmentVariable("KACHALKA_DATA");Environment.SetEnvironmentVariable("KACHALKA_DATA",Path.Combine(root,"report-state"));
        try
        {
            DiagnosticLog.Write("fixture",new{State="Metadata",Connections=0});ErrorLog.Write(new Exception("diagnostic fixture"));
            var service=new DownloadService();var report=DiagnosticReport.Create(new Preferences(),service);
            Check(report.Contains("Version")&&report.Contains("Metadata")&&report.Contains("diagnostic fixture")&&!service.EngineCreated,"collecting diagnostics includes errors and network state without starting the torrent engine");
            await File.WriteAllTextAsync(Path.Combine(Preferences.DataDir,"error.log"),new string('x',1024*1024));
            Check(DiagnosticReport.Create(new Preferences(),service).Length<250_000,"copied report remains bounded when existing logs are large");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",old);}
        foreach(var scale in new[]{1d,1.2,1.25,1.5,1.75,2,2.5})
        {
            var fit=WindowSizing.FitPixels(1366,728,scale,scale);
            Check(fit.Width*scale<=1366&&fit.Height*scale<=728&&fit.MinWidth<=fit.Width&&fit.MinHeight<=fit.Height,"window fits physical work area at Windows scale "+scale*100+"%");
        }
    }
}

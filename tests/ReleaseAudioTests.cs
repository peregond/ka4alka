using Kachalka;

static class ReleaseAudioTests
{
    public static void Run()
    {
        SourceEntry Entry(string title)=>new("audio",title,"RuTor","https://example.invalid",null,null);
        void Check(bool pass,string message){if(!pass)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var multi=Entry("Сериал S01-S03 1080p MVO, DVO [LostFilm / NewStudio / Кубик в Кубе]");
        Check(ReleaseAudio.StudioNames(multi).SequenceEqual(new[]{"LostFilm","NewStudio","Кубик в Кубе"}),"multiple studios are retained for a series release");
        Check(ReleaseAudio.MatchesStudio(multi,"NewStudio")&&ReleaseAudio.MatchesType(multi,"Двухголосая")&&ReleaseAudio.MatchesType(multi,"Многоголосая"),"filters match individual studios and audio tracks, not the combined label");
        Check(!ReleaseAudio.MatchesStudio(multi,"AlexFilm")&&!ReleaseAudio.MatchesType(multi,"Дубляж"),"unadvertised studio and audio type are excluded");
        Check(ReleaseAudio.StudioNames(Entry("Film [Лост-Фильм, HDrezka Studio, RuDub, TVShows]")).SequenceEqual(new[]{"LostFilm","HDrezka","RuDub","TVShows"}),"studio aliases are canonicalized");
        Check(ReleaseAudio.TypeNames(Entry("Фильм DUB + AVO + ENG")).SequenceEqual(new[]{"Дубляж","Одноголосая","Оригинал"}),"film dubbing, one voice and original tracks can coexist");
        Check(ReleaseAudio.TypeNames(Entry("Фильм двухголосый перевод")).Single()=="Двухголосая","Russian audio type descriptions are recognized");
        var unknown=Entry("Сериал 1080p [LostFilm]");
        Check(ReleaseAudio.MatchesType(unknown,"Не указано")&&!ReleaseAudio.MatchesType(unknown,"Многоголосая"),"a known studio does not invent an audio type");
        Check(ReleaseAudio.TypeNames(Entry("Film [AniDUB] dubbedSubmarine ENG subs RUS")).SequenceEqual(new[]{"Оригинал"}),"type tokens do not match substrings inside studio names or words");
        Check(ReleaseAudio.StudioNames(Entry("LostFilmography NewStudioX")).Count==0,"studio matching respects word boundaries");
        Check(ReleaseAudio.MatchesStudio(Entry("Фильм 2026"),"Не указано")&&ReleaseAudio.MatchesStudio(multi,"Все"),"unknown and all filters behave explicitly");
        Check(ReleaseAudio.StudioNames(Entry("Сериал subs [LostFilm]")).Count==0&&ReleaseAudio.TypeNames(Entry("Фильм subs ENG")).Count==0,"subtitle metadata does not identify the audio track");
        foreach(var name in new[]{"Flarrow Films","FLARROW.FILMS","Flarrow-Films","Фларроу Филмс"})
            Check(ReleaseAudio.StudioNames(Entry("Фильм DUB ["+name+"]")).SequenceEqual(new[]{"Flarrow Films"}),"Flarrow Films studio alias is recognized: "+name);
        var dubbing=Entry("Закулисье реальности / Backrooms (2026) WEB-DL 1080p DUB [Flarrow Films]") with{Id="dub",Seeds=80,TorrentUrl="magnet:?xt=urn:btih:"+new string('a',40)};
        var voiceover=dubbing with{Id="mvo",Title="Закулисье реальности / Backrooms (2026) WEB-DL 1080p MVO [HDrezka Studio]",Seeds=600};
        Check(ReleaseAudio.MatchesStudio(dubbing,"Flarrow Films")&&ReleaseAudio.MatchesType(dubbing,"Дубляж"),"studio and dubbing filters find the advertised Flarrow Films track");
        Check(ReleaseRecommendation.Pick([voiceover,dubbing])?.Entry==dubbing,"an available Russian dub is recommended ahead of a voice-over with more seeds");
        Check(ReleaseRecommendation.Pick([voiceover,dubbing])!.Reason.Contains("дубляж"),"the recommendation explains the advertised dubbing preference");
        foreach(var label in new[]{"дубляж","дублированный","дублированное","DUB"})
        {
            var variant=dubbing with{Title="Backrooms 2026 1080p "+label};
            Check(ReleaseRecommendation.Pick([voiceover,variant])?.Entry==variant,"explicit dubbing labels receive priority: "+label);
        }
        var otherDub=voiceover with{Title="Backrooms 2026 1080p DUB [HDrezka Studio]"};
        Check(ReleaseRecommendation.Pick([dubbing,otherDub])?.Entry==otherDub,"equivalent dubs are ranked by seeders rather than a studio whitelist");
        var savedLanguage=AppLanguage.Current;
        try
        {
            AppLanguage.Set("en");
            Check(AppLanguage.T(ReleaseRecommendation.Pick([voiceover,dubbing])!.Reason).StartsWith("Russian dubbing"),"the dubbing recommendation explanation has an English translation");
        }
        finally{AppLanguage.Set(savedLanguage);}
        var unknownDub=dubbing with{Id="unknown",Title="Backrooms (2026) WEB-DL 1080p [Flarrow Films]"};
        Check(!RussianAudio.DubbedAudio(unknownDub)&&ReleaseAudio.TypeNames(unknownDub).Count==0,"the Flarrow Films name alone does not invent dubbing");
        var multiDub=dubbing with{Id="multi",Title="Backrooms 2026 1080p DUB [Flarrow Films] + MVO [HDrezka Studio]"};
        Check(ReleaseAudio.StudioNames(multiDub).Count==2&&ReleaseAudio.TypeNames(multiDub).Count==2,"a release advertising both tracks retains both studios and types");
        Check(ReleaseRecommendation.Pick([voiceover,dubbing with{Seeds=19}])?.Entry==voiceover,"dubbing priority cannot bypass minimum seeder availability");
        Check(ReleaseRecommendation.Pick([voiceover,dubbing with{TorrentUrl=null}])?.Entry==voiceover,"dubbing priority cannot recommend a metadata-only release");
        Check(ReleaseRecommendation.Pick([voiceover,dubbing with{Title=dubbing.Title+" HDCAM"}])?.Entry==voiceover,"dubbing priority cannot recommend a screen recording");
        var english=dubbing with{Id="english",Source="Test",Title="Backrooms 2026 1080p ENG",Seeds=30};
        Check(ReleaseRecommendation.Pick([dubbing,english],preferredLanguage:"en")?.Entry==english,"English interface keeps English audio ahead of Russian dubbing");
        Check(!RussianAudio.DubbedAudio(dubbing with{Title="Backrooms 1080p subs [DUB Flarrow Films]"}),"dubbing in subtitle metadata cannot change audio priority");
        Check(RussianAudio.Order([dubbing with{Seeds=0},voiceover]).First()==voiceover,"a dead dubbing swarm remains behind a live voice-over");
        var clone=multi with{Title="Фильм [AlexFilm] DUB"};
        Check(ReleaseAudio.MatchesStudio(clone,"AlexFilm")&&!ReleaseAudio.MatchesStudio(clone,"LostFilm"),"cached labels do not leak from a cloned release with a new title");
        Check(ReleaseAudio.StudioNames(Entry("Сериал LostFilm / lost-film")).Count==1,"duplicate studio aliases are collapsed");
    }
}

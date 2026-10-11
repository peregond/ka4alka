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
        var clone=multi with{Title="Фильм [AlexFilm] DUB"};
        Check(ReleaseAudio.MatchesStudio(clone,"AlexFilm")&&!ReleaseAudio.MatchesStudio(clone,"LostFilm"),"cached labels do not leak from a cloned release with a new title");
        Check(ReleaseAudio.StudioNames(Entry("Сериал LostFilm / lost-film")).Count==1,"duplicate studio aliases are collapsed");
    }
}

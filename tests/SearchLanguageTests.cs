using Kachalka;
static class SearchLanguageTests
{
    public static void Run()
    {
        void Check(bool valid,string message){if(!valid)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        SourceEntry Row(string id,string title,string source="The Pirate Bay",int seeds=200)=>new(id,title,source,"https://example.test/"+id,"magnet:?xt=urn:btih:"+new string('a',40),null,Seeds:seeds);
        var english=Row("en","Dune (2021) 1080p ENG",seeds:50);
        var russian=Row("ru","Дюна (2021) 1080p RUS","RuTor",900);
        var unknown=Row("unknown","Dune (2021) 1080p",seeds:500);
        var japanese=Row("jp","Dune (2021) 1080p JPN",seeds:1000);
        var dead=english with{Id="dead",Seeds=0};
        var rows=new[]{russian,unknown,japanese,dead,english};
        Check(SearchLanguage.AudioOrder(rows,"en").Select(x=>x.Id).SequenceEqual(["en","unknown","jp","ru","dead"]),"English preference keeps healthy English audio first and retains every other language with dead swarms last");
        Check(SearchLanguage.AudioOrder(rows,"ru").SequenceEqual(RussianAudio.Order(rows)),"Russian mode preserves its established audio ordering");
        foreach(var title in new[]{"Dune 1080p RUS Subs: ENG","Dune 1080p RUS [Subtitles: English]","Dune 1080p JPN ENG.SUBS","Dune 1080p Japanese English subtitles"})
            Check(SearchLanguage.EnglishAudioRank(Row("subs",title))==0,"English subtitles do not imply English audio: "+title);
        Check(SearchLanguage.EnglishAudioRank(Row("multi","Dune 1080p RUS ENG"))==3,"dual-audio releases qualify when an English audio track is advertised");
        Check(SearchLanguage.EnglishAudioRank(Row("original","Dune 1080p original JPN"))==0,"original Japanese audio is never assumed to be English");
        Check(ReleaseRecommendation.Pick(rows,preferredLanguage:"en")?.Entry==english,"the primary download recommendation follows English audio preference");
        Check(ReleaseRecommendation.Pick(rows,preferredLanguage:"ru")?.Entry==russian,"the primary download recommendation retains Russian priority in Russian mode");
        Check(ReleaseRecommendation.Pick([russian],preferredLanguage:"en")?.Entry==russian,"a Russian fallback remains downloadable when no English release exists");
        Check(ReleaseRecommendation.Pick([dead],preferredLanguage:"en")==null,"language preference cannot bypass minimum seeder safeguards");
        Check(ReleaseRecommendation.Pick(rows,preferredLanguage:"en")!.Reason.Contains("английская озвучка"),"recommendations describe evidenced English audio only");
    }
}

using System.Text;
using Kachalka;

static class BackroomsAudioTests
{
    public static void Run()
    {
        void Check(bool pass,string message){if(!pass)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var item=new MediaItem(-1,"Закулисье реальности","Фильмы","",2026,"—","—","#123456"){OriginalTitle="Backrooms"};
        var path=Path.Combine(AppContext.BaseDirectory,"Fixtures","rutor-backrooms-2026.html");
        var rows=LiveCatalog.ParseRutor(File.ReadAllBytes(path)).Where(row=>LiveCatalog.Matches(item,row)).ToArray();
        Check(rows.Length==16,"the captured live Backrooms search retains every real film release");
        var flarrow=rows.Where(row=>ReleaseAudio.MatchesStudio(row,"Flarrow Films")).ToArray();
        Check(flarrow.Length>=5&&flarrow.All(RussianAudio.DubbedAudio),"real Flarrow Films rows labelled D are recognized as dubbed releases");
        var fullHd=flarrow.Single(row=>row.PageUrl.Contains("/1104372/"));
        Check(fullHd.Quality=="Full HD"&&fullHd.Seeds==5&&fullHd.Size>0&&ReleaseQuality.Downloadable(fullHd),"real BDRemux release preserves its actual Full HD, size, five seeders and magnet");
        Check(ReleaseAudio.StudioNames(fullHd).Contains("HDrezka")&&ReleaseAudio.TypeNames(fullHd).Contains("Дубляж"),"mixed-studio rows preserve Flarrow Films and HDrezka without dropping the D audio marker");
        var sparse=flarrow.Where(row=>row.Quality is "4K" or "Full HD").ToArray();
        Check(sparse.Length>0&&sparse.All(row=>row.Seeds<ReleaseRecommendation.MinimumSeeds),"the real Flarrow HD fixture is not falsely reported as a well-seeded recommendation");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var megaHtml=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","megapeer-backrooms-2026.html"));
        var megaRows=MegaPeerSource.Parse(Encoding.GetEncoding(1251).GetBytes(megaHtml),item);
        Check(megaRows.Count==8&&megaRows.Count(row=>ReleaseAudio.MatchesStudio(row,"Flarrow Films"))==4,"the real MegaPeer search also exposes the four Flarrow alternatives");
        var commented=megaRows.Single(row=>row.Id=="MegaPeer:209947");
        Check(commented.Size>6_000_000_000&&commented.Seeds==13,"comment-count columns cannot hide the actual release size");
        var table=SearchLanguage.AudioOrder(rows,"ru").ToArray();
        Check(table.Length==rows.Length&&flarrow.All(table.Contains),"Russian sorting preserves sparse Flarrow alternatives for manual selection");
    }
}

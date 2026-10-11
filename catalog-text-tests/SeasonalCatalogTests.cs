using System.Text.Json;
using Kachalka;

static class SeasonalCatalogTests
{
    public static void Run()
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        SeasonalCollection[] Parse(string? start,string? end)
        {
            using var json=JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            {
                collections=new[]{new{id="festival-2026",title="Фестиваль",mediaIds=new[]{"movies:festival-film"},startsAtUtc=start,endsAtUtc=end}}
            }));
            return SeasonalCatalog.Parse(json.RootElement);
        }
        var utc=Parse("2026-10-11T05:00:00Z","2026-10-18T05:00:00Z").Single();
        var offset=Parse("2026-10-11T05:00:00+00:00","2026-10-18T05:00:00+00:00").Single();
        Check(utc.StartsAtUtc==offset.StartsAtUtc&&utc.EndsAtUtc==offset.EndsAtUtc&&offset.StartsAtUtc?.Kind==DateTimeKind.Utc&&offset.EndsAtUtc?.Kind==DateTimeKind.Utc,"seasonal Z and explicit +00:00 timestamps normalize to the same UTC window");
        var start=new DateTime(2026,10,11,5,0,0,DateTimeKind.Utc);var end=start.AddDays(7);
        Check(!offset.Active(start.AddTicks(-1))&&offset.Active(start)&&offset.Active(end.AddTicks(-1))&&!offset.Active(end),"seasonal UTC windows include the start and exclude the end");
        foreach(var ambiguous in new[]{"2026-10-11T05:00:00","2026-10-11","2026-10-11T05:00:00+03:00","2026-10-11T05:00:00-03:00","not-a-date"})
        {
            Check(Parse(ambiguous,"2026-10-18T05:00:00Z").Length==0&&Parse("2026-10-11T05:00:00Z",ambiguous).Length==0,"seasonal windows reject ambiguous, non-UTC, or invalid timestamps: "+ambiguous);
        }
        Check(Parse("2026-10-18T05:00:00+00:00","2026-10-18T05:00:00Z").Length==0&&Parse("2026-10-19T05:00:00Z","2026-10-18T05:00:00+00:00").Length==0,"equal and reversed seasonal windows remain invalid across UTC representations");
        Check(Parse(null,null).Single().Active(start),"undated seasonal packs remain available");
    }
}

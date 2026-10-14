using System.Text.Json;
using Kachalka;

static class GameReleaseProbe
{
    public static async Task Run(string root)
    {
        using var client=new SourceClient();
        var results=new List<object>();
        foreach(var game in new[]{GameCatalog.Item(620,"Portal 2™"),GameCatalog.Item(1245620,"ELDEN RING"),GameCatalog.Item(1086940,"Baldur’s Gate 3"),GameCatalog.Item(292030,"The Witcher® 3: Wild Hunt")})
        {
            var result=await ReleaseSearch.RunAsync(GameReleaseSources.Create(client,game,[]),timeout:TimeSpan.FromSeconds(18));
            var downloadable=result.Items.Where(ReleaseQuality.Downloadable).ToArray();
            Console.WriteLine(game.Title+": "+downloadable.Length+" downloadable rows; "+string.Join(", ",result.Sources.Select(x=>x.Name+"="+x.State+"/"+x.Count)));
            results.Add(new{Game=game.Title,Count=downloadable.Length,Sources=result.Sources,Rows=downloadable.Take(3).Select(x=>new{x.Title,x.Source,x.Seeds})});
            var output=Path.Combine(root,"game-releases");Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            if(downloadable.Length==0)throw new Exception("No downloadable release found for "+game.Title+"; see source states.");
            if(downloadable.Any(x=>!GameReleaseSources.Matches(game,x)))throw new Exception("Another game or platform entered the release list.");
        }
    }
}

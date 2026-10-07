using System.Text.Json;
using Kachalka;

public static class CinemaSourceTests
{
    public static async Task Run()
    {
        using var client=new SourceClient();using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var catalog=new LiveCatalog(client);var output=Path.GetFullPath("test-output/cinema-source");Directory.CreateDirectory(output);
        var films=await catalog.Browse("Фильмы","Терминатор",1,deadline.Token);
        foreach(var film in films.Take(3))
        {
            var bytes=await client.Read(new Uri(film.PageUrl!),4*1024*1024,deadline.Token);
            await File.WriteAllBytesAsync(Path.Combine(output,"film-"+film.Id+".html"),bytes,deadline.Token);
            var people=CinemaMetadata.People(bytes);Console.WriteLine(film.Title+": "+people.Length+" participants");
            foreach(var person in people.Take(2))
            {
                var personBytes=await client.Read(new Uri(person.PageUrl),4*1024*1024,deadline.Token);
                await File.WriteAllBytesAsync(Path.Combine(output,"person.html"),personBytes,deadline.Token);
                var profile=CinemaMetadata.Person(personBytes,person);
                if(profile.Filmography.Length==0)continue;
                await File.WriteAllTextAsync(Path.Combine(output,"verified.json"),JsonSerializer.Serialize(new{Film=film.Title,People=people,Person=profile.Person,Works=profile.Filmography.Length,Top=CinemaMetadata.Top(profile.Filmography).Length}),deadline.Token);
                Console.WriteLine("PASS: live film credits and person filmography");return;
            }
        }
        throw new InvalidOperationException("Live source did not provide working film → person → film navigation; see captured source HTML.");
    }
}

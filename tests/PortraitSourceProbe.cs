using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Kachalka;

// This probe uses real Wikipedia biographies and Wikimedia image bytes. The
// regular suite separately checks lookup, cancellation and caching with mocks.
public static class PortraitSourceProbe
{
    public static async Task Run(string root)
    {
        using var client=new SourceClient();
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var people=new CinemaPeople(client);
        var output=Path.Combine(root,"portrait-source");Directory.CreateDirectory(output);
        var cases=new[]{
            (Person:new CinemaPerson("Джеймс Кэмерон","Режиссёры",""),Film:"Аватар",Year:2009,Original:"Avatar"),
            (Person:new CinemaPerson("Том Холланд","Актёры",""),Film:"Человек-паук: Нет пути домой",Year:2021,Original:"Spider-Man: No Way Home"),
            (Person:new CinemaPerson("Ребекка Фергюсон","Актёры",""),Film:"Укрытие",Year:2023,Original:"Silo"),
            (Person:new CinemaPerson("Иван Янковский","Актёры",""),Film:"Временные трудности",Year:2018,Original:"")
        };
        var evidence=new List<object>();
        var failures=new List<Exception>();
        var caseIndex=0;
        foreach(var example in cases)
        {
            PortraitResult? portrait=null;
            try
            {
            if(caseIndex++>0)await Task.Delay(TimeSpan.FromMilliseconds(1250),deadline.Token);
            var origin=new MediaItem(-example.Year,example.Film,example.Original=="Silo"?"Сериалы":"Фильмы","",example.Year,"—","—","#526B69"){
                OriginalTitle=example.Original,People=[example.Person]
            };
            portrait=await people.ResolvePortrait(example.Person,deadline.Token,origin);
            Console.WriteLine($"PORTRAIT person={example.Person.Name}; status={portrait.Status}; source={portrait.SourceUrl}; cached={portrait.FromCache}");
            if(portrait.Status!=PortraitStatus.Available||CinemaPeople.PhotoUrl(portrait.Url)==null||string.IsNullOrWhiteSpace(portrait.SourceUrl)||portrait.FromCache)
                throw new InvalidOperationException("A fresh, confirmed photograph was not resolved for "+example.Person.Name+".");
            using var download=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);download.CancelAfter(TimeSpan.FromSeconds(15));
            var image=await client.Read(new Uri(portrait.Url!),2*1024*1024,download.Token);
            if(image.Length<512||!IsImage(image))throw new InvalidDataException("Wikimedia did not return image bytes for "+example.Person.Name+".");
            var dimensions=await DecodePortrait(image,deadline.Token);
            var cached=await people.ResolvePortrait(example.Person,deadline.Token,origin);
            if(cached.Status!=PortraitStatus.Available||!cached.FromCache||cached.Url!=portrait.Url)
                throw new InvalidOperationException("Resolved photograph metadata was not retained for "+example.Person.Name+".");
            evidence.Add(new{Person=example.Person.Name,Role=example.Person.Role,portrait.SourceUrl,portrait.Url,Bytes=image.Length,dimensions.Width,dimensions.Height,Sha256=Convert.ToHexString(SHA256.HashData(image))});
            Console.WriteLine($"PASS: {example.Person.Name}: confirmed portrait downloads ({image.Length} bytes), decodes {dimensions.Width}×{dimensions.Height} and remains cached");
            }
            catch(Exception error)
            {
                var message=example.Person.Name+": "+error.Message;
                Console.WriteLine("PORTRAIT FAILURE "+message);
                failures.Add(new InvalidOperationException(message,error));
                evidence.Add(new{Person=example.Person.Name,Status=portrait?.Status.ToString(),SourceUrl=portrait?.SourceUrl,Url=portrait?.Url,Error=error.ToString()});
                await Diagnose(client,example.Person,portrait?.SourceUrl,deadline.Token);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output,"verified.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        if(failures.Count>0)throw new AggregateException("Live portrait verification failed.",failures);
    }

    static async Task Diagnose(SourceClient client,CinemaPerson person,string? sourceUrl,CancellationToken ct)
    {
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var title=sourceUrl!=null&&Uri.TryCreate(sourceUrl,UriKind.Absolute,out var page)?Uri.UnescapeDataString(page.AbsolutePath["/wiki/".Length..]).Replace('_',' '):person.Name;
            var api="https://ru.wikipedia.org/w/api.php?action=query&format=json&redirects=1&prop=pageprops%7Cpageimages&ppprop=wikibase_item&piprop=thumbnail&pithumbsize=400&titles="+Uri.EscapeDataString(title);
            var bytes=await client.Read(new Uri(api),1024*1024,timeout.Token);var json=Encoding.UTF8.GetString(bytes);
            Console.WriteLine("PORTRAIT DIAGNOSTIC Wikipedia "+title+": "+json[..Math.Min(json.Length,4096)]);
            using var document=JsonDocument.Parse(bytes);
            if(!document.RootElement.TryGetProperty("query",out var query)||!query.TryGetProperty("pages",out var pages))return;
            foreach(var candidate in pages.EnumerateObject().Select(x=>x.Value))
            {
                if(!candidate.TryGetProperty("pageprops",out var properties)||properties.ValueKind!=JsonValueKind.Object||!properties.TryGetProperty("wikibase_item",out var item))continue;
                var id=item.GetString();if(id==null||!Regex.IsMatch(id,@"^Q[1-9]\d*$"))continue;
                var data="https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&ids="+id;
                using var entity=JsonDocument.Parse(await client.Read(new Uri(data),1024*1024,timeout.Token));
                if(entity.RootElement.TryGetProperty("entities",out var entities)&&entities.TryGetProperty(id,out var value)&&value.TryGetProperty("claims",out var claims))
                {
                    Console.WriteLine("PORTRAIT DIAGNOSTIC Wikidata "+id+" P18: "+(claims.TryGetProperty("P18",out var images)?images.GetRawText()[..Math.Min(images.GetRawText().Length,4096)]:"missing"));
                    if(images.ValueKind!=JsonValueKind.Array)continue;
                    var image=images.EnumerateArray().Select(x=>x.TryGetProperty("mainsnak",out var snak)&&snak.TryGetProperty("datavalue",out var dataValue)&&dataValue.TryGetProperty("value",out var file)&&file.ValueKind==JsonValueKind.String?file.GetString():null).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
                    if(image==null)continue;
                    var commons="https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url&iiurlwidth=400&titles="+Uri.EscapeDataString("File:"+image);
                    var media=Encoding.UTF8.GetString(await client.Read(new Uri(commons),1024*1024,timeout.Token));
                    Console.WriteLine("PORTRAIT DIAGNOSTIC Commons "+image+": "+media[..Math.Min(media.Length,4096)]);
                }
                else Console.WriteLine("PORTRAIT DIAGNOSTIC Wikidata "+id+": "+entity.RootElement.GetRawText()[..Math.Min(entity.RootElement.GetRawText().Length,4096)]);
            }
        }
        catch(Exception error){Console.WriteLine("PORTRAIT DIAGNOSTIC unavailable: "+error.Message);}
    }

    static async Task<(int Width,int Height)> DecodePortrait(byte[] bytes,CancellationToken ct)
    {
        var result=new TaskCompletionSource<(int Width,int Height)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder=new Thread(()=>
        {
            try
            {
                using var stream=new MemoryStream(bytes);
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth=192;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
                if(bitmap.PixelWidth<1||bitmap.PixelHeight<1)throw new InvalidDataException("The photograph decoded without visible pixels.");
                result.SetResult((bitmap.PixelWidth,bitmap.PixelHeight));
            }
            catch(Exception error){result.SetException(error);}
        }){IsBackground=true};
        decoder.SetApartmentState(ApartmentState.STA);decoder.Start();
        return await result.Task.WaitAsync(ct);
    }

    static bool IsImage(byte[] bytes)=>
        bytes.AsSpan().StartsWith(new byte[]{0xff,0xd8,0xff})||
        bytes.AsSpan().StartsWith(new byte[]{0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a})||
        bytes.AsSpan().StartsWith("GIF8"u8)||
        bytes.Length>=12&&bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)&&bytes.AsSpan(8,4).SequenceEqual("WEBP"u8);
}

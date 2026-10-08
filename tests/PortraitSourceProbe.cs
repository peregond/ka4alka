using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Kachalka;

// This probe uses real Wikipedia biographies and Wikimedia image bytes. The
// regular suite separately checks lookup, cancellation and caching with mocks.
public static class PortraitSourceProbe
{
    public static async Task Run(string root)
    {
        using var client=new SourceClient();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var people=new CinemaPeople(client);
        var output=Path.Combine(root,"portrait-source");Directory.CreateDirectory(output);
        var cases=new[]{
            (Person:new CinemaPerson("Джеймс Кэмерон","Режиссёры",""),Film:"Аватар",Year:2009,Original:"Avatar"),
            (Person:new CinemaPerson("Том Холланд","Актёры",""),Film:"Человек-паук: Нет пути домой",Year:2021,Original:"Spider-Man: No Way Home"),
            (Person:new CinemaPerson("Ребекка Фергюсон","Актёры",""),Film:"Укрытие",Year:2023,Original:"Silo")
        };
        var evidence=new List<object>();
        foreach(var example in cases)
        {
            var origin=new MediaItem(-example.Year,example.Film,example.Original=="Silo"?"Сериалы":"Фильмы","",example.Year,"—","—","#526B69"){
                OriginalTitle=example.Original,People=[example.Person]
            };
            var portrait=await people.ResolvePortrait(example.Person,deadline.Token,origin);
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
        await File.WriteAllTextAsync(Path.Combine(output,"verified.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}),deadline.Token);
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

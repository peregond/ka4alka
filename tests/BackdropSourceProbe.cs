using System.Text.Json;
using System.Windows.Media.Imaging;
using Kachalka;

static class BackdropSourceProbe
{
    public static async Task Run(string root)
    {
        using var client = new SourceClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var output = Path.Combine(root, "backdrop-source");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        foreach (var id in new[] { "movies:obekt-prestupleniya", "series:truth-and-treason" })
        {
            var bytes = await client.Read(new Uri(OnlineIndexClient.PublishedSite,
                "api/backdrop?id=" + Uri.EscapeDataString(id)), 32768, timeout.Token);
            var url = FeatureBackdrop.FromResponse(bytes, id)
                ?? throw new InvalidDataException("Published site returned no backdrop for " + id);
            var file = new Uri(url).AbsolutePath["/t/p/w1280".Length..];
            var jpeg = await client.Read(new Uri(OnlineIndexClient.PublishedSite,
                "api/backdrop-image?file=" + Uri.EscapeDataString(file)), 2 * 1024 * 1024, timeout.Token);
            if (jpeg.Length < 3 || jpeg[0] != 0xff || jpeg[1] != 0xd8 || jpeg[2] != 0xff)
                throw new InvalidDataException("Published image is not JPEG: " + id);
            var dimensions = await Task.Run(() =>
            {
                using var stream = new MemoryStream(jpeg);
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                return (frame.PixelWidth, frame.PixelHeight);
            }, timeout.Token);
            if (dimensions.PixelWidth != 1280 || !FeatureBackdrop.Landscape(dimensions.PixelWidth, dimensions.PixelHeight))
                throw new InvalidDataException("Published image is not a wide w1280 backdrop: " + id);
            var name = id.Replace(':', '-');
            await File.WriteAllBytesAsync(Path.Combine(output, name + ".jpg"), jpeg, timeout.Token);
            results.Add(new { Id = id, Source = "TMDB", dimensions.PixelWidth, dimensions.PixelHeight, Bytes = jpeg.Length });
            Console.WriteLine($"PASS: public {id} resolves to a real TMDB JPEG {dimensions.PixelWidth}x{dimensions.PixelHeight} through the application's HTTP client");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(results), timeout.Token);
    }
}

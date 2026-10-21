using System.Text.Json;
using Kachalka.Lan;

static class PhoneBrowserFixture
{
    public static async Task Run()
    {
        var added=false;
        await using var server=new PhoneRemoteServer((method,args,ct)=>Task.FromResult<object>(method switch
        {
            "catalog"=>new{items=new[]{new{id="film",title="Тестовый фильм / Test movie",year=2026,poster=(string?)null}},hasNext=false},
            "releases"=>new{items=new[]{new{id="release",title="Test movie · Full HD · Dub",source="Local test",quality="Full HD",audio="Dub",size=1073741824,seeds=10,downloadable=true}}},
            "add"=>Add(),
            "downloads"=>new{items=added?new[]{new{id="download",title="Test movie",status="Downloading",progress=42}}:[]},
            _=>throw new ArgumentException()
        }),0,true);
        object Add(){added=true;return new{id="download"};}
        server.Start();
        // Test harness pipe only; this line is captured in memory, not included in test logs.
        var state=JsonSerializer.SerializeToElement(server.OpenPairing());
        Console.WriteLine(JsonSerializer.Serialize(new{port=server.Port,code=state.GetProperty("code").GetString()}));
        await Task.Run(Console.ReadLine);
    }
}

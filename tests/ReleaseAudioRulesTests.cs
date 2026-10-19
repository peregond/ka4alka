using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kachalka;

static class ReleaseAudioRulesTests
{
    sealed class Handler(Func<HttpResponseMessage> response):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(response());}
    }
    public static async Task Run()
    {
        void Check(bool pass,string message){if(!pass)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var original=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","release-audio-rules.json"));
        var root=Path.Combine(Path.GetTempPath(),"kachalka-audio-rules-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            ReleaseAudio.ConfigureRules(original);ReleaseAudioTests.Run();BackroomsAudioTests.Run();var start=ReleaseAudio.RulesRevision;
            var entry=new SourceEntry("rule-update","Film 1080p NEWDUB [Remote Studio]","RuTor","","magnet:?xt=urn:btih:"+new string('a',40),null,Seeds:30);
            Check(ReleaseAudio.StudioNames(entry).Count==0&&!RussianAudio.DubbedAudio(entry),"an unknown studio and new audio alias start unrecognized");
            var document=JsonNode.Parse(original)!;document["revision"]=start+1;
            document["studios"]!.AsArray().Add(JsonSerializer.SerializeToNode(new{name="Remote Studio",aliases=new[]{"Remote Studio"},prefixes=Array.Empty<string>(),codes=Array.Empty<string>()}));
            document["types"]![0]!["aliases"]!.AsArray().Add("NEWDUB");
            var updated=JsonSerializer.SerializeToUtf8Bytes(document);
            using var handler=new Handler(()=>new(HttpStatusCode.OK){Content=new ByteArrayContent(updated)});
            using var client=new SourceClient(handler);
            var path=Path.Combine(root,"rules.json");var loader=new ReleaseAudioRulesClient(client,new Uri("https://rules.example.test/data"),path);
            await Task.WhenAll(Enumerable.Range(0,8).Select(_=>loader.Refresh(CancellationToken.None)));
            Check(handler.Calls==1&&ReleaseAudio.RulesRevision==start+1,"concurrent card requests share one remote rules refresh");
            Check(ReleaseAudio.MatchesStudio(entry,"Remote Studio")&&RussianAudio.DubbedAudio(entry),"downloaded rules update the same cached release object without changing the application binary");
            Check(File.Exists(path),"the verified rules are saved for offline use");
            using var offlineHandler=new Handler(()=>new(HttpStatusCode.ServiceUnavailable));using var offline=new SourceClient(offlineHandler);
            await new ReleaseAudioRulesClient(offline,new Uri("https://rules.example.test/data"),path).Refresh(CancellationToken.None);
            Check(offlineHandler.Calls==0&&ReleaseAudio.MatchesStudio(entry,"Remote Studio"),"a fresh persisted rule set works without a server request");
            foreach(var bad in new[]{"[]","{\"schemaVersion\":2}","{\"schemaVersion\":\"1\"}"})
            {
                try{ReleaseAudio.ConfigureRules(System.Text.Encoding.UTF8.GetBytes(bad));throw new Exception("Invalid rules accepted");}
                catch(Exception e) when(e is System.IO.InvalidDataException or JsonException){}
            }
            Check(ReleaseAudio.RulesRevision==start+1&&ReleaseAudio.MatchesStudio(entry,"Remote Studio"),"invalid server data cannot discard the last working rules or cached labels");
            using var downHandler=new Handler(()=>new(HttpStatusCode.ServiceUnavailable));using var downClient=new SourceClient(downHandler);
            await new ReleaseAudioRulesClient(downClient,new Uri("https://rules.example.test/data"),Path.Combine(root,"missing.json")).Refresh(CancellationToken.None);
            Check(ReleaseAudio.RulesRevision==start+1,"an unavailable rules server retains the last working in-memory configuration");
            document["dubbingPriority"]=0;ReleaseAudio.ConfigureRules(JsonSerializer.SerializeToUtf8Bytes(document));
            var voice=entry with{Id="mvo",Title="Film 1080p MVO",Seeds=600};
            Check(RussianAudio.Order([entry,voice]).First()==voice,"a server policy update changes ranking without replacing executable components");
            document["studios"]!.AsArray()[document["studios"]!.AsArray().Count-1]!["aliases"]!.AsArray().Add(".*");ReleaseAudio.ConfigureRules(JsonSerializer.SerializeToUtf8Bytes(document));
            Check(!ReleaseAudio.MatchesStudio(entry with{Title="An unrelated film 1080p"},"Remote Studio"),"server aliases are literal text and cannot inject executable regular expressions");
        }
        finally{ReleaseAudio.ConfigureRules(original);Directory.Delete(root,true);}
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Kachalka.Lan;

static class PhoneRemoteTests
{
    public static async Task Run()
    {
        var time=new Clock();int calls=0;
        await using var server=new PhoneRemoteServer((method,args,ct)=>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<object>(new{method,items=Array.Empty<object>()});
        },0,true,time);
        server.Start();
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{server.Port}"),Timeout=TimeSpan.FromSeconds(5)};
        int checks=0;
        void Check(bool ok,string description){if(!ok)throw new Exception(description);checks++;Console.WriteLine("PASS: phone "+description);}
        async Task<HttpResponseMessage> Post(string path,object data,string? token=null,string? origin=null)
        {
            var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
            // The wire protocol deliberately accepts one unambiguous content type.
            request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
            if(token!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            if(origin!=null)request.Headers.Add("Origin",origin);
            return await client.SendAsync(request);
        }
        string Code()=>JsonSerializer.SerializeToElement(server.OpenPairing()).GetProperty("code").GetString()!;
        async Task<string> Pair(string code)
        {
            using var response=await Post("/pair",new{code});Check(response.IsSuccessStatusCode,"one-time code pairs browser");
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty("token").GetString()!;
        }
        using(var page=await client.GetAsync("/"))
        {
            var text=await page.Content.ReadAsStringAsync();Check(page.IsSuccessStatusCode&&text.Contains("viewport")&&text.Contains("pair-form"),"responsive local page is served");
            Check(page.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'none'"),"browser page blocks framing and injected scripts");
            Check(page.Headers.CacheControl?.NoStore==true,"pairing and responses are not cached");
        }
        foreach(var path in new[]{"/app.js","/style.css"})using(var response=await client.GetAsync(path))Check(response.IsSuccessStatusCode,"bundled browser asset "+path);
        using(var denied=await Post("/api/add",new{source="magnet:?xt=urn:btih:test"}))Check(denied.StatusCode==HttpStatusCode.Unauthorized&&calls==0,"unauthenticated download never reaches desktop");
        using(var denied=await Post("/api/control",new{id="task",action="pause"}))Check(denied.StatusCode==HttpStatusCode.Unauthorized&&calls==0,"unauthenticated control never reaches desktop");
        var firstCode=Code();
        using(var cross=await Post("/pair",new{code=firstCode},origin:"https://evil.example"))Check(cross.StatusCode==HttpStatusCode.Forbidden,"cross-origin pairing rejected");
        var token=await Pair(firstCode);
        using(var reused=await Post("/pair",new{code=firstCode}))Check(reused.StatusCode==HttpStatusCode.Unauthorized,"pairing code cannot be reused");
        using(var cross=await Post("/api/add",new{},token,"null"))Check(cross.StatusCode==HttpStatusCode.Forbidden&&calls==0,"opaque origin cannot issue commands");
        foreach(var method in new[]{"catalog","releases","downloads","add","control"})
            using(var response=await Post("/api/"+method,new{},token))Check(response.IsSuccessStatusCode,"paired browser dispatches "+method);
        using(var denied=await Post("/api/setSettings",new{},token))Check(denied.StatusCode==HttpStatusCode.NotFound&&calls==5,"desktop settings API is not remotely exposed");
        using(var denied=await client.PostAsync("/api/add",new FormUrlEncodedContent(new[]{KeyValuePair.Create("source","test")})))Check(denied.StatusCode==HttpStatusCode.BadRequest,"HTML form cannot submit a download");
        using(var denied=await Post("/api/add",new{data=new string('x',65536)},token))Check(denied.StatusCode==HttpStatusCode.BadRequest,"oversized body rejected before dispatch");
        using(var request=new HttpRequestMessage(HttpMethod.Get,"/"))
        {request.Headers.Host="evil.example";using var denied=await client.SendAsync(request);Check(denied.StatusCode==HttpStatusCode.Forbidden,"DNS rebinding host rejected");}
        var expiredCode=Code();time.Advance(TimeSpan.FromMinutes(3));
        using(var denied=await Post("/pair",new{code=expiredCode}))Check(denied.StatusCode==HttpStatusCode.Unauthorized,"pairing expires in two minutes");
        var limitedCode=Code();
        for(var i=0;i<5;i++)using(await Post("/pair",new{code="wrong"})){}
        using(var denied=await Post("/pair",new{code=limitedCode}))Check(denied.StatusCode==HttpStatusCode.Unauthorized,"pairing brute-force attempts are limited");
        time.Advance(TimeSpan.FromHours(9));
        using(var denied=await Post("/api/downloads",new{},token))Check(denied.StatusCode==HttpStatusCode.Unauthorized,"browser sessions expire after eight hours");
        token=await Pair(Code());server.Revoke();
        using(var denied=await Post("/api/add",new{},token))Check(denied.StatusCode==HttpStatusCode.Unauthorized,"revoke immediately invalidates sessions");
        Check(server.Connected==0,"revocation clears all paired phones");
        // A raw duplicate header must be rejected rather than interpreted differently by a browser/proxy.
        using(var socket=new TcpClient())
        {
            await socket.ConnectAsync(IPAddress.Loopback,server.Port);var stream=socket.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET / HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\nHost: evil.example\r\n\r\n"));
            var bytes=new byte[256];var count=await stream.ReadAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Check(Encoding.ASCII.GetString(bytes,0,count).StartsWith("HTTP/1.1 400"),"ambiguous duplicate headers rejected");
        }
        Console.WriteLine($"Phone browser integration: {checks} checks passed.");
    }
    sealed class Clock:TimeProvider
    {
        DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan duration)=>now+=duration;
    }
}

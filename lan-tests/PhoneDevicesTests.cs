using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kachalka.Lan;

/// <summary>Remembered devices and file streaming of the phone-access server.</summary>
static class PhoneDevicesTests
{
    sealed class Clock:TimeProvider
    {
        DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan duration)=>now+=duration;
    }

    public static async Task Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"kachalka-phone-devices-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var devicesFile=Path.Combine(directory,"phone-devices.json");
        var movie=Path.Combine(directory,"movie.mkv");var bytes=new byte[300_000];new Random(5).NextBytes(bytes);File.WriteAllBytes(movie,bytes);
        var time=new Clock();int checks=0;
        void Check(bool ok,string what){if(!ok)throw new Exception("phone devices: "+what);checks++;Console.WriteLine("PASS: phone devices "+what);}
        MediaFile? Resolve(string id,int index)=>id=="abc123"&&index==0?new(movie,"video/x-matroska"):null;
        try
        {
            string token;
            await using (var server=new PhoneRemoteServer((m,a,c)=>Task.FromResult<object>(new{m}),0,true,time,devicesFile,Resolve))
            {
                server.Start();
                using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{server.Port}"),Timeout=TimeSpan.FromSeconds(10)};
                async Task<HttpResponseMessage> Post(string path,object data,string? bearer=null)
                {
                    var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
                    request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
                    if(bearer!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
                    return await http.SendAsync(request);
                }
                string Code()=>JsonSerializer.SerializeToElement(server.OpenPairing()).GetProperty("code").GetString()!;
                using (var response=await Post("/pair",new{code=Code(),remember=true,name="Гостиная ТВ"}))
                {
                    Check(response.IsSuccessStatusCode,"a code pairs and the device is remembered");
                    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    token=json.RootElement.GetProperty("token").GetString()!;
                    Check(json.RootElement.GetProperty("remembered").GetBoolean(),"the answer says the device is remembered");
                }
                Check(!File.ReadAllText(devicesFile).Contains(token)&&File.ReadAllText(devicesFile).Contains("Гостиная ТВ"),"only a hash of the token is stored");
                Check(server.Devices().Count==1&&server.Devices()[0].Name=="Гостиная ТВ","the device is listed");
                // Without "remember" the usual short session is used and nothing is stored.
                using (var plain=await Post("/pair",new{code=Code()})){Check(plain.IsSuccessStatusCode&&server.Devices().Count==1,"a plain pairing is not remembered");}
                // Media: needs the token, supports ranges, serves only what the resolver names.
                async Task<HttpResponseMessage> Media(string path,string? bearer,long? from=null,long? to=null)
                {
                    var request=new HttpRequestMessage(HttpMethod.Get,path);
                    if(bearer!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
                    if(from!=null)request.Headers.Range=new RangeHeaderValue(from,to);
                    return await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);
                }
                using (var denied=await Media("/media/abc123/0",null)){Check(denied.StatusCode==HttpStatusCode.Unauthorized,"media without a token is refused");}
                using (var wrong=await Media("/media/abc123/0","0000")){Check(wrong.StatusCode==HttpStatusCode.Unauthorized,"media with a wrong token is refused");}
                using (var whole=await Media("/media/abc123/0",token))
                {
                    Check(whole.StatusCode==HttpStatusCode.OK&&(await whole.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(bytes)&&whole.Content.Headers.ContentType?.MediaType=="video/x-matroska","the whole file is served with its type");
                }
                using (var part=await Media("/media/abc123/0",token,1000,1999))
                {
                    Check(part.StatusCode==HttpStatusCode.PartialContent&&part.Content.Headers.ContentRange?.ToString()=="bytes 1000-1999/300000"&&(await part.Content.ReadAsByteArrayAsync()).AsSpan().SequenceEqual(bytes.AsSpan(1000,1000)),"a range is served as 206 with the right bytes");
                }
                using (var tail=await Media("/media/abc123/0",token,299_000,null)){Check((await tail.Content.ReadAsByteArrayAsync()).Length==1000,"an open-ended range reads to the end");}
                using (var beyond=await Media("/media/abc123/0",token,400_000,null)){Check(beyond.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable,"a range past the end is refused");}
                foreach(var path in new[]{"/media/abc123/1","/media/zzz/0","/media/abc123/x","/media/../abc123/0","/media/abc123","/media/abc%2e/0"})
                    using(var missing=await Media(path,token)){Check(missing.StatusCode==HttpStatusCode.NotFound,"media path "+path+" is not served");}
                // Forget revokes at once.
                var id=server.Devices()[0].Id;
                Check(server.Forget(id)&&!server.Forget(id),"a device can be forgotten once");
                using (var revoked=await Post("/api/downloads",new{},token)){Check(revoked.StatusCode==HttpStatusCode.Unauthorized,"a forgotten device is refused");}
                using (var code=await Post("/pair",new{code=Code(),remember=true,name="Телефон"}))
                { using var json=JsonDocument.Parse(await code.Content.ReadAsStringAsync());token=json.RootElement.GetProperty("token").GetString()!; }
            }
            // A new server instance (an app restart) still knows the remembered device.
            Check(PhoneRemoteServer.HasRememberedDevices(devicesFile,time),"the file reports a remembered device");
            await using (var restarted=new PhoneRemoteServer((m,a,c)=>Task.FromResult<object>(new{m}),0,true,time,devicesFile,Resolve))
            {
                restarted.Start();
                using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{restarted.Port}"),Timeout=TimeSpan.FromSeconds(10)};
                async Task<HttpStatusCode> Call(string? bearer)
                {
                    var request=new HttpRequestMessage(HttpMethod.Post,"/api/downloads"){Content=new StringContent("{}",Encoding.UTF8,"application/json")};
                    request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
                    if(bearer!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
                    using var response=await http.SendAsync(request);return response.StatusCode;
                }
                Check(await Call(token)==HttpStatusCode.OK,"after a restart the remembered device connects without a code");
                Check(await Call("nope")==HttpStatusCode.Unauthorized,"after a restart other tokens are still refused");
                time.Advance(TimeSpan.FromDays(91));
                Check(await Call(token)==HttpStatusCode.Unauthorized&&restarted.Devices().Count==0,"a device expires after 90 days");
                Check(!PhoneRemoteServer.HasRememberedDevices(devicesFile,time),"an expired device no longer starts the server");
            }
            // Many devices: the oldest makes room.
            await using (var crowded=new PhoneRemoteServer((m,a,c)=>Task.FromResult<object>(new{m}),0,true,time,Path.Combine(directory,"crowded.json"),Resolve))
            {
                crowded.Start();
                using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{crowded.Port}"),Timeout=TimeSpan.FromSeconds(10)};
                for(var i=0;i<PhoneRemoteServer.MaxRememberedDevices+3;i++)
                {
                    var code=JsonSerializer.SerializeToElement(crowded.OpenPairing()).GetProperty("code").GetString();
                    var request=new HttpRequestMessage(HttpMethod.Post,"/pair"){Content=new StringContent(JsonSerializer.Serialize(new{code,remember=true,name="d"+i}),Encoding.UTF8,"application/json")};
                    request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
                    using var response=await http.SendAsync(request);if(!response.IsSuccessStatusCode)throw new Exception("pair "+i);
                }
                Check(crowded.Devices().Count==PhoneRemoteServer.MaxRememberedDevices&&crowded.Devices().All(x=>x.Name!="d0"),"the number of remembered devices is limited");
                crowded.ForgetAllDevices();Check(crowded.Devices().Count==0&&!PhoneRemoteServer.HasRememberedDevices(Path.Combine(directory,"crowded.json"),time),"all devices can be forgotten");
            }
            // The host that gives no devices file never remembers.
            await using (var memoryOnly=new PhoneRemoteServer((m,a,c)=>Task.FromResult<object>(new{m}),0,true,time))
            {
                memoryOnly.Start();
                using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri($"http://127.0.0.1:{memoryOnly.Port}"),Timeout=TimeSpan.FromSeconds(10)};
                var code=JsonSerializer.SerializeToElement(memoryOnly.OpenPairing()).GetProperty("code").GetString();
                var request=new HttpRequestMessage(HttpMethod.Post,"/pair"){Content=new StringContent(JsonSerializer.Serialize(new{code,remember=true,name="x"}),Encoding.UTF8,"application/json")};
                request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
                using var response=await http.SendAsync(request);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Check(!json.RootElement.GetProperty("remembered").GetBoolean()&&memoryOnly.Devices().Count==0,"without a devices file nothing is remembered");
            }
            Console.WriteLine($"PASS: phone devices ({checks} checks)");
        }
        finally{try{Directory.Delete(directory,true);}catch(IOException){}}
    }
}

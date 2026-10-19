using System.IO;
using System.Text.Json;

namespace Kachalka;

public sealed class ReleaseAudioRulesClient(SourceClient client,Uri? endpoint=null,string? cachePath=null,Func<DateTime>? utcNow=null)
{
    public static readonly Uri Endpoint=new("https://catalog-production-e329.up.railway.app/api/release-audio-rules");
    public static readonly Uri Fallback=new("https://github.com/peregond/ka4alka/releases/download/catalog-data/release-audio-rules.json");
    readonly SemaphoreSlim gate=new(1,1);
    readonly Func<DateTime> clock=utcNow??(()=>DateTime.UtcNow);
    DateTime nextCheck;bool restored;
    public async Task Refresh(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(clock()<nextCheck)return;
            var path=cachePath??Path.Combine(Preferences.DataDir,"cache","release-audio-rules.json");
            if(!restored)
            {
                restored=true;
                try
                {
                    if(File.Exists(path)&&new FileInfo(path).Length<=ReleaseAudioRules.MaxBytes)
                    {
                        ReleaseAudio.ConfigureRules(await File.ReadAllBytesAsync(path,ct));
                        var freshUntil=File.GetLastWriteTimeUtc(path).AddMinutes(5);
                        if(clock()<freshUntil){nextCheck=freshUntil;return;}
                    }
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException){}
            }
            foreach(var address in endpoint!=null?new[]{endpoint}:new[]{Endpoint,Fallback})
            {
                try
                {
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var bytes=await client.Read(address,ReleaseAudioRules.MaxBytes,deadline.Token);
                    ReleaseAudio.ConfigureRules(bytes);nextCheck=clock().AddMinutes(5);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path+".tmp",bytes,ct);File.Move(path+".tmp",path,true);
                    }
                    catch(Exception error) when(error is IOException or UnauthorizedAccessException){}
                    DiagnosticLog.Write("release-audio-rules",new{Revision=ReleaseAudio.RulesRevision,Source=address.Host});return;
                }
                catch(Exception error) when(!ct.IsCancellationRequested&&error is System.Net.Http.HttpRequestException or IOException or JsonException or InvalidDataException or OperationCanceledException or System.Text.RegularExpressions.RegexMatchTimeoutException){}
            }
            nextCheck=clock().AddMinutes(1);
        }
        finally{gate.Release();}
    }
}

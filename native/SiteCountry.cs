using System.Text;
using System.Text.RegularExpressions;

namespace Kachalka;

// The country of the user's address as Cloudflare sees it: the same "loc" the
// chatgpt.site hosting uses to refuse Russia and Belarus. Read once and kept
// for six hours; an unanswered check is retried after ten minutes.
public static class SiteCountry
{
    static readonly Uri[] Traces=[new("https://www.cloudflare.com/cdn-cgi/trace"),new("https://1.1.1.1/cdn-cgi/trace")];
    static readonly object gate=new();
    static Task<string?>? current;
    static DateTime expiresUtc;

    public static string? Parse(byte[] trace)
    {
        var match=Regex.Match(Encoding.ASCII.GetString(trace),@"(?m)^loc=([A-Z]{2})\r?$");
        return match.Success?match.Groups[1].Value:null;
    }

    // Countries where chatgpt.site answers with its block page.
    public static bool Blocked(string? country)=>country is "RU" or "BY";

    public static async Task<string?> Get(SourceClient client,CancellationToken ct)
    {
        Task<string?> task;
        lock(gate)
        {
            if(current==null||DateTime.UtcNow>=expiresUtc){current=Detect(client);expiresUtc=DateTime.UtcNow.AddMinutes(10);}
            task=current;
        }
        // A slow answer never holds the catalog back for more than a few seconds.
        try{return await task.WaitAsync(TimeSpan.FromSeconds(4),ct);}
        catch(TimeoutException){return null;}
    }

    static async Task<string?> Detect(SourceClient client)
    {
        foreach(var trace in Traces)
        {
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
                if(Parse(await client.Read(trace,4096,timeout.Token)) is {} country)
                {
                    lock(gate)expiresUtc=DateTime.UtcNow.AddHours(6);
                    DiagnosticLog.Write("site-country",new{Country=country,Railway=Blocked(country)});
                    return country;
                }
            }
            // Any failure, including a client disposed meanwhile, only means the country is unknown.
            catch(Exception){}
        }
        return null;
    }
}

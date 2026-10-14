using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace Kachalka;

// A film or series the link points to. The slug is the title's page on the catalog site and is what
// identifies it; the title and year only let the card show before its details load.
public sealed record ShareTarget(string Section,string Slug,string Title,int Year)
{
    public string PageUrl=>LiveCatalog.Base+(Section=="Фильмы"?"/movies/":"/tvseries/")+Slug;
}

// Links of the form kachalka://movie/<slug>?title=…&year=… (kachalka://series/… for series). They open the
// card in Качалка by a click where Windows knows the protocol, or when pasted into the search field.
public static class ShareLink
{
    public const string Scheme="kachalka";
    const int MaximumLength=2048;
    static readonly Regex Slug=new(@"^[-\p{L}\p{N}]{1,120}$",RegexOptions.CultureInvariant);
    static readonly Regex Embedded=new(@"kachalka://\S+",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);

    public static string? Create(MediaItem item)
    {
        var kind=item.Section switch{"Фильмы"=>"movie","Сериалы"=>"series",_=>null};
        var path=item.Section=="Фильмы"?"/movies/":"/tvseries/";
        if(kind==null||!Uri.TryCreate(item.PageUrl,UriKind.Absolute,out var page)||page.Scheme!="https"||page.Host!=new Uri(LiveCatalog.Base).Host||!page.IsDefaultPort||page.UserInfo.Length>0||page.Query.Length>0||page.Fragment.Length>0||!page.AbsolutePath.StartsWith(path,StringComparison.Ordinal))return null;
        var slug=Uri.UnescapeDataString(page.AbsolutePath[path.Length..].TrimEnd('/'));
        if(!Slug.IsMatch(slug))return null;
        var link=new StringBuilder(Scheme+"://").Append(kind).Append('/').Append(Escape(slug));
        var title=Clean(item.Title);
        if(title.Length>0)link.Append("?title=").Append(Escape(title));
        if(item.Year is >=1888 and <=2100)link.Append(title.Length>0?'&':'?').Append("year=").Append(item.Year);
        return link.ToString();
    }

    // Accepts the link alone or inside a short message ("Курьер (2026) kachalka://…"). Anything else, including
    // a link to another site or an unknown kind, is not a share link and is left to the caller.
    public static ShareTarget? Parse(string? text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>MaximumLength*4)return null;
        var match=Embedded.Match(text);
        if(!match.Success||match.Length>MaximumLength)return null;
        var value=match.Value.TrimEnd('.',',',';',')','»','"','\'','/');
        var rest=value[(Scheme.Length+3)..];
        var queryStart=rest.IndexOfAny(['?','#']);
        var path=(queryStart<0?rest:rest[..queryStart]).Trim('/');
        var query=queryStart<0||rest[queryStart]=='#'?"":rest[(queryStart+1)..].Split('#')[0];
        var parts=path.Split('/');
        if(parts.Length!=2)return null;
        var section=parts[0].ToLowerInvariant() switch{"movie"=>"Фильмы","series"=>"Сериалы",_=>null};
        string slug;
        try{slug=Uri.UnescapeDataString(parts[1]);}catch(UriFormatException){return null;}
        if(section==null||!Slug.IsMatch(slug))return null;
        string title="";var year=0;
        foreach(var pair in query.Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var separator=pair.IndexOf('=');if(separator<=0)continue;
            string key,data;
            try{key=Uri.UnescapeDataString(pair[..separator].Replace('+',' '));data=Uri.UnescapeDataString(pair[(separator+1)..].Replace('+',' '));}
            catch(UriFormatException){continue;}
            if(key=="title")title=Clean(data);
            else if(key=="year"&&int.TryParse(data,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var parsed)&&parsed is >=1888 and <=2100)year=parsed;
        }
        return new(section,slug,title,year);
    }

    static string Clean(string? value)
    {
        // Control and direction-override characters could make a title read as something else.
        var text=new string((value??"").Where(c=>char.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.Format).Select(c=>char.IsControl(c)?' ':c).ToArray());
        text=Regex.Replace(text,@"\s+"," ").Trim();
        return text.Length>200?text[..200].TrimEnd():text;
    }

    // Letters and digits stay readable (a Cyrillic title is still a Cyrillic title in a chat); everything
    // else, spaces and separators included, is percent-encoded so the link stays one unbroken word.
    static string Escape(string value)
    {
        var result=new StringBuilder();
        Span<byte> bytes=stackalloc byte[4];
        foreach(var rune in value.EnumerateRunes())
        {
            if(Rune.IsLetterOrDigit(rune)||rune.Value is '-' or '.' or '_' or '~'){result.Append(rune.ToString());continue;}
            var count=rune.EncodeToUtf8(bytes);
            for(var i=0;i<count;i++)result.Append('%').Append(bytes[i].ToString("X2"));
        }
        return result.ToString();
    }
}

// A second copy of Качалка started by a link hands the link to the copy already running and exits.
public static class ShareLinkPipe
{
    static string Name(string key)=>"Kachalka-"+key+"-links";
    // The copy started by the click owns the foreground and lets the running copy bring its window forward.
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    const int AnyProcess=-1;

    public static bool Send(string key,string link)
    {
        try
        {
            AllowSetForegroundWindow(AnyProcess);
            using var pipe=new NamedPipeClientStream(".",Name(key),PipeDirection.Out,PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            var bytes=Encoding.UTF8.GetBytes(link);
            pipe.Write(bytes,0,bytes.Length);pipe.Flush();
            return true;
        }
        catch(Exception error) when(error is IOException or TimeoutException or UnauthorizedAccessException){return false;}
    }

    public static async Task Listen(string key,Action<string> received,CancellationToken cancellation)
    {
        while(!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe=new NamedPipeServerStream(Name(key),PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var buffer=new byte[8192];var length=0;
                while(length<buffer.Length)
                {
                    var read=await pipe.ReadAsync(buffer.AsMemory(length),timeout.Token);
                    if(read==0)break;
                    length+=read;
                }
                var text=Encoding.UTF8.GetString(buffer,0,length);
                if(ShareLink.Parse(text)!=null)received(text);
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested){return;}
            catch(OperationCanceledException){}
            catch(IOException){await Task.Delay(500,cancellation).ContinueWith(_=>{},TaskScheduler.Default);}
            catch(UnauthorizedAccessException){return;}
        }
    }
}

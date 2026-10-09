using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kachalka;

public static class DiagnosticLog
{
    static readonly object sync=new();
    public static void Write(string name,object details)
    {
        try
        {
            lock(sync)
            {
                Directory.CreateDirectory(Preferences.DataDir);
                var path=Path.Combine(Preferences.DataDir,"diagnostic.log");
                if(File.Exists(path)&&new FileInfo(path).Length>512*1024)File.Move(path,path+".previous",true);
                File.AppendAllText(path,JsonSerializer.Serialize(new{Utc=DateTime.UtcNow,Event=name,Details=details})+Environment.NewLine);
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

public static class DiagnosticReport
{
    public const string IssueUrl="https://github.com/peregond/ka4alka/issues/new?template=bug_report.yml";
    public static string Redact(string text,IEnumerable<string> paths)
    {
        foreach(var path in paths.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x=>x.Length))
        {
            text=Regex.Replace(text,Regex.Escape(path),"[папка]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            text=Regex.Replace(text,Regex.Escape(JsonSerializer.Serialize(path)[1..^1]),"[папка]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        }
        // Decode JSON string values before removing embedded URLs/paths. In logs,
        // Unicode and slashes may be escaped, so plaintext replacement alone is
        // insufficient for a copied or browser-prefilled diagnostic report.
        text=Regex.Replace(text,@"""(?:\\.|[^""\\])*""",match=>
        {
            try
            {
                var value=JsonSerializer.Deserialize<string>(match.Value);
                if(value==null)return match.Value;
                var clean=RedactCredentials(RedactLocations(value),embedded:true);
                return value==clean?match.Value:JsonSerializer.Serialize(clean);
            }
            catch(JsonException){return match.Value;}
        });
        return RedactCredentials(RedactLocations(text));
    }
    // Use the same complete, nonrecursive credential sanitizer for decoded
    // generic JSON messages and plaintext. Quoted values can contain spaces;
    // masking only their first word would still expose the rest of a password.
    static string RedactCredentials(string text,bool embedded=false)
    {
        text=Regex.Replace(text,@"(?i)(""(?:api[_-]?key|(?:access[_-]?|refresh[_-]?)?token|passkey|password|(?:client[_-]?)?secret|authorization|cookie|set-cookie)""\s*:\s*)""(?:\\.|[^""\\])*""", "$1\"[скрыто]\"");
        text=Regex.Replace(text,@"(?i)(https?://)[^/\s:@]+:[^/\s@]+@","$1[скрыто]@");
        text=Regex.Replace(text,@"(?i)((?:[?&]|\b)(?:api[_-]?key|(?:access[_-]?|refresh[_-]?)?token|passkey|password|(?:client[_-]?)?secret|authorization|cookie|set-cookie)\s*[=:]\s*)(?:""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])*')","$1[скрыто]");
        text=Regex.Replace(text,@"(?i)((?:[?&]|\b)(?:api[_-]?key|(?:access[_-]?|refresh[_-]?)?token|passkey|password|(?:client[_-]?)?secret|authorization)\s*[=:]\s*)(?:Bearer\s+|Basic\s+)?[^\s&\""'<>]+","$1[скрыто]");
        text=Regex.Replace(text,@"(?i)\b(Bearer|Basic)\s+(?:""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])*')","$1 [скрыто]");
        text=Regex.Replace(text,@"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._~+/-]+=*","$1 [скрыто]");
        var headers=embedded?@"(?im)(\b(?:Authorization|Set-Cookie|Cookie)\s*:\s*)[^\r\n]+":@"(?im)^(\s*(?:Authorization|Set-Cookie|Cookie)\s*:\s*)[^\r\n]+";
        text=Regex.Replace(text,headers,"$1[скрыто]");
        text=Regex.Replace(text,@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b","[скрыто]");
        return text;
    }
    static string RedactLocations(string text)
    {
        text=Regex.Replace(text,@"(?i)\bmagnet:\?[^\s""<>]+","[magnet-ссылка скрыта]");
        // A tracker passkey may be in an unnamed path segment, not a query key.
        // Keep the host useful for diagnosis; never export the URL's path/query.
        text=Regex.Replace(text,@"(?i)\b(?:https?|udp|wss?)://[^\s""<>]+",match=>
        {
            var raw=match.Value;
            if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||string.IsNullOrEmpty(uri.Host))return "[адрес скрыт]";
            return uri.Scheme+"://"+uri.Host+(uri.IsDefaultPort?"":":"+uri.Port)+"/[скрыто]";
        });
        text=Regex.Replace(text,@"(?i)(?<![\p{L}\p{N}])[a-z]:[\\/][^\r\n""<>|]+","[папка]");
        text=Regex.Replace(text,@"(?<![\\\p{L}\p{N}])\\\\[^\r\n""<>|]+","[папка]");
        text=Regex.Replace(text,@"(?i)(?<![:\p{L}\p{N}])/(?:users|home|workspace|tmp|var|mnt|media|volumes)/[^\r\n""<>]+","[папка]");
        return text;
    }
    public sealed record Snapshot(string Header,IReadOnlyList<string> DownloadLines,IReadOnlyList<string> PrivatePaths,string DataDirectory,string DownloadFolder);
    // Capture is intentionally synchronous: DownloadItem properties and the
    // collection belong to the WPF dispatcher. Only the immutable DTO leaves it.
    public static Snapshot Capture(Preferences prefs,DownloadService downloads)
    {
        downloads.Update();
        var header=JsonSerializer.Serialize(new{Version=typeof(Preferences).Assembly.GetName().Version?.ToString(3),Utc=DateTime.UtcNow,OS=Environment.OSVersion.VersionString,Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),Theme=prefs.Light?"Светлая":"Тёмная",prefs.MaxDownloadKbps,prefs.MaxUploadKbps,FolderConfigured=prefs.FolderConfigured});
        var lines=downloads.Items.Take(100).Select(item=>JsonSerializer.Serialize(new{item.DisplayName,item.ReleaseSource,item.InfoHash,item.Paused,item.Status,item.Progress,item.PeersText,item.Hint,Files=item.Files.Count})).ToArray();
        var paths=new[]{prefs.Folder,Preferences.DataDir,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}.Concat(downloads.Items.Select(x=>x.Folder)).ToArray();
        return new(header,lines,paths,Preferences.DataDir,prefs.Folder);
    }
    public static Task<string> CreateAsync(Preferences prefs,DownloadService downloads,CancellationToken cancellation=default)
    {
        var snapshot=Capture(prefs,downloads);
        return Task.Run(()=>Compose(snapshot,cancellation),cancellation);
    }
    public static string Create(Preferences prefs,DownloadService downloads)
    {
        return Compose(Capture(prefs,downloads));
    }
    public static string Compose(Snapshot snapshot,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        var report=new StringBuilder();
        report.AppendLine("Диагностика Качалки");
        report.AppendLine(snapshot.Header);
        report.AppendLine(JsonSerializer.Serialize(new{DownloadFolderExists=Directory.Exists(snapshot.DownloadFolder)}));
        report.AppendLine("Текущие загрузки:");
        foreach(var line in snapshot.DownloadLines)report.AppendLine(line);
        foreach(var file in new[]{"diagnostic.log.previous","diagnostic.log","error.log.previous","error.log"})
        {
            cancellation.ThrowIfCancellationRequested();
            report.AppendLine("\n"+file+":");
            try
            {
                using var stream=new FileStream(Path.Combine(snapshot.DataDirectory,file),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                var length=stream.Length;var truncated=length>48*1024;
                if(truncated){stream.Seek(-48*1024,SeekOrigin.End);report.AppendLine("… последние записи …");}
                // Bound the bytes even when another process keeps appending.
                var bytes=new byte[(int)Math.Min(length,48*1024)];var count=0;
                while(count<bytes.Length){cancellation.ThrowIfCancellationRequested();var read=stream.Read(bytes,count,bytes.Length-count);if(read==0)break;count+=read;}
                var tail=Encoding.UTF8.GetString(bytes,0,count);
                // Drop a partial first record completely. Its missing prefix
                // could contain the key needed to recognize a secret value.
                if(truncated){var first=tail.IndexOf('\n');tail=first<0?"":tail[(first+1)..];}
                report.AppendLine(tail);
            }
            catch(FileNotFoundException){report.AppendLine("Записей пока нет.");}
            catch(DirectoryNotFoundException){report.AppendLine("Записей пока нет.");}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){report.AppendLine("Лог недоступен: "+error.GetType().Name);}
        }
        cancellation.ThrowIfCancellationRequested();
        return Redact(report.ToString(),snapshot.PrivatePaths);
    }
}

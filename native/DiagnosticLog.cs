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
        text=Regex.Replace(text,@"(?i)(""(?:api[_-]?key|token|passkey|password|secret|authorization)""\s*:\s*)""(?:\\.|[^""\\])*""", "$1\"[скрыто]\"");
        text=Regex.Replace(text,@"(?i)(https?://)[^/\s:@]+:[^/\s@]+@","$1[скрыто]@");
        text=Regex.Replace(text,@"(?i)((?:[?&]|\b)(?:api[_-]?key|token|passkey|password|secret|authorization)\s*[=:]\s*)(?:Bearer\s+)?[^\s&\""<>]+","$1[скрыто]");
        text=Regex.Replace(text,@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*","Bearer [скрыто]");
        return text;
    }
    public static string Create(Preferences prefs,DownloadService downloads)
    {
        downloads.Update();
        var report=new StringBuilder();
        report.AppendLine("Диагностика Качалки");
        report.AppendLine(JsonSerializer.Serialize(new{Version=typeof(Preferences).Assembly.GetName().Version?.ToString(3),Utc=DateTime.UtcNow,OS=Environment.OSVersion.VersionString,Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),Theme=prefs.Light?"Светлая":"Тёмная",prefs.MaxDownloadKbps,prefs.MaxUploadKbps,FolderConfigured=prefs.FolderConfigured,DownloadFolderExists=Directory.Exists(prefs.Folder)}));
        report.AppendLine("Текущие загрузки:");
        foreach(var item in downloads.Items.Take(100))
            report.AppendLine(JsonSerializer.Serialize(new{item.DisplayName,item.ReleaseSource,item.InfoHash,item.Paused,item.Status,item.Progress,item.PeersText,item.Hint,Files=item.Files.Count}));
        foreach(var file in new[]{"diagnostic.log.previous","diagnostic.log","error.log.previous","error.log"})
        {
            report.AppendLine("\n"+file+":");
            try
            {
                using var stream=new FileStream(Path.Combine(Preferences.DataDir,file),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                var truncated=stream.Length>48*1024;if(truncated){stream.Seek(-48*1024,SeekOrigin.End);report.AppendLine("… последние записи …");}
                using var reader=new StreamReader(stream);if(truncated)reader.ReadLine();report.AppendLine(reader.ReadToEnd());
            }
            catch(FileNotFoundException){report.AppendLine("Записей пока нет.");}
            catch(DirectoryNotFoundException){report.AppendLine("Записей пока нет.");}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){report.AppendLine("Лог недоступен: "+error.GetType().Name);}
        }
        return Redact(report.ToString(),new[]{prefs.Folder,Preferences.DataDir,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}.Concat(downloads.Items.Select(x=>x.Folder)));
    }
}

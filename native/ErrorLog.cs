using System.IO;

namespace Kachalka;

public static class ErrorLog
{
    static readonly object sync=new();
    public static void Write(Exception error)
    {
        try
        {
            lock(sync)
            {
                Directory.CreateDirectory(Preferences.DataDir);
                var path=Path.Combine(Preferences.DataDir,"error.log");
                if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".previous",true);
                File.AppendAllText(path,$"{DateTimeOffset.Now:O}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
}

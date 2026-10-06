using System.IO;
namespace Kachalka;
public static class DownloadFiles
{
    public static void ValidatePath(string folder,string file)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));var path=Path.GetFullPath(file);
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл находится вне папки этой загрузки.");
        for(var current=path;current!=null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Удаление файлов через ссылки запрещено.");
    }
    public static void RemoveEmptyParents(string folder,string file)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        for(var dir=Path.GetDirectoryName(Path.GetFullPath(file));dir!=null&&!dir.Equals(root,StringComparison.OrdinalIgnoreCase);dir=Path.GetDirectoryName(dir))
        {
            if(!Directory.Exists(dir)||Directory.EnumerateFileSystemEntries(dir).Any())break;
            try{Directory.Delete(dir);}catch(IOException){break;}catch(UnauthorizedAccessException){break;}
        }
    }
}

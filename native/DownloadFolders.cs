using System.IO;
namespace Kachalka;
public static class DownloadFolders
{
    public static string Prepare(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Укажите полный путь к папке загрузок.",nameof(path));
        var parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException("Выбранная папка больше недоступна: "+parent);
        var folder=string.Equals(Path.GetFileName(parent),"Ka4alka",StringComparison.OrdinalIgnoreCase)?parent:Path.Combine(parent,"Ka4alka");
        Directory.CreateDirectory(folder);
        return folder;
    }
}


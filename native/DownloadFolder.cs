using System.IO;
using System.Windows;
using Microsoft.Win32;

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

public partial class MainWindow
{
    public bool EnsureDownloadFolder()
    {
        if(prefs.FolderConfigured&&Path.IsPathFullyQualified(prefs.Folder)&&Directory.Exists(prefs.Folder))return true;

        var parent=Path.GetDirectoryName(prefs.Folder);
        var initialDirectory=Directory.Exists(prefs.Folder)
            ? prefs.Folder
            : Directory.Exists(parent)
                ? parent!
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var picker=new OpenFolderDialog
        {
            Title="Выберите папку — внутри будет создана Ka4alka для загрузок",
            InitialDirectory=initialDirectory
        };
        if(picker.ShowDialog(this)!=true)return false;

        try
        {
            SetDownloadFolder(picker.FolderName);
            return true;
        }
        catch(Exception error)
        {
            MessageBox.Show(this,"Не удалось сохранить папку загрузок: "+error.Message,"Качалка",MessageBoxButton.OK,MessageBoxImage.Error);
            return false;
        }
    }

    public void SetDownloadFolder(string path)
    {
        var folder=DownloadFolders.Prepare(path);

        var previousFolder=prefs.Folder;
        var previouslyConfigured=prefs.FolderConfigured;
        prefs.Folder=folder;
        prefs.FolderConfigured=true;
        try{prefs.Save();}
        catch
        {
            prefs.Folder=previousFolder;
            prefs.FolderConfigured=previouslyConfigured;
            throw;
        }
    }
}

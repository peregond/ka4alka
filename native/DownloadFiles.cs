using System.IO;
namespace Kachalka;
public static class DownloadFiles
{
    const int SharingViolation=32,LockViolation=33;
    static readonly TimeSpan[] RetryDelays=[TimeSpan.FromMilliseconds(120),TimeSpan.FromMilliseconds(240),TimeSpan.FromMilliseconds(400),TimeSpan.FromMilliseconds(600)];
    public static void ValidatePath(string folder,string file)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));var path=Path.GetFullPath(file);
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл находится вне папки этой загрузки.");
        for(var current=path;current!=null;current=Path.GetDirectoryName(current))
            if(Attributes(current) is { } attributes&&(attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Удаление файлов через ссылки запрещено.");
    }
    static FileAttributes? Attributes(string path)
    {
        try{return File.GetAttributes(path);}
        catch(FileNotFoundException){return null;}
        catch(DirectoryNotFoundException){return null;}
    }
    // Perform the filesystem work away from the dispatcher. A short-lived
    // scanner/player handle may close soon after the torrent writer stops.
    // Retry only Windows sharing/lock errors, never permissions or unsafe paths.
    public static Task<int> DeleteAsync(string folder,IReadOnlyList<string> files,CancellationToken cancellation=default)
    {
        var paths=files.Where(path=>!string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.Run(async()=>
        {
            foreach(var path in paths)
            {
                cancellation.ThrowIfCancellationRequested();ValidatePath(folder,path);
                if(Attributes(path) is { } attributes&&(attributes&FileAttributes.Directory)!=0)throw new IOException("Вместо файла обнаружена папка. Её содержимое не удалено.");
            }
            var deleted=0;
            foreach(var path in paths)
            {
                try
                {
                    for(var attempt=0;;attempt++)
                    {
                        cancellation.ThrowIfCancellationRequested();ValidatePath(folder,path);
                        var attributes=Attributes(path);
                        if(attributes==null)break;
                        if((attributes.Value&FileAttributes.Directory)!=0)throw new IOException("Вместо файла обнаружена папка. Её содержимое не удалено.");
                        var readOnly=(attributes.Value&FileAttributes.ReadOnly)!=0;
                        try
                        {
                            if(readOnly)File.SetAttributes(path,attributes.Value&~FileAttributes.ReadOnly);
                            File.Delete(path);deleted++;break;
                        }
                        catch(IOException error)when(attempt<RetryDelays.Length&&IsInUse(error))
                        {
                            RestoreReadOnly(folder,path,attributes.Value,readOnly);
                            await Task.Delay(RetryDelays[attempt],cancellation).ConfigureAwait(false);
                        }
                        catch
                        {
                            RestoreReadOnly(folder,path,attributes.Value,readOnly);throw;
                        }
                    }
                }
                catch(OperationCanceledException error)
                {throw new DownloadFileDeletionCanceledException(deleted,error);}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException)
                {throw new DownloadFileDeletionException(FailureMessage(error),deleted,error);}
            }
            foreach(var path in paths)RemoveEmptyParents(folder,path);
            return deleted;
        },cancellation);
    }
    static bool IsInUse(IOException error)=>(error.HResult&0xffff) is SharingViolation or LockViolation;
    static string FailureMessage(Exception error)=>error switch
    {
        IOException io when IsInUse(io)=>"Файл открыт в другой программе. Закрой проигрыватель или дождись проверки антивируса, затем повтори удаление.",
        UnauthorizedAccessException=>"Нет разрешения на удаление файла. Проверь права доступа к папке загрузки и повтори удаление.",
        _=>"Не удалось удалить файл. Проверь доступность папки загрузки и повтори удаление."
    };
    static void RestoreReadOnly(string folder,string path,FileAttributes original,bool changed)
    {
        if(!changed)return;
        try{ValidatePath(folder,path);if(Attributes(path) is { } current&&(current&FileAttributes.Directory)==0)File.SetAttributes(path,original);}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static void RemoveEmptyParents(string folder,string file)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        for(var dir=Path.GetDirectoryName(Path.GetFullPath(file));dir!=null&&!dir.Equals(root,StringComparison.OrdinalIgnoreCase);dir=Path.GetDirectoryName(dir))
        {
            try
            {
                ValidatePath(folder,file);
                if(!Directory.Exists(dir)||Directory.EnumerateFileSystemEntries(dir).Any())break;
                Directory.Delete(dir);
            }
            catch(IOException){break;}catch(UnauthorizedAccessException){break;}
        }
    }
}

public sealed class DownloadFileDeletionException(string message,int deletedFiles,Exception innerException):IOException(message,innerException)
{
    public int DeletedFiles {get;}=deletedFiles;
}

public sealed class DownloadFileDeletionCanceledException(int deletedFiles,OperationCanceledException innerException):OperationCanceledException("Удаление файлов отменено.",innerException,innerException.CancellationToken)
{
    public int DeletedFiles {get;}=deletedFiles;
}

using System.IO;
namespace Kachalka;

public static class CompletedPayloadFiles
{
    // Only metadata is read: no hashing or writes during normal queue recovery.
    public static string? Problem(IReadOnlyList<DownloadFile> files,CancellationToken cancellation=default)
    {
        if(files.Count==0)return "Не найден список файлов завершённой загрузки. Повторное скачивание требует подтверждения.";
        var completed=files.Where(file=>file.Size>0&&file.Progress>=100).ToArray();
        if(completed.Length==0)completed=files.Where(file=>file.Size>0).ToArray(); // legacy queues without per-file progress
        foreach(var file in completed)
        {
            cancellation.ThrowIfCancellationRequested();
            bool Present(string path)
            {
                if(string.IsNullOrWhiteSpace(path))return false;
                try{var info=new FileInfo(path);return info.Exists&&info.Length>=file.Size;}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return false;}
            }
            if(!Present(file.FullPath)&&!Present(file.IncompletePath))return "Файлы удалены, перемещены или недоступны. Верни их в папку загрузки либо выбери «Скачать заново».";
        }
        return null;
    }
}

public sealed partial class DownloadService
{
    bool checkingCompletedFiles;
    DateTime lastCompletedFilesCheck;
    Task completedFilesCheck=Task.CompletedTask;
    async Task<bool> ProtectCompletedFilesAsync(DownloadItem item)
    {
        if(!item.HasCompletedDownload&&!item.FilesMissing)return true;
        var files=item.Files.ToArray();
        var problem=await Task.Run(()=>CompletedPayloadFiles.Problem(files,lifetime.Token),lifetime.Token).WaitAsync(lifetime.Token);
        if(problem==null)
        {
            if(item.FilesMissing){item.FilesMissing=false;item.MissingFilesMessage="";item.Hint="";item.Status="На паузе";item.Stats=$"{item.Progress:F1}% · на паузе";item.Refresh();Save();}
            return true;
        }
        var changed=!item.FilesMissing||!item.Paused||item.Hint!=problem;
        pendingResume.Remove(item.Id);
        if(managers.TryGetValue(item.Id,out var manager))await StopManager(manager);
        item.FilesMissing=true;item.MissingFilesMessage=problem;item.Paused=true;item.Hint=problem;ApplyReliabilityStatus(item);if(changed){item.Refresh();Save();}
        return false;
    }
    public Task CheckCompletedFilesAsync(bool force=false)
    {
        if(closeRequested)return Task.CompletedTask;
        if(checkingCompletedFiles)return force?CheckAfterCurrentAsync(completedFilesCheck):completedFilesCheck;
        if(!Items.Any(item=>item.HasCompletedDownload||item.FilesMissing))return Task.CompletedTask;
        if(!force&&DateTime.UtcNow-lastCompletedFilesCheck<TimeSpan.FromSeconds(10))return Task.CompletedTask;
        return completedFilesCheck=CheckCompletedFilesCoreAsync();
    }
    async Task CheckAfterCurrentAsync(Task currentCheck)
    {await currentCheck;await CheckCompletedFilesAsync(true);}
    async Task CheckCompletedFilesCoreAsync()
    {
        checkingCompletedFiles=true;lastCompletedFilesCheck=DateTime.UtcNow;
        try
        {
            foreach(var item in Items.Where(x=>x.HasCompletedDownload||x.FilesMissing).ToArray())
            {
                await gate.WaitAsync(lifetime.Token);
                try{if(!closeRequested&&Items.Contains(item)&&!item.Busy&&(item.HasCompletedDownload||item.FilesMissing))await ProtectCompletedFilesAsync(item);}
                finally{gate.Release();}
            }
            await gate.WaitAsync(lifetime.Token);
            try{await ReleaseIdleEngine();}finally{gate.Release();}
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception error){ErrorLog.Write(error);}
        finally{checkingCompletedFiles=false;}
    }
    // This is the sole explicit opt-in to replace externally deleted payloads.
    public Task RedownloadAsync(DownloadItem item)=>SetPausedAsync(item,false,true);
}

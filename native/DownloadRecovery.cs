using MonoTorrent.Client;
using MonoTorrent.Trackers;

namespace Kachalka;

public sealed partial class DownloadService
{
    readonly DownloadSpace space;
    CancellationTokenSource lifetime=new();
    readonly Dictionary<string,CancellationTokenSource> metadataWatches=[];
    readonly Dictionary<string,DownloadSpaceCheck> spaceChecks=[];
    readonly Dictionary<string,DateTime> lastSpaceChecks=[];
    readonly HashSet<string> completionNotified=[];
    readonly Dictionary<string,string> errorNotified=[];
    bool closeRequested;
    bool? networkAvailable;
    DateTime lastRecoveryUtc;
    public event Action<DownloadNotice>? Notice;

    public void SetNetworkAvailable(bool available)
    {
        networkAvailable=available;
        foreach(var item in Items){ApplyReliabilityStatus(item);item.Refresh();}
    }
    public DownloadDiagnosticSnapshot GetDiagnostics(DownloadItem item)
    {
        managers.TryGetValue(item.Id,out var manager);meters.TryGetValue(item.Id,out var meter);spaceChecks.TryGetValue(item.Id,out var check);
        if(check==null&&item.SpaceCheckFailed)check=new(null,0,0,0,item.SpacePauseMessage);
        return new()
        {
            CapturedUtc=DateTime.UtcNow,DownloadId=item.Id,Name=item.DisplayName,
            State=manager?.State??TorrentState.Stopped,ManagerAvailable=manager!=null,Paused=item.Paused,
            LowSpacePaused=item.LowSpacePaused,HasMetadata=manager?.HasMetadata??item.Files.Count>0,Completed=item.Completed,
            NetworkAvailable=networkAvailable,Connections=manager?.OpenConnections??0,Seeds=manager?.Peers.Seeds??0,
            DownloadRate=item.DownloadRate,StartedUtc=item.LastStartedUtc,LastPayloadUtc=meter.Down?.LastPayloadUtc,
            RemainingBytes=manager?.HasMetadata==true||item.Files.Count>0?check?.OwnRemainingBytes:null,
            FreeBytes=check?.AvailableBytes,SpaceCheck=check,Error=manager?.Error?.Exception?.Message,
            DhtEnabled=engine!=null?engine.Settings.DhtEndPoint!=null:null,
            Trackers=manager?.TrackerManager.Tiers.SelectMany(t=>t.Trackers).Select(t=>new DownloadTrackerDiagnostic(t.Uri.Host,t.Uri.Scheme,t.Status.ToString(),t.Status==TrackerState.Ok,t.Status is TrackerState.Offline or TrackerState.InvalidResponse)).ToArray()??[]
        };
    }
    static DownloadItem SpaceCopy(DownloadItem item)=>new(){Id=item.Id,Folder=item.Folder,Progress=item.Progress,Files=[..item.Files]};
    async Task<bool> EnsureSpaceAsync(DownloadItem item,TorrentManager manager,CancellationToken token)
    {
        var copy=SpaceCopy(item);
        if(manager.Complete)copy.Progress=100;
        var commitments=Items.Where(x=>x.Id!=item.Id&&!x.Completed&&(!x.Paused||pendingResume.Contains(x.Id))).Select(SpaceCopy).ToArray();
        var check=await Task.Run(()=>space.Check(copy,commitments),CancellationToken.None).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if(check.Error==null&&manager.Complete&&check.OwnRemainingBytes==0)check=check with{RequiredBytes=0};
        spaceChecks[item.Id]=check;lastSpaceChecks[item.Id]=DateTime.UtcNow;
        if(check.HasEnoughSpace)
        {
            if(item.LowSpacePaused){item.Status="На паузе";item.Hint="";}
            item.LowSpacePaused=false;item.SpacePauseMessage="";item.SpaceCheckFailed=false;
            return true;
        }
        var firstPause=!item.LowSpacePaused;
        // Preserve the reason before stopping so a concurrent queue save cannot
        // restore this task as an active download on the next launch.
        item.LowSpacePaused=true;item.SpacePauseMessage=check.Message;item.SpaceCheckFailed=check.Error!=null;item.Paused=true;pendingResume.Remove(item.Id);
        if(manager.State!=TorrentState.Stopped)await StopManager(manager).WaitAsync(token);
        item.DownloadRate=0;item.UploadRate=0;item.Remaining="";item.PeersText="";item.Indeterminate=false;
        ApplyReliabilityStatus(item);item.Refresh();
        if(firstPause)RaiseNotice(new(check.Error==null?DownloadNoticeKind.LowSpace:DownloadNoticeKind.Error,item.Id,item.DisplayName,check.Message));
        return false;
    }
    public async Task PollReliabilityAsync(CancellationToken cancellationToken=default)
    {
        if(closeRequested)return;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellationToken);var token=linked.Token;
        if(!await gate.WaitAsync(0,token))return;
        try
        {
            var changed=false;
            foreach(var item in Items.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if(item.Paused||item.Busy||item.Completed||!managers.TryGetValue(item.Id,out var manager)||!manager.HasMetadata)continue;
                if(lastSpaceChecks.TryGetValue(item.Id,out var checkedUtc)&&DateTime.UtcNow-checkedUtc<TimeSpan.FromSeconds(15))continue;
                SnapshotFiles(item,manager);
                if(!await EnsureSpaceAsync(item,manager,token))changed=true;
            }
            if(changed){Save();await ReleaseIdleEngine();}
        }
        finally{gate.Release();}
    }
    void WatchMetadata(DownloadItem item,TorrentManager manager)
    {
        CancelMetadataWatch(item.Id);
        var stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);metadataWatches[item.Id]=stop;
        _=Wait();
        async Task Wait()
        {
            try
            {
                await manager.WaitForMetadataAsync(stop.Token);
                await gate.WaitAsync(stop.Token);
                try
                {
                    if(closeRequested||item.Paused||!Items.Contains(item)||!managers.TryGetValue(item.Id,out var current)||current!=manager)return;
                    SnapshotFiles(item,manager);
                    if(!await EnsureSpaceAsync(item,manager,stop.Token)){Save();await ReleaseIdleEngine();}
                }
                finally{gate.Release();}
            }
            catch(OperationCanceledException){}
            catch(Exception error){DiagnosticLog.Write("torrent-space-check",new{item.Id,Error=error.Message});}
            finally
            {
                if(metadataWatches.TryGetValue(item.Id,out var current)&&current==stop)metadataWatches.Remove(item.Id);
                stop.Dispose();
            }
        }
    }
    void CancelMetadataWatch(string id)
    {if(metadataWatches.Remove(id,out var stop))stop.Cancel();}
    void CancelAllMetadataWatches()
    {foreach(var id in metadataWatches.Keys.ToArray())CancelMetadataWatch(id);}

    // A network event says that an adapter is available; it does not prove that
    // the Internet or a particular tracker is reachable. Never infer a block.
    public async Task<int> RecoverConnectionsAsync(bool available,bool force=false,CancellationToken cancellationToken=default,bool autoRecover=true)
    {
        var returned=networkAvailable==false&&available;SetNetworkAvailable(available);
        if(closeRequested||!available||!autoRecover||!returned&&!force)return 0;
        if(!returned&&DateTime.UtcNow-lastRecoveryUtc<TimeSpan.FromSeconds(15))return 0;
        lastRecoveryUtc=DateTime.UtcNow;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellationToken);var token=linked.Token;
        await gate.WaitAsync(token);
        try
        {
            int recovered=0;
            foreach(var item in Items.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if(item.Paused||item.Busy||item.LowSpacePaused||!managers.TryGetValue(item.Id,out var manager))continue;
                if(manager.State is not (TorrentState.Metadata or TorrentState.Downloading or TorrentState.Seeding or TorrentState.Stopped))continue;
                try{if(await RestartConnectionsAsync(item,manager,token))recovered++;}
                catch(OperationCanceledException){throw;}
                catch(Exception error)
                {
                    item.Status="Не удалось восстановить соединение";item.Hint=error.Message;item.Refresh();
                    DiagnosticLog.Write("torrent-network-recovery-error",new{item.Id,Error=error.Message});
                }
            }
            if(recovered>0){Update();Save();DiagnosticLog.Write("torrent-network-recovered",new{Count=recovered});}
            return recovered;
        }
        finally{gate.Release();}
    }
    public async Task<bool> RetryConnectionsAsync(DownloadItem item,CancellationToken cancellationToken=default)
    {
        if(closeRequested||item.Paused||item.Busy||item.LowSpacePaused||networkAvailable==false)return false;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellationToken);var token=linked.Token;
        await gate.WaitAsync(token);
        try
        {
            if(networkAvailable==false||!Items.Contains(item)||item.Paused||!managers.TryGetValue(item.Id,out var manager)||manager.State is not (TorrentState.Metadata or TorrentState.Downloading or TorrentState.Seeding or TorrentState.Stopped or TorrentState.Error))return false;
            var restarted=await RestartConnectionsAsync(item,manager,token);Update();Save();return restarted;
        }
        finally{gate.Release();}
    }
    async Task<bool> RestartConnectionsAsync(DownloadItem item,TorrentManager manager,CancellationToken token)
    {
        SnapshotFiles(item,manager);
        if(!await EnsureSpaceAsync(item,manager,token))return false;
        await StopManager(manager).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if(closeRequested||networkAvailable==false||!Items.Contains(item)||item.Paused||item.Busy)return false;
        await manager.StartAsync();item.LastStartedUtc=DateTime.UtcNow;
        return true;
    }
    void ApplyReliabilityStatus(DownloadItem item)
    {
        if(item.LowSpacePaused)
        {
            item.Status=item.SpaceCheckFailed?"Папка недоступна · на паузе":"Недостаточно места · на паузе";item.Hint=string.IsNullOrWhiteSpace(item.SpacePauseMessage)?"Освободи место и нажми «Продолжить». Перед запуском место проверится заново.":item.SpacePauseMessage;
            item.DownloadRate=0;item.UploadRate=0;item.Remaining="";item.PeersText="";item.Indeterminate=false;
        }
        else if(!item.Paused&&networkAvailable==false&&!item.Completed)
        {
            item.Status="Нет подключения к сети";item.Hint="Подключение к сети отсутствует. Активные загрузки продолжатся после восстановления связи, если включено автовосстановление в настройках.";
            item.DownloadRate=0;item.UploadRate=0;item.Remaining="";item.PeersText="";item.Indeterminate=false;
        }
    }
    void ObserveNotices(DownloadItem item,TorrentManager manager)
    {
        if(!closeRequested&&!item.Paused&&manager.State==TorrentState.Seeding&&completionNotified.Add(item.Id))
        {RaiseNotice(new(DownloadNoticeKind.Completed,item.Id,item.DisplayName,"Загрузка завершена. Файлы сохранены в выбранной папке."));Save();}
        if(manager.State==TorrentState.Error)
        {
            var error=manager.Error?.Exception?.Message??"Загрузка остановилась с ошибкой. Открой «Почему не скачивается?» в очереди.";
            if(!closeRequested&&(!errorNotified.TryGetValue(item.Id,out var prior)||prior!=error))
            {errorNotified[item.Id]=error;RaiseNotice(new(DownloadNoticeKind.Error,item.Id,item.DisplayName,error));Save();}
        }
        else errorNotified.Remove(item.Id);
    }
    void RaiseNotice(DownloadNotice notice)
    {
        try{Notice?.Invoke(notice);}
        catch(Exception error){DiagnosticLog.Write("download-notice-error",new{Error=error.Message});}
    }
}

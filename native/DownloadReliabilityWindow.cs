using System.Windows;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    readonly CancellationTokenSource reliabilityCancellation=new();
    WindowsNotifications? downloadNotifications;
    DownloadRecoveryMonitor? downloadRecoveryMonitor;
    bool reliabilityPollBusy,recoveryBusy,recoveryPending,recoveryForced;
    void InitializeDownloadReliability()
    {
        downloadNotifications=new(this,()=>
        {
            if(closing||closed)return;
            if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
            ShowDownloads(this,new RoutedEventArgs());Activate();
        },()=>prefs.NotifyDownloads&&!closing&&!closed);
        downloads.Notice+=DownloadNoticeReceived;
        downloadRecoveryMonitor=new(Dispatcher,RequestDownloadRecovery);
        ContentRendered+=(_,_)=>RequestDownloadRecovery(false);
    }
    void DownloadNoticeReceived(DownloadNotice notice)
    {
        if(closing||closed||Dispatcher.HasShutdownStarted)return;
        if(!Dispatcher.CheckAccess())
        {
            _=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>DownloadNoticeReceived(notice)));return;
        }
        downloadNotifications?.Show(notice);
    }
    bool ShowTestDownloadNotification()=>downloadNotifications?.Test()==true;
    async void RefreshDownloadsReliably()
    {
        if(closing||closed)return;
        // Disk checks can wait on a network share. Keep all other transfer
        // counters and completion notifications updating while that I/O runs.
        try{downloads.Update();RefreshDownloadView();SyncTimer();}
        catch(Exception error){ErrorLog.Write(error);Status.Text="Не удалось обновить загрузки: "+error.Message;}
        if(reliabilityPollBusy)return;
        reliabilityPollBusy=true;
        try
        {
            await downloads.PollReliabilityAsync(reliabilityCancellation.Token);
            if(closing||closed)return;
            downloads.Update();RefreshDownloadView();SyncTimer();
        }
        catch(OperationCanceledException)when(reliabilityCancellation.IsCancellationRequested){}
        catch(Exception error)
        {
            if(!closing&&!closed){ErrorLog.Write(error);Status.Text="Не удалось проверить загрузки: "+error.Message;}
        }
        finally{reliabilityPollBusy=false;}
    }
    async void RequestDownloadRecovery(bool force)
    {
        if(closing||closed)return;
        recoveryPending=true;recoveryForced|=force;
        if(recoveryBusy)return;recoveryBusy=true;
        try
        {
            while(recoveryPending&&!closing&&!closed)
            {
                recoveryPending=false;var refreshConnections=recoveryForced;recoveryForced=false;
                await downloads.RecoverConnectionsAsync(DownloadRecoveryMonitor.NetworkAvailable,refreshConnections,reliabilityCancellation.Token,prefs.AutoRecoverDownloads);
                if(closing||closed)return;
                downloads.Update();RefreshDownloadView();SyncTimer();
            }
        }
        catch(OperationCanceledException)when(reliabilityCancellation.IsCancellationRequested){}
        catch(Exception error)
        {
            if(!closing&&!closed){ErrorLog.Write(error);Status.Text="Не удалось восстановить подключения: "+error.Message;}
        }
        finally{recoveryBusy=false;}
    }
    void StopDownloadReliability()
    {
        reliabilityCancellation.Cancel();downloadRecoveryMonitor?.Dispose();downloadRecoveryMonitor=null;
        downloads.Notice-=DownloadNoticeReceived;downloadNotifications?.Dispose();downloadNotifications=null;
    }
}

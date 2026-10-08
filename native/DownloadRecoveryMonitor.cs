using System.Net.NetworkInformation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Kachalka;

// Windows can emit several adapter events during one reconnect. Wait for them
// to settle before asking the torrent service to refresh active connections.
public sealed class DownloadRecoveryMonitor : IDisposable
{
    readonly Dispatcher dispatcher;
    readonly DispatcherTimer debounce;
    readonly Action<bool> changed;
    bool forced,disposed;
    public DownloadRecoveryMonitor(Dispatcher dispatcher,Action<bool> changed)
    {
        this.dispatcher=dispatcher;this.changed=changed;
        debounce=new DispatcherTimer(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromSeconds(2)};
        debounce.Tick+=OnTick;
        NetworkChange.NetworkAvailabilityChanged+=AvailabilityChanged;
        NetworkChange.NetworkAddressChanged+=AddressChanged;
        SystemEvents.PowerModeChanged+=PowerChanged;
    }
    public static bool NetworkAvailable
    {
        get{try{return NetworkInterface.GetIsNetworkAvailable();}catch(NetworkInformationException){return true;}}
    }
    void AvailabilityChanged(object? sender,NetworkAvailabilityEventArgs e)=>Schedule(false);
    void AddressChanged(object? sender,EventArgs e)=>Schedule(true);
    void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume)Schedule(true);}
    void Schedule(bool force)
    {
        if(dispatcher.HasShutdownStarted)return;
        _=dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            if(disposed)return;
            forced|=force;debounce.Stop();debounce.Start();
        }));
    }
    void OnTick(object? sender,EventArgs e)
    {
        debounce.Stop();var force=forced;forced=false;if(!disposed)changed(force);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;debounce.Stop();debounce.Tick-=OnTick;
        NetworkChange.NetworkAvailabilityChanged-=AvailabilityChanged;
        NetworkChange.NetworkAddressChanged-=AddressChanged;
        SystemEvents.PowerModeChanged-=PowerChanged;
    }
}

using Kachalka;
using MonoTorrent.Client;

static class DownloadDiagnosticsTests
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var now=new DateTime(2026,10,8,12,0,0,DateTimeKind.Utc);
        var waiting=new DownloadDiagnosticSnapshot{CapturedUtc=now,State=TorrentState.Downloading,ManagerAvailable=true,HasMetadata=true,StartedUtc=now.AddMinutes(-2),NetworkAvailable=true};
        string Reason(DownloadDiagnosticSnapshot s)=>DownloadDiagnostics.Explain(s).ReasonCode;
        Check(Reason(waiting)=="no-peers","diagnostics identify missing connected peers without trusting advertised seed counts");
        var unknown=waiting with{NetworkAvailable=null};
        Check(Reason(unknown)=="no-peers"&&!DownloadDiagnostics.NetworkFact(null).Contains("нет подключения",StringComparison.OrdinalIgnoreCase),"unknown adapter status is not reported as an outage");
        Check(Reason(waiting with{NetworkAvailable=false})=="network-offline","an observed unavailable adapter produces a network reason");
        Check(Reason(waiting with{Paused=true,NetworkAvailable=false})=="paused"&&!DownloadDiagnostics.Explain(waiting with{Paused=true}).CanRetryConnections,"manual pause has priority over an outage and cannot reconnect itself");
        Check(Reason(waiting with{Completed=true,State=TorrentState.Seeding,NetworkAvailable=false})=="complete","completed torrent does not pretend that zero download speed is an error");
        Check(Reason(waiting with{State=TorrentState.Hashing,NetworkAvailable=false})=="checking-files","local file verification is distinguished from a network wait");
        Check(Reason(waiting with{DownloadRate=3000,NetworkAvailable=false})=="receiving","actual payload traffic outranks a stale adapter observation");
        Check(Reason(waiting with{LastPayloadUtc=now.AddSeconds(-4),DownloadRate=0})=="receiving","a brief zero-rate interval after payload is not classified as a stall");
        Check(Reason(waiting with{LastPayloadUtc=now.AddMinutes(-1),Connections=3})=="no-payload","connected peers without recent payload are explained separately");
        Check(Reason(waiting with{StartedUtc=now.AddSeconds(-3)})=="connecting"&&!DownloadDiagnostics.Explain(waiting with{StartedUtc=now.AddSeconds(-3)}).CanRetryConnections,"new connections get time to initialize before suggesting retries");
        Check(Reason(waiting with{HasMetadata=false,State=TorrentState.Metadata})=="metadata","magnet metadata waiting is distinguished from payload downloading");
        Check(Reason(waiting with{ManagerAvailable=false})=="preparing"&&!DownloadDiagnostics.Explain(waiting with{ManagerAvailable=false}).CanRetryConnections,"an absent manager does not invent observations or offer an impossible connection retry");
        var failed=waiting with{Trackers=[new("tracker.example","udp","Offline",Failed:true),new("tracker2.example","https","InvalidResponse",Failed:true)]};
        var failure=DownloadDiagnostics.Explain(failed);
        Check(failure.ReasonCode=="trackers-unavailable"&&failure.CanRetryConnections&&!failure.Explanation.Contains("Россия")&&!failure.Explanation.Contains("брандмауэр"),"actual tracker failures suggest retry without assigning country or firewall blame");
        Check(Reason(failed with{Connections=2})=="no-payload","tracker failures cannot override actual peer connections");
        Check(Reason(failed with{Trackers=[new("tracker.example","udp","Unknown")]})=="no-peers","pending tracker responses are not counted as failures");
        Check(Reason(failed with{Trackers=[new("tracker.example","udp","Ok",Responded:true)]})=="no-peers","an answering tracker does not prove that a payload peer is available");
        Check(Reason(waiting with{SpaceCheck=new(100,500,200,44),Paused=true,LowSpacePaused=true})=="disk-space","known lack of queue disk budget has priority over automatic safety pause");
        var diskUnknown=waiting with{SpaceCheck=new(null,0,0,0,"Папка недоступна"),LowSpacePaused=true};
        Check(Reason(diskUnknown)=="disk-unavailable"&&!DownloadDiagnostics.Explain(diskUnknown).Explanation.Contains("заполн"),"an inaccessible disk is not misreported as a full disk");
        Check(DownloadDiagnostics.SpaceFact(waiting with{SpaceCheck=new(1000,700,300,100)}).Contains("запаса"),"space facts describe the queue and reserve rather than only the current file");
        Check(Reason(waiting with{State=TorrentState.Error,Error="Ошибка чтения файла"})=="engine-error","engine errors remain actionable even if network is present");
        Check(!DownloadDiagnostics.Explain(waiting with{State=TorrentState.Error,NetworkAvailable=false}).CanRetryConnections,"an offline engine error does not offer a retry that cannot run");
        var folder=Path.Combine(Path.GetTempPath(),"diagnostic-secret-user");
        var item=new DownloadItem{Folder=folder,MediaTitle="Проверка диагностики",ReleaseSource="Тест",Source="magnet:?xt=private-source"};
        var report=DownloadDiagnostics.CreateReport(waiting with{State=TorrentState.Error,Error=folder+" token=do-not-share",Trackers=[new("tracker.example","https","Offline",Failed:true)]},item,new Preferences{Folder=folder});
        Check(!report.Contains(folder)&&!report.Contains("do-not-share")&&!report.Contains("private-source")&&report.Contains("[скрыто]")&&report.Contains("tracker.example"),"copyable diagnostics redact private paths and tokens while retaining tracker host observations");
    }
}

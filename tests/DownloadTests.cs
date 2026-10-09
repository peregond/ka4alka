using Kachalka;
using MonoTorrent.Client;

static class DownloadTests
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var start=new DateTime(2026,9,29,10,0,0,DateTimeKind.Utc);
        var meter=new PayloadMeter();
        meter.Sample(start,start,0);
        Check(meter.Sample(start.AddSeconds(2),start,0)==0,"no payload means zero file speed");
        Check(meter.Sample(start.AddSeconds(4),start,4096)==1024,"payload rate uses actual elapsed time");
        meter.Sample(start.AddSeconds(6),start,4096);meter.Sample(start.AddSeconds(8),start,4096);
        meter.Sample(start.AddSeconds(10),start,4096);
        Check(meter.Sample(start.AddSeconds(12),start,4096)==0,"displayed speed decays when data stops");
        Check(meter.LastPayloadUtc==start.AddSeconds(4),"waiting timer follows last received payload");
        Check(meter.Sample(start.AddSeconds(14),start.AddSeconds(14),4096)==0,"resume discards old speed");
        meter.Sample(start.AddSeconds(16),start.AddSeconds(14),8192);
        Check(meter.Sample(start.AddSeconds(18),start.AddSeconds(14),0)==0,"engine counter reset never creates negative speed");
        Check(meter.Sample(start.AddSeconds(40),start.AddSeconds(14),9000)==0,"long sampling gaps do not show stale speed");

        var now=start.AddMinutes(1);var item=new DownloadItem();
        var snapshot=new DownloadSnapshot(TorrentState.Downloading,false,50,1024*1024,0,0,0,0,start,null);
        void Apply(DownloadSnapshot s)=>DownloadPresentation.Apply(item,s,now);
        Apply(snapshot);Check(item.Status=="Ожидание участников"&&item.Hint.Contains("никто не подключён"),"missing peers explained without claiming zero seeds in swarm");
        Apply(snapshot with{Connections=3});Check(item.Status=="Ожидание данных"&&item.Hint.Contains("не передают"),"connected but silent peers distinguished from downloading");
        Apply(snapshot with{Connections=3,DownloadRate=1024,LastPayloadUtc=now});
        Check(item.Status=="Скачивание"&&item.Remaining.Contains("мин")&&item.Hint=="","file transfer has ETA and clears waiting warning");
        Check(item.DownloadRate==1024&&item.UploadRate==0&&item.TotalBytes==1024*1024,"active file telemetry supplies numeric speed and size sort keys");
        Apply(snapshot with{State=TorrentState.Metadata,DownloadRate=10000});
        Check(item.Indeterminate&&item.Remaining==""&&!item.Stats.Contains("/с")&&!item.Stats.Contains("50"),"metadata does not pretend to transfer movie bytes");
        Apply(snapshot with{State=TorrentState.Metadata,Trackers=new TrackerHealth(3,0,3)});
        Check(item.Hint.Contains("Трекеры пока недоступны")&&!item.Hint.Contains("нет сидов"),"unavailable trackers distinguished from an empty swarm without inventing a regional block");
        Apply(snapshot with{State=TorrentState.Metadata,Trackers=new TrackerHealth(3,1,1)});
        Check(item.Hint.Contains("ответили 1 из 3")&&item.Hint.Contains("устаревшим"),"healthy tracker response with no connections explains why indexed seed counts are not a transfer guarantee");
        Apply(snapshot with{State=TorrentState.Metadata,Connections=1,Trackers=new TrackerHealth(3,0,3)});
        Check(!item.Hint.Contains("недоступны"),"a connected metadata peer clears the tracker waiting warning");
        Check(item.DownloadRate==0&&item.UploadRate==0,"metadata traffic does not contribute to download sorting speed");
        Apply(snapshot with{State=TorrentState.Hashing,DownloadRate=10000});
        Check(item.Status=="Проверка файлов"&&item.PeersText==""&&!item.Stats.Contains("/с"),"verification has no fake network rate");
        Check(item.DownloadRate==0&&item.UploadRate==0,"file verification clears both displayed sorting rates");
        Apply(snapshot with{Paused=true,DownloadRate=10000,UploadRate=10000});
        Check(item.Status=="На паузе"&&!item.Indeterminate&&item.Remaining==""&&item.PeersText==""&&!item.Stats.Contains("/с"),"paused tasks clear transient telemetry");
        Check(item.DownloadRate==0&&item.UploadRate==0,"paused tasks cannot rank by stale speed");
        Apply(snapshot with{State=TorrentState.Seeding,Progress=100,DownloadRate=10000,UploadRate=1024});
        Check(item.Status=="Готово · раздаётся"&&!item.Stats.Contains("↓")&&item.Stats.Contains("↑")&&item.Remaining=="","completed task shows upload only");
        Check(item.DownloadRate==0&&item.UploadRate==1024,"seeding speed remains available for queue sorting");
        Apply(snapshot with{State=TorrentState.Error,Error="Недостаточно места на диске"});
        Check(item.Hint=="Недостаточно места на диске"&&item.Remaining=="","engine error reason reaches download card");
        Check(item.DownloadRate==0&&item.UploadRate==0,"failed tasks do not retain a previous transfer speed");
        Apply(snapshot with{Progress=double.NaN,TotalBytes=null,DownloadRate=long.MaxValue});
        Check(item.Progress==0&&item.Remaining=="","unknown total and invalid progress never produce an ETA");
        Check(item.TotalBytes==null,"an unknown download size clears its stale numeric sort value");
    }
}

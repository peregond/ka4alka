using MonoTorrent.Client;
using MonoTorrent.Trackers;

namespace Kachalka;

// Only file payload contributes to the displayed rate. Tracker and handshake traffic do not.
public sealed class PayloadMeter
{
    readonly Queue<(DateTime Time,long Bytes)> samples=[];
    DateTime started;
    public DateTime? LastPayloadUtc {get;private set;}
    public long Rate {get;private set;}
    public long Sample(DateTime now,DateTime start,long bytes)
    {
        bytes=Math.Max(0,bytes);
        if(samples.Count==0||started!=start||bytes<samples.Last().Bytes||now-samples.Last().Time>TimeSpan.FromSeconds(12))
        {
            samples.Clear();samples.Enqueue((now,bytes));started=start;LastPayloadUtc=null;Rate=0;return Rate;
        }
        var previous=samples.Last();
        if(now<=previous.Time)return Rate;
        if(bytes>previous.Bytes)LastPayloadUtc=now;
        samples.Enqueue((now,bytes));
        while(samples.Count>2&&now-samples.Peek().Time>TimeSpan.FromSeconds(8))samples.Dequeue();
        var first=samples.Peek();var seconds=(now-first.Time).TotalSeconds;
        if(seconds>=1)Rate=(long)((bytes-first.Bytes)/seconds);
        return Rate;
    }
}

public record TrackerHealth(int Total,int Responded,int Failed)
{
    public static TrackerHealth From(TorrentManager manager)
    {
        var trackers=manager.TrackerManager.Tiers.SelectMany(t=>t.Trackers).ToArray();
        return new(trackers.Length,trackers.Count(t=>t.Status==TrackerState.Ok),trackers.Count(t=>t.Status is TrackerState.Offline or TrackerState.InvalidResponse));
    }
    public string Summary=>Total==0?"":$"Трекеры: ответили {Responded} из {Total}. ";
    public string WaitingHint=>Total>0&&Failed==Total
        ?"Трекеры пока недоступны. Проверь подключение к сети или попробуй другую раздачу."
        :Summary+"Сейчас никто не подключён к этой раздаче. Число отдающих в поиске может быть устаревшим. Если ожидание затянется, попробуй другой вариант.";
}

public record DownloadSnapshot(TorrentState State,bool Paused,double Progress,long? TotalBytes,long DownloadRate,long UploadRate,int Connections,int Seeds,DateTime StartedUtc,DateTime? LastPayloadUtc,string? Error=null,TrackerHealth? Trackers=null);

public static class DownloadPresentation
{
    public static void Apply(DownloadItem item,DownloadSnapshot s,DateTime now)
    {
        var progress=double.IsFinite(s.Progress)?Math.Clamp(s.Progress,0,100):0;
        var total=s.TotalBytes.HasValue?Math.Max(0,s.TotalBytes.Value):(long?)null;
        var transferred=total.HasValue?(long)(total.Value*(progress/100)):0;
        var idle=now-(s.LastPayloadUtc??s.StartedUtc)>TimeSpan.FromSeconds(20);
        var down=s.State==TorrentState.Downloading&&!s.Paused?Math.Max(0,s.DownloadRate):0;
        var up=!s.Paused&&(s.State is TorrentState.Downloading or TorrentState.Seeding)?Math.Max(0,s.UploadRate):0;
        item.Progress=progress;item.DownloadRate=down;item.UploadRate=up;item.TotalBytes=total;item.Remaining="";item.Hint="";
        item.Indeterminate=!s.Paused&&(s.State is TorrentState.Metadata or TorrentState.Hashing);
        item.PeersText=s.Paused?"":$"Подключено: {Math.Max(0,s.Connections)} · отдают: {Math.Max(0,s.Seeds)}";
        var volume=total.HasValue?$"{DownloadService.FormatBytes(transferred)} из {DownloadService.FormatBytes(total.Value)}":"Размер станет известен после получения метаданных";
        if(s.Paused){item.Status="На паузе";item.Stats=$"{progress:F1}% · {volume}";return;}
        switch(s.State)
        {
            case TorrentState.Metadata:
                item.Status=idle&&s.Connections==0?"Ожидание участников":"Получение метаданных…";
                item.Stats="Получаем список файлов и размер раздачи";
                item.Hint=idle&&s.Connections==0?(s.Trackers?.WaitingHint??MissingPeers):"Загрузка файлов начнётся после получения метаданных magnet-ссылки.";
                break;
            case TorrentState.Hashing:
                item.Status="Проверка файлов";item.Stats="Проверяем уже сохранённые данные";
                item.PeersText="";item.Hint="После проверки загрузка продолжится с недостающих частей.";
                break;
            case TorrentState.Downloading:
                item.Status=down>0?"Скачивание":idle?(s.Connections==0?"Ожидание участников":"Ожидание данных"):"Подключение к участникам…";
                item.Stats=$"{progress:F1}% · {volume} · ↓ {DownloadService.FormatBytes(down)}/с · ↑ {DownloadService.FormatBytes(up)}/с";
                if(idle&&down==0)item.Hint=s.Connections==0?(s.Trackers?.WaitingHint??MissingPeers):"Участники подключены, но пока не передают файлы. Если ожидание затянется, попробуй раздачу, где больше отдающих.";
                if(down>0&&total.HasValue&&progress<100)item.Remaining=Eta((total.Value-transferred)/(double)down);
                break;
            case TorrentState.Seeding:
                item.Status="Готово · раздаётся";item.Stats=$"{volume} · ↑ {DownloadService.FormatBytes(up)}/с";
                break;
            case TorrentState.Error:
                item.Status="Ошибка загрузки";item.Stats=volume;item.PeersText="";
                item.Hint=string.IsNullOrWhiteSpace(s.Error)?"Попробуй поставить загрузку на паузу и продолжить.":s.Error;
                break;
            default:
                item.Status="Подключение…";item.Stats=volume;item.PeersText="";
                break;
        }
    }
    const string MissingPeers="Сейчас никто не подключён к этой раздаче. Попробуй вариант, где больше отдающих; скорость зависит от участников, которые раздают файлы.";
    static string Eta(double seconds)
    {
        if(!double.IsFinite(seconds)||seconds<=0)return "";
        if(seconds<60)return "Осталось меньше минуты";
        if(seconds<3600)return $"Осталось ≈ {Math.Ceiling(seconds/60):F0} мин";
        if(seconds<86400)return $"Осталось ≈ {seconds/3600:F1} ч";
        return $"Осталось ≈ {Math.Ceiling(seconds/86400):F0} дн";
    }
}

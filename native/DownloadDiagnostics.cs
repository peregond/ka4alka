using System.Text;
using MonoTorrent.Client;

namespace Kachalka;

// These are observations made by the torrent engine, not guesses based on the
// source's advertised seed count or the user's country.
public sealed record DownloadTrackerDiagnostic(string Host,string Protocol,string Status,bool Responded=false,bool Failed=false);

public sealed record DownloadDiagnosticSnapshot
{
    public string DownloadId {get;init;}="";
    public string Name {get;init;}="";
    public DateTime CapturedUtc {get;init;}=DateTime.UtcNow;
    public TorrentState State {get;init;}=TorrentState.Stopped;
    public bool ManagerAvailable {get;init;}
    public bool Paused {get;init;}
    public bool LowSpacePaused {get;init;}
    public bool HasMetadata {get;init;}
    public bool Completed {get;init;}
    public bool? NetworkAvailable {get;init;}
    public int Connections {get;init;}
    public int Seeds {get;init;}
    public long DownloadRate {get;init;}
    public DateTime StartedUtc {get;init;}
    public DateTime? LastPayloadUtc {get;init;}
    public long? RemainingBytes {get;init;}
    public long? FreeBytes {get;init;}
    public DownloadSpaceCheck? SpaceCheck {get;init;}
    public string? Error {get;init;}
    public bool? DhtEnabled {get;init;}
    public IReadOnlyList<DownloadTrackerDiagnostic> Trackers {get;init;}=[];
}

public enum DownloadDiagnosticLevel {Healthy,Waiting,Attention}
public sealed record DownloadDiagnosis(string ReasonCode,string Title,string Explanation,string Recommendation,DownloadDiagnosticLevel Level,bool CanRetryConnections=false);

public static class DownloadDiagnostics
{
    public static DownloadDiagnosis Explain(DownloadDiagnosticSnapshot snapshot)
    {
        var s=snapshot;
        DownloadDiagnosis Result(string code,string title,string explanation,string action,DownloadDiagnosticLevel level=DownloadDiagnosticLevel.Waiting,bool retry=false)
            =>new(code,title,explanation,action,level,retry&&!s.Paused&&!s.Completed&&s.NetworkAvailable!=false&&s.ManagerAvailable);
        if(s.SpaceCheck is {Error:not null} unavailable)
            return Result("disk-unavailable","Не удалось проверить папку загрузки","Не можем определить свободное место: "+unavailable.Error,"Проверь подключение диска и права доступа к папке. Затем нажми «Продолжить»; сообщение о недоступной папке не означает, что диск заполнен.",DownloadDiagnosticLevel.Attention);
        if(s.LowSpacePaused||s.SpaceCheck is {AvailableBytes:not null,HasEnoughSpace:false})
            return Result("disk-space","Недостаточно свободного места","На последней проверке места не хватило, поэтому загрузка приостановлена. Учитываем оставшиеся файлы, другие активные загрузки и запас места.","Освободи место в папке загрузок и нажми «Продолжить»: место проверится заново. Уже полученные части сохранятся.",DownloadDiagnosticLevel.Attention);
        if(s.Paused)
            return Result("paused","Загрузка на паузе","Эта задача остановлена. Переподключение к сети не возобновляет задачи, которые ты поставил на паузу.","Нажми «Продолжить», когда захочешь возобновить загрузку.");
        if(s.Completed||s.State==TorrentState.Seeding)
            return Result("complete","Файлы уже скачаны","Загрузка завершена. Задача может отдавать файлы другим участникам.","Готовые файлы доступны в папке загрузки.",DownloadDiagnosticLevel.Healthy);
        if(s.State is TorrentState.Hashing or TorrentState.HashingPaused)
            return Result("checking-files","Проверяем сохранённые файлы","Качалка проверяет уже полученные части. Во время проверки скорость скачивания может быть нулевой.","Дождись окончания проверки: повторно скачиваться будут только недостающие части.");
        if(s.State==TorrentState.Downloading&&(s.DownloadRate>0||s.LastPayloadUtc.HasValue&&s.CapturedUtc-s.LastPayloadUtc.Value<TimeSpan.FromSeconds(15)))
            return Result("receiving","Загрузка работает","От участников поступают данные файлов. Скорость зависит от раздачи и установленного ограничения.","Можно продолжать пользоваться приложением: файлы скачиваются в фоне.",DownloadDiagnosticLevel.Healthy);
        if(s.State==TorrentState.Error||!string.IsNullOrWhiteSpace(s.Error))
            return Result("engine-error","Ошибка загрузки",string.IsNullOrWhiteSpace(s.Error)?"Торрент-модуль сообщил об ошибке.":s.Error,"Проверь доступность папки загрузок. После устранения причины нажми «Повторить соединение»; если ошибка повторится, скопируй диагностику.",DownloadDiagnosticLevel.Attention,true);
        if(s.NetworkAvailable==false)
            return Result("network-offline","Нет подключения к сети","Windows сейчас не видит подключённого сетевого адаптера. Новые соединения с участниками недоступны.","Подключись к Wi-Fi или кабельной сети. Активные загрузки восстановят соединения после возвращения сети.",DownloadDiagnosticLevel.Attention);
        if(!s.ManagerAvailable)
            return Result("preparing","Подготавливаем загрузку","Торрент-модуль ещё не запущен для этой задачи. По этим данным нельзя определить доступность участников.","Подожди несколько секунд. Если состояние не меняется, закрой диагностику, поставь задачу на паузу и продолжи её.");
        var waitingSince=s.LastPayloadUtc??s.StartedUtc;
        var waiting=waitingSince!=default&&s.CapturedUtc-waitingSince>=TimeSpan.FromSeconds(30);
        var failed=s.Trackers.Count(t=>t.Failed);
        var allFailed=s.Trackers.Count>0&&failed==s.Trackers.Count;
        var trackerNote=allFailed?" Ни один из настроенных трекеров пока не ответил. Это само по себе не доказывает блокировку."+(s.DhtEnabled==false?" DHT выключен.":" Участники также могут находиться через DHT."):"";
        if(!s.HasMetadata||s.State==TorrentState.Metadata)
            return Result("metadata","Получаем список файлов",s.Connections>0?"Участники подключены, но ещё не передали метаданные magnet-ссылки. Пока не известны файлы и точный размер раздачи.":"Для magnet-ссылки сначала нужно получить список файлов от участника раздачи."+trackerNote,
                waiting?"Повтори соединение. Если ожидание затянется, выбери другую раздачу или .torrent-файл с готовым списком файлов.":"Дай участникам время подключиться. После получения списка файлов начнётся скачивание.",retry:waiting);
        if(allFailed&&s.Connections==0&&waiting)
            return Result("trackers-unavailable","Трекеры пока не отвечают","Все настроенные трекеры сейчас сообщают об отсутствии ответа или ошибке. Подключённых участников пока нет. Доступность всего интернета по этому результату не определена.","Проверь, открываются ли сайты в браузере, и повтори соединение. Если другая раздача скачивается, выбери другой вариант этого фильма.",DownloadDiagnosticLevel.Attention,true);
        if(s.Connections==0)
            return Result(waiting?"no-peers":"connecting",waiting?"Нет подключённых участников":"Ищем участников",waiting?"К этой задаче сейчас никто не подключён. Число отдающих в поиске может быть устаревшим: оно не подтверждает доступность раздачи.":"Трекеры и DHT ищут участников раздачи. В первые секунды отсутствие скорости нормально.",
                waiting?"Повтори соединение или выбери другую раздачу с доступными участниками.":"Подожди немного: поиск участников выполняется в фоне.",retry:waiting);
        return Result(waiting?"no-payload":"waiting-data",waiting?"Участники не передают файлы":"Ожидаем данные",$"Подключено участников: {Math.Max(0,s.Connections)}. "+(waiting?"Данные файлов не поступают уже некоторое время. Участники могут не иметь нужных частей или ограничивать отдачу.":"Соединения установлены; ждём первые части файлов."),
            waiting?"Повтори соединение. Если скорость не появится, выбери другую раздачу; количество подключений не гарантирует передачу файлов.":"Дай участникам время начать передачу.",retry:waiting);
    }

    public static string NetworkFact(bool? available)=>available switch
    {
        true=>"Сетевой адаптер подключён. Доступ в интернет отдельно не подтверждён.",
        false=>"Windows не видит подключения к сети.",
        null=>"Состояние сети пока неизвестно."
    };
    public static string TrackerFact(DownloadTrackerDiagnostic tracker)=>tracker.Responded?"Ответ получен":tracker.Failed?"Нет ответа или ошибка":"Ответ пока не получен";
    public static string SpaceFact(DownloadDiagnosticSnapshot snapshot)
    {
        if(snapshot.SpaceCheck is { } space)
        {
            if(space.AvailableBytes is not { } free)return "Свободное место не удалось определить. "+space.Error;
            return $"При последней проверке свободно {DownloadService.FormatBytes(Math.Max(0,free))}. Нужно {DownloadService.FormatBytes(Math.Max(0,space.RequiredBytes))} с учётом активной очереди и запаса.";
        }
        return snapshot.FreeBytes.HasValue?$"Свободно {DownloadService.FormatBytes(Math.Max(0,snapshot.FreeBytes.Value))}. Размер очереди пока уточняется.":"Проверка будет доступна после получения размеров файлов.";
    }
    public static string CreateReport(DownloadDiagnosticSnapshot snapshot,DownloadItem item,Preferences preferences)
    {
        var diagnosis=Explain(snapshot);var text=new StringBuilder();
        text.AppendLine("Диагностика загрузки · Качалка");
        text.AppendLine($"Версия: {typeof(Preferences).Assembly.GetName().Version?.ToString(3)} · UTC: {snapshot.CapturedUtc:O}");
        text.AppendLine($"Система: {Environment.OSVersion.VersionString}");
        text.AppendLine($"Название: {item.DisplayName}");text.AppendLine($"Источник: {item.ReleaseSource??"Не указан"}");
        text.AppendLine($"Результат: {diagnosis.Title} ({diagnosis.ReasonCode})");text.AppendLine(diagnosis.Explanation);text.AppendLine(diagnosis.Recommendation);
        text.AppendLine($"Состояние модуля: {(snapshot.ManagerAvailable?snapshot.State.ToString():"Не запущен")}; пауза: {snapshot.Paused}; метаданные: {snapshot.HasMetadata}");
        text.AppendLine("Сеть: "+NetworkFact(snapshot.NetworkAvailable));
        text.AppendLine($"Подключено: {Math.Max(0,snapshot.Connections)}; отдают: {Math.Max(0,snapshot.Seeds)}; скорость: {Math.Max(0,snapshot.DownloadRate)} Б/с");
        text.AppendLine($"Последние данные: {snapshot.LastPayloadUtc?.ToString("O")??"Пока не получены"}; DHT: {snapshot.DhtEnabled?.ToString()??"Неизвестно"}");
        text.AppendLine("Диск: "+SpaceFact(snapshot));
        foreach(var tracker in snapshot.Trackers)text.AppendLine($"Трекер: {tracker.Protocol}://{tracker.Host} · {TrackerFact(tracker)} ({tracker.Status})");
        // Tracker URLs, magnet links, local file names, passkeys and peer IPs are
        // deliberately omitted. Redact error messages which can contain paths.
        return DiagnosticReport.Redact(text.ToString(),new[]{item.Folder,preferences.Folder,Preferences.DataDir,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)});
    }
}

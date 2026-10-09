using System.Globalization;

namespace Kachalka;

public static class ReleaseFreshness
{
    public const string ConnectionNote="Участники — по данным источника. Подключение проверяется при скачивании.";
    public const string AudioNote="Озвучка определена по названию раздачи. Аудиодорожки ещё не проверены.";

    // A cache envelope can be rewritten after a partial response. Only the row's
    // original receipt time can tell us how old its advertised details are.
    public static DateTime? ValidUtc(DateTime? value,DateTime now)
    {
        if(value is not DateTime time||time.Year<2000||time>now.AddMinutes(5))return null;
        return DateTime.SpecifyKind(time,DateTimeKind.Utc);
    }
    public static string Age(DateTime? receivedUtc,DateTime now)
    {
        if(ValidUtc(receivedUtc,now) is not DateTime received)return "время неизвестно";
        var elapsed=now-received;
        if(elapsed<TimeSpan.FromMinutes(1))return "сейчас";
        if(elapsed<TimeSpan.FromHours(1))return $"{(int)elapsed.TotalMinutes} мин назад";
        if(elapsed<TimeSpan.FromDays(1))return $"{(int)elapsed.TotalHours} ч назад";
        return received.ToLocalTime().ToString("dd.MM.yyyy",CultureInfo.InvariantCulture);
    }
    public static string SourceName(SourceEntry entry)=>entry.Source+(string.IsNullOrWhiteSpace(entry.Via)?"":" · через "+entry.Via);
    public static bool FromIndex(SourceEntry entry)=>entry.DataProvider=="Онлайн-индекс";
    public static string Caption(SourceEntry entry,DateTime now)=>SourceName(entry)+(FromIndex(entry)?" · индекс":"")+" · "+Age(entry.DataReceivedUtc,now);
    public static string Details(SourceEntry entry,DateTime now)
    {
        var origin=FromIndex(entry)?"Данные онлайн-индекса":"Данные источника";
        var received=ValidUtc(entry.DataReceivedUtc,now);
        var upstream=FromIndex(entry)||!string.IsNullOrWhiteSpace(entry.Via)?" Дата обновления на самом трекере неизвестна.":"";
        return origin+(received.HasValue?" получены "+received.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture):": время получения неизвестно")+"."+upstream+" "+ConnectionNote;
    }
    public static string Audio(SourceEntry entry)
    {
        if(RussianAudio.Rank(entry)!=3)return entry.Voice;
        return entry.Voice switch
        {
            "Дубляж"=>"Русская · дубляж",
            "Многоголосая"=>"Русская · многоголосая",
            "Оригинал"=>"Русская / оригинал",
            _=>"Русская"
        };
    }
    public static string SourceStateText(SourceCheck check,DateTime now)
    {
        var state=check.State switch
        {
            SourceState.Searching=>"Проверяем…",
            SourceState.Ready=>$"Найдено: {check.Count}",
            SourceState.Empty=>"Подходящих вариантов нет",
            SourceState.TimedOut=>"Не ответил вовремя",
            SourceState.Unavailable=>"Временно недоступен",
            SourceState.Indexed=>$"В индексе: {check.Count} · без прямого запроса к трекеру",
            SourceState.Saved=>$"Сохранено: {check.Count}",
            _=>""
        };
        // A failed request has a check time but must never acquire a fresh success time.
        if(check.State is SourceState.Ready or SourceState.Empty)
            return state+" · ответ "+Age(check.CheckedUtc,now);
        if(check.State is SourceState.TimedOut or SourceState.Unavailable&&ValidUtc(check.CheckedUtc,now).HasValue)
            state+=" · попытка "+Age(check.CheckedUtc,now);
        if(ValidUtc(check.LastSuccessUtc,now).HasValue)
            return state+" · успешный ответ "+Age(check.LastSuccessUtc,now);
        return state;
    }
}

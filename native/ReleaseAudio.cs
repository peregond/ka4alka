using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

namespace Kachalka;

/// <summary>Labels advertised by the release title, not inspection of downloaded audio tracks.</summary>
public static class ReleaseAudio
{
    static Regex Token(string pattern)=>new(@"(?<![\p{L}\p{N}])(?:"+pattern+@")(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly (string Label,Regex Pattern)[] Studios=
    [
        ("LostFilm",Token(@"Lost[ ._-]?Film|Лост[ ._-]?Фильм")),
        ("NewStudio",Token(@"New[ ._-]?Studio|Нью[ ._-]?Студио")),
        ("AlexFilm",Token(@"Alex[ ._-]?Film|Алекс[ ._-]?Фильм")),
        ("Кубик в Кубе",Token(@"Кубик[ ._-]*в[ ._-]*Кубе|Kubik[ ._-]*v[ ._-]*Kube")),
        ("Jaskier",Token(@"Jaskier|Яскиер")),
        ("Flarrow Films",Token(@"Flarrow[ ._-]*Films|Фларроу[ ._-]*Филмс")),
        ("HDrezka",Token(@"HD[ ._-]?rezka(?:[ ._-]?Studio)?|ХДрезка")),
        ("RuDub",Token(@"Ru[ ._-]?Dub|Ру[ ._-]?Даб")),
        ("TVShows",Token(@"TV[ ._-]?Shows")),
        ("LE-Production",Token(@"LE[ ._-]?Production")),
        ("BaibaKo",Token(@"BaibaKo|Байбако")),
        ("Кураж-Бамбей",Token(@"Кураж[ ._-]?Бамбей|Kuraj[ ._-]?Bambey")),
        ("Сыендук",Token(@"Сыендук|Syenduk")),
        ("AniLibria",Token(@"Ani[ ._-]?Libria|Анилибрия")),
        ("AniDUB",Token(@"Ani[ ._-]?Dub|Анидаб")),
        ("Дубляжная",Token(@"Дубляжная")),
        ("Red Head Sound",Token(@"Red[ ._-]*Head[ ._-]*Sound|RHS")),
        ("StudioBand",Token(@"Studio[ ._-]?Band")),
        ("Ultradox",Token(@"Ultradox|Ультрадокс")),
        ("Coldfilm",Token(@"Cold[ ._-]?Film")),
        ("ViruseProject",Token(@"Viruse[ ._-]?Project")),
        ("СВ-Дубль",Token(@"СВ[ ._-]?Дубль")),
        ("AMS",Token(@"AMS")),
        ("Octopus",Token(@"Octopus|Октопус"))
    ];
    static readonly (string Label,Regex Pattern)[] Types=
    [
        ("Дубляж",Token(@"DUB|дубляж|дублирован[\p{L}]*")),
        ("Многоголосая",Token(@"MVO|многоголос[\p{L}]*")),
        ("Двухголосая",Token(@"DVO|двухголос[\p{L}]*")),
        ("Одноголосая",Token(@"AVO|VO|одноголос[\p{L}]*")),
        ("Оригинал",Token(@"ENG|ENGLISH|оригинал|original"))
    ];
    sealed record Labels(string[] Studios,string[] Types);
    // Reference keys keep cloned records with a changed title separate; weak keys do not retain old catalogs.
    static readonly ConditionalWeakTable<SourceEntry,Labels> Cache=new();
    static readonly Regex SubtitlePrefix=new(@"(?i)(?:subtitles?|subs?|субтитры)[\s:._=\-]*(?:\[[^\]\r\n]*\]|\([^\)\r\n]*\)|(?:EN|ENG|ENGLISH|RU|RUS|RUSSIAN|GER|DEU|JPN|FRA)\b|русские\b|английские\b)",RegexOptions.CultureInvariant);
    static readonly Regex SubtitleSuffix=new(@"(?i)(?<![\p{L}\p{N}])(?:EN|ENG|ENGLISH|RU|RUS|RUSSIAN|GER|DEU|JPN|FRA|русские|английские)[\s._\-]+(?:subtitles?|subs?|субтитры)(?![\p{L}\p{N}])",RegexOptions.CultureInvariant);
    public static string AudioTitle(string title)=>SubtitleSuffix.Replace(SubtitlePrefix.Replace(title,""),"");

    static Labels Parse(SourceEntry entry)=>Cache.GetValue(entry,static value=>
    {
        var title=AudioTitle(value.Title);
        return new(Studios.Where(x=>x.Pattern.IsMatch(title)).Select(x=>x.Label).ToArray(),
            Types.Where(x=>x.Pattern.IsMatch(title)).Select(x=>x.Label).ToArray());
    });
    public static IReadOnlyList<string> StudioNames(SourceEntry entry)=>Parse(entry).Studios;
    public static IReadOnlyList<string> TypeNames(SourceEntry entry)=>Parse(entry).Types;
    public static IEnumerable<string> StudioOptions(SourceEntry entry)=>KnownOrUnknown(StudioNames(entry));
    public static IEnumerable<string> TypeOptions(SourceEntry entry)=>KnownOrUnknown(TypeNames(entry));
    static IEnumerable<string> KnownOrUnknown(IReadOnlyList<string> labels)=>labels.Count==0?["Не указано"]:labels;
    public static bool MatchesStudio(SourceEntry entry,string label)=>label=="Все"||StudioOptions(entry).Contains(label);
    public static bool MatchesType(SourceEntry entry,string label)=>label=="Все"||TypeOptions(entry).Contains(label);
    public static string TypeLabel(SourceEntry entry)=>string.Join(" / ",TypeOptions(entry));
    public static string StudioLabel(SourceEntry entry)=>string.Join(" / ",StudioOptions(entry));
}

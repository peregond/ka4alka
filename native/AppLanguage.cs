using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kachalka;

// Language is a presentation preference, never a catalog key or torrent state.
public sealed class LanguageState : INotifyPropertyChanged
{
    public static LanguageState Instance {get;}=new();
    public int Version {get;private set;}
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh(){Version++;PropertyChanged?.Invoke(this,new(nameof(Version)));}
}
public static class AppLanguage
{
    public static string Current {get;private set;}="ru";
    public static string Normalize(string? language)=>language=="en"?"en":"ru";
    public static void Set(string? language)
    {
        var next=Normalize(language);if(next==Current)return;
        Current=next;LanguageState.Instance.Refresh();
    }
    static readonly Lazy<Dictionary<string,string>> English=new(()=>
    {
        using var stream=typeof(AppLanguage).Assembly.GetManifestResourceStream("Kachalka.Localization.en.json")??throw new InvalidOperationException("English interface resources are missing.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??[];
    });
    sealed record Template(string Source,string Translation,string Prefix,Lazy<Regex> Pattern);
    static readonly Regex Placeholder=new(@"\{(\d+)(?:[^{}]*)\}",RegexOptions.CultureInvariant);
    static readonly Lazy<Template[]> Templates=new(()=>English.Value.Where(pair=>Placeholder.IsMatch(pair.Key)).Select(pair=>
    {
        var prefix=pair.Key[..pair.Key.IndexOf('{')];
        return new Template(pair.Key,pair.Value,prefix,new(()=>new Regex("\\A"+Placeholder.Replace(Regex.Escape(pair.Key).Replace(@"\{","{").Replace(@"\}","}"),match=>"(?<p"+match.Groups[1].Value+">.*?)")+"\\z",RegexOptions.Singleline|RegexOptions.CultureInvariant|RegexOptions.NonBacktracking)));
    }).OrderByDescending(row=>row.Prefix.Length).ThenByDescending(row=>row.Source.Length).ToArray());
    static readonly Lazy<KeyValuePair<string,string>[]> Prefixes=new(()=>English.Value.Where(pair=>pair.Key.Length>1&&pair.Key.EndsWith(" ")&&!pair.Key.Contains("{")).OrderByDescending(pair=>pair.Key.Length).ToArray());
    static readonly Regex Units=new(@"(?<=\d)\s*(ГБ|МБ|КБ|ТБ|Б)(?=$|[\s·/])|/с(?=$|[\s·])",RegexOptions.CultureInvariant);
    static string TranslateUnits(string text)=>Units.Replace(text,match=>match.Value.Trim() switch{"ГБ"=>" GB","МБ"=>" MB","КБ"=>" KB","ТБ"=>" TB","Б"=>" B","/с"=>"/s",_=>match.Value});
    public static string T(string? source)
    {
        if(string.IsNullOrEmpty(source)||Current!="en")return source??"";
        if(!source.Any(c=>c is >= 'А' and <= 'я' or 'Ё' or 'ё'))return source;
        if(English.Value.TryGetValue(source,out var exact))return exact;
        // No broad word replacement: a movie name, path, studio or biography
        // inside a format argument must stay exactly as supplied by its source.
        foreach(var template in Templates.Value)
        {
            if(!source.StartsWith(template.Prefix,StringComparison.Ordinal))continue;
            var match=template.Pattern.Value.Match(source);if(!match.Success)continue;
            return Placeholder.Replace(template.Translation,part=>TranslateUnits(match.Groups["p"+part.Groups[1].Value].Value));
        }
        foreach(var pair in Prefixes.Value)if(source.StartsWith(pair.Key,StringComparison.Ordinal))return pair.Value+T(source[pair.Key.Length..]);
        if(source.Contains(',')){var parts=source.Split(',',StringSplitOptions.TrimEntries);if(parts.All(English.Value.ContainsKey))return string.Join(", ",parts.Select(T));}
        return TranslateUnits(source);
    }
    public static string Genres(string? source)=>Current!="en"?source??"":string.Join(", ",(source??"").Split(',',StringSplitOptions.TrimEntries).Select(T));
    public static IReadOnlyDictionary<string,string> Resources=>English.Value;
}

using System.Globalization;
using System.Text.RegularExpressions;
using Kachalka;

static class LanguageResourceTests
{
    public static void Run()
    {
        void Check(bool valid,string message){if(!valid)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var previous=AppLanguage.Current;var culture=CultureInfo.CurrentCulture;
        var token=new Regex(@"\{(\d+)(?:[^{}]*)\}");
        try
        {
            AppLanguage.Set("ru");Check(AppLanguage.T("Сериалы")=="Сериалы","Russian interface keeps existing canonical labels");
            AppLanguage.Set("en");
            Check(AppLanguage.T("Настройки")=="Settings"&&AppLanguage.T("Файлы не найдены")=="Files not found","English translates settings and protected missing-file status");
            foreach(var pair in AppLanguage.Resources)
            {
                var original=token.Matches(pair.Key).Select(m=>m.Groups[1].Value).Order().ToArray();
                var translated=token.Matches(pair.Value).Select(m=>m.Groups[1].Value).Order().ToArray();
                Check(original.SequenceEqual(translated),"Translation preserves format arguments: "+pair.Key);
                if(original.Length>0)
                {
                    var sample=token.Replace(pair.Key,m=>"arg"+m.Groups[1].Value);
                    var expected=token.Replace(pair.Value,m=>"arg"+m.Groups[1].Value);
                    Check(AppLanguage.T(sample)==expected,"Runtime formats localized template: "+pair.Key);
                }
            }
            Check(AppLanguage.T("Результаты для «Южный парк».")=="Results for “Южный парк”.","Localized search preserves the original user query");
            Check(AppLanguage.T("Файлы загрузки «Сериалы» не найдены. Скачать недостающие файлы заново?").Contains("“Сериалы”"),"Names matching an interface label stay intact inside confirmation messages");
            Check(AppLanguage.T("12,5 МБ/с")=="12,5 MB/s","Transfer units translate without changing numeric data");
            Check(AppLanguage.Genres("драма, комедия")=="drama, comedy","Genre labels translate individually");
            Check(ReferenceEquals(CultureInfo.CurrentCulture,culture),"Interface language never changes the culture used by parsers or persistence");
            AppLanguage.Set("unsupported");Check(AppLanguage.Current=="ru","Unsupported preferences fall back to Russian");
        }
        finally{AppLanguage.Set(previous);}
    }
}

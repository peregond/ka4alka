using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace Kachalka;

// Палитра редизайна 0.40. Обе палитры создаются один раз, все кисти заморожены,
// смена темы только подменяет ссылки на ресурсы. Эффекты и анимации не создаются.
static class InterfacePalette
{
    // Ключ, тёмная тема, светлая тема. Формат #AARRGGBB — цвет с прозрачностью.
    static readonly (string Key,string Dark,string Light)[] tokens=
    [
        // Поверхности
        ("Bg","#0A0C0F","#F3F5F7"),            // фон окна
        ("Sidebar","#0E1115","#FFFFFF"),       // левое меню
        ("Panel","#111419","#FFFFFF"),         // карточки, строки загрузок, секции настроек
        ("PanelAlt","#0C0F13","#F6F8FA"),      // «утопленные» поля: путь к папке, мини-плитки, код сопряжения
        ("Hover","#1C2129","#E9EDF1"),         // наведение на кнопки и строки
        ("Selected","#232932","#E1E6EC"),      // нажатие, активный сегмент, выбранный пункт списка
        // Границы
        ("Edge","#262C35","#CDD4DC"),          // рамки кнопок и полей
        ("EdgeSoft","#1E232A","#E1E6EB"),      // рамки карточек, разделители
        // Текст
        ("Text","#EEF1F4","#12161B"),
        ("Muted","#A6AFBA","#4E5966"),         // вторичный текст
        // Бренд
        ("Accent","#5BE3B5","#0B7A5E"),        // акцентный текст, иконки, активные индикаторы, прогресс
        ("AccentInk","#06140F","#FFFFFF"),     // текст/знак поверх Accent
        ("AccentSoft","#245BE3B5","#1F0B7A5E"),// заливка 14%: бейджи, активный фильтр
        ("Primary","#5BE3B5","#3FD6A5"),       // заливка главной кнопки
        ("PrimaryInk","#06140F","#06140F"),    // текст главной кнопки
        ("PrimaryHover","#7BEBC6","#34C597"),
        // Статусы
        ("Danger","#FF8A7A","#C2362B"),
        ("RatingInk","#F2B35B","#9A5B00"),
        // Кнопки поверх бэкдропа (всегда на тёмном затемнении, поэтому одинаковы в обеих темах)
        ("BannerAction","#14EEF1F4","#14EEF1F4"),

        // ── Новые ключи ──
        ("Raised","#1A1F26","#EEF1F4"),        // вторичные кнопки, чипы внутри карточек, меню
        ("NavSelected","#181C23","#EEF1F4"),   // выбранный пункт левого меню
        ("Subtle","#7F8995","#5F6A77"),        // подписи, заголовки колонок, третичный текст
        ("AccentLine","#595BE3B5","#590B7A5E"),// рамка 35%: рекомендованная раздача, прогресс-кнопка
        ("DangerSoft","#24FF8A7A","#1FC2362B"),
        ("Warning","#F2B35B","#9A5B00"),       // «Ожидание данных», мало сидов (средне)
        ("WarningSoft","#24F2B35B","#1F9A5B00"),
        ("Info","#7FA9FF","#2F62D6"),          // раздача, скорость отдачи
        ("InfoSoft","#247FA9FF","#1F2F62D6"),
        ("BannerEdge","#29EEF1F4","#29EEF1F4"),
        ("Track","#1F242C","#E1E6EB"),         // фон полос прогресса
        ("TrackOff","#2A303A","#CDD4DC"),      // выключенный переключатель, неактивные деления «здоровья»
        ("EdgeStrong","#2C333D","#B9C2CC"),    // рамка при наведении, рамка меню и разделители внутри меню
        ("EdgeFocus","#3A424E","#8D99A8"),     // рамка поля ввода в фокусе
        ("TextSoft","#C9D0D8","#2B3540"),      // текст чипов и вторичных кнопок
    ];

    static readonly IReadOnlyDictionary<string,Brush> dark=Create(false),light=Create(true);

    internal static IReadOnlyDictionary<string,Brush> For(bool isLight)=>isLight?light:dark;

    static IReadOnlyDictionary<string,Brush> Create(bool isLight)
    {
        var result=new Dictionary<string,Brush>();
        foreach(var (key,darkColor,lightColor) in tokens)
        {
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(isLight?lightColor:darkColor));
            brush.Freeze();result[key]=brush;
        }
        // Как и раньше: один сплошной акцент для всех главных действий.
        result["PrimaryFill"]=result["Primary"];
        return result;
    }
}

public partial class MainWindow
{
    internal void ApplyInterfaceTheme(bool light)
    {
        foreach(var (key,brush) in InterfacePalette.For(light))Application.Current.Resources[key]=brush;
    }

    void InitializeInterfacePolish()
    {
        Search.SizeChanged+=(_,_)=>RefreshSearchHint();
        SizeChanged+=(_,_)=>RefreshSearchHint();
        Search.TextChanged+=(_,_)=>RefreshSearchHint();
        Search.IsKeyboardFocusWithinChanged+=(_,_)=>RefreshSearchHint();
        RefreshSearchHint();
    }

    void RefreshSearchHint()
    {
        // Compact navigation prioritizes the search text. The shortcut hint only
        // shows while the field is empty and unfocused; once text is typed the
        // submit and clear actions take its place.
        var empty=Search.Text.Length==0;
        var show=!compactWidth&&empty&&!Search.IsKeyboardFocusWithin&&Search.ActualWidth>=320;
        SearchShortcutHint.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        SearchSubmitButton.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
        ClearSearchButton.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
        var margin=SearchPlaceholder.Margin;
        SearchPlaceholder.Margin=new Thickness(margin.Left,0,show?92:14,0);
    }

    // The window backdrop is the flat Bg surface: no gradient or grain layer sits behind the content.
    object CheckBackgroundTheme()
    {
        var background=(SolidColorBrush)FindResource("Bg");
        if(!ReferenceEquals(Background,background)&&!(Background is SolidColorBrush own&&own.Color==background.Color))throw new Exception("The window must use the flat Bg surface.");
        if(Application.Current.Resources.Contains("AmbientGlow")||Application.Current.Resources.Contains("BackgroundGrain"))throw new Exception("The decorative gradient and grain layers were removed.");
        if(Content is not Grid root||root.Children.OfType<Border>().Any(layer=>layer.Name=="BackgroundGrainLayer"||layer.Background is DrawingBrush or ImageBrush or RadialGradientBrush))throw new Exception("A decorative backdrop layer still sits behind the content.");
        return new{Flat=true,Color=background.Color.ToString()};
    }
}

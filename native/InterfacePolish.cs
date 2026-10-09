using System.Windows;
using System.Windows.Media;

namespace Kachalka;

// Both palettes are created once. Theme changes only replace references to
// immutable brushes; catalog cards do not allocate effects or animated layers.
static class InterfacePalette
{
    static readonly string[] keys=["Bg","Panel","Text","Muted","Edge","Selected","Accent","AccentInk","Sidebar","Hover","Primary","PrimaryInk","PrimaryHover","AccentSoft","PanelAlt","EdgeSoft","Danger","RatingInk","BannerAction"];
    static readonly IReadOnlyDictionary<string,Brush> dark=Create(false),light=Create(true);

    internal static IReadOnlyDictionary<string,Brush> For(bool isLight)=>isLight?light:dark;
    static IReadOnlyDictionary<string,Brush> Create(bool isLight)
    {
        string[] colors=isLight
            ? ["#F3F5FA","#FFFFFF","#172139","#4D5B73","#B5C1D5","#E3EAFB","#3148A8","#FFFFFF","#E9EEF7","#E5ECF8","#4B4ED1","#FFFFFF","#4245C4","#E4EBFF","#FFFFFF","#D6DFED","#AE263E","#805200","#EC1D2844"]
            : ["#0B1020","#151F33","#F8FAFF","#B4BFD4","#4A5D7A","#233356","#B8C4FF","#111B33","#10182A","#20304A","#5854DB","#FFFFFF","#6864E8","#243359","#111B2E","#2A3851","#FFADB9","#FFD166","#EC1D2844"];
        var result=new Dictionary<string,Brush>();
        for(var index=0;index<keys.Length;index++)
        {
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index]));brush.Freeze();result[keys[index]]=brush;
        }
        var primary=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(isLight?"#5548CB":"#6552DB"),(Color)ColorConverter.ConvertFromString(isLight?"#2B60CD":"#2C64D6"),35);
        primary.Freeze();result["PrimaryFill"]=primary;return result;
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
        // Compact navigation prioritizes the search text. A wide text box alone
        // is not enough to expose the shortcut in that reduced shell; it can
        // become wide after the sidebar and header labels collapse.
        var show=!compactWidth&&Search.Text.Length==0&&!Search.IsKeyboardFocusWithin&&Search.ActualWidth>=320;
        SearchShortcutHint.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        var margin=SearchPlaceholder.Margin;
        // Responsive layout still owns the left inset. Reserve space for the
        // hint or clear action so neither can cover a long placeholder.
        SearchPlaceholder.Margin=new Thickness(margin.Left,0,show?70:40,0);
    }
}

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
            ? ["#F4F6F6","#FFFFFF","#1D272B","#59676A","#9CA9AC","#D6EAE5","#146C62","#FFFFFF","#ECEFEF","#E6EFEC","#0F766E","#FFFFFF","#118077","#E2F1EC","#FAFCFB","#D1DADB","#AE263E","#805200","#EC232A2D"]
            : ["#111417","#191D22","#F4F7F7","#ACB5BD","#46545A","#1D3734","#79D5C1","#102821","#151A1F","#24312F","#16766B","#FFFFFF","#118077","#203C36","#171D21","#303A40","#FFA8AF","#E7BF72","#EC232A2D"];
        var result=new Dictionary<string,Brush>();
        for(var index=0;index<keys.Length;index++)
        {
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index]));brush.Freeze();result[keys[index]]=brush;
        }
        // One solid brand accent gives every action the same visual weight.
        // White labels retain 5.47:1 contrast, including the dark palette.
        result["PrimaryFill"]=result["Primary"];return result;
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

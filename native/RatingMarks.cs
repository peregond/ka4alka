using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

// The small mark of the site a rating comes from, so two ratings next to each other say where each is from:
// IMDb in its yellow, Кинопоиск as «КП» in its orange. Drawn here, so there is no image to load.
public partial class MainWindow
{
    static readonly Brush ImdbMarkBrush=Freeze(Color.FromRgb(0xF5,0xC5,0x18)),KinopoiskMarkBrush=Freeze(Color.FromRgb(0xFF,0x55,0x00));
    static Brush Freeze(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}

    internal static FrameworkElement RatingMark(string source,double size=13)
    {
        var imdb=source=="IMDb";
        var label=new TextBlock{Text=imdb?"IMDb":"КП",FontSize=Math.Max(8,size-3.5),FontWeight=FontWeights.ExtraBold,Foreground=imdb?Brushes.Black:Brushes.White,VerticalAlignment=VerticalAlignment.Center};
        return new Border{Background=imdb?ImdbMarkBrush:KinopoiskMarkBrush,CornerRadius=new(3),Padding=new(imdb?4:3,0,imdb?4:3,1),Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center,ToolTip=imdb?"IMDb":"Кинопоиск",Child=label};
    }
    // The ratings an item has, Кинопоиск first, each with the mark of its site.
    internal static IEnumerable<(string Source,string Value)> MediaRatings(MediaItem item)
    {
        if(item.KpAvailable)yield return ("Кинопоиск",item.KpValue);
        if(item.ImdbAvailable)yield return ("IMDb",item.ImdbValue);
    }
    // One rating as a row: the mark, then the number.
    internal static StackPanel RatingRow(string source,string value,double size,Brush? valueBrush=null,string? valueResource=null)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,ToolTip=source+" · "+value};
        row.Children.Add(RatingMark(source,size));
        var number=new TextBlock{Text=AppLanguage.T(value),FontSize=size,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};
        if(valueBrush!=null)number.Foreground=valueBrush;else number.SetResourceReference(TextBlock.ForegroundProperty,valueResource??"RatingInk");
        row.Children.Add(number);return row;
    }
}

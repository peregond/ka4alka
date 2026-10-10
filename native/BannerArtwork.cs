using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    void AddBannerArtwork(Grid artwork,Image backdrop,Image portrait)
    {
        var placeholder=new Grid{Name="BannerPlaceholder",IsHitTestVisible=false};
        placeholder.Children.Add(new Border{Background=(Brush)FindResource("BannerPlaceholderBase")});
        var blur=new BannerPlaceholder();
        BindingOperations.SetBinding(blur,BannerPlaceholder.PosterSourceProperty,new Binding("Source"){Source=portrait});
        BindingOperations.SetBinding(blur,BannerPlaceholder.BackdropSourceProperty,new Binding("Source"){Source=backdrop});
        placeholder.Children.Add(blur);
        placeholder.Children.Add(new Border{Background=(Brush)FindResource("BannerPlaceholderTint")});
        var fallback=new Style(typeof(Grid));fallback.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));
        var missing=new DataTrigger{Binding=new Binding("Source"){Source=backdrop},Value=null};missing.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible));fallback.Triggers.Add(missing);placeholder.Style=fallback;
        artwork.Children.Add(placeholder);
        var picture=new ImageBrush{Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Top};
        BindingOperations.SetBinding(picture,ImageBrush.ImageSourceProperty,new Binding("Source"){Source=backdrop});
        artwork.Children.Add(new Border{Background=picture,IsHitTestVisible=false});
        artwork.Children.Add(new Border{Background=(Brush)FindResource("ScrimHorizontal"),IsHitTestVisible=false});
        artwork.Children.Add(new Border{Background=(Brush)FindResource("ScrimVertical"),IsHitTestVisible=false});
    }
}

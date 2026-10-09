using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Kachalka;

public partial class MainWindow
{
    FrameworkElement IconLabel(string label,string geometry,double size=17)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var icon=new System.Windows.Shapes.Path{Data=(Geometry)FindResource(geometry),Width=size,Height=size,Stretch=Stretch.Uniform,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new Thickness(0,0,label.Length>0?10:0,0),VerticalAlignment=VerticalAlignment.Center};
        icon.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});
        if(geometry=="IconHeartFilled")
        {
            System.Windows.Data.BindingOperations.ClearBinding(icon,Shape.StrokeProperty);
            icon.Fill=new SolidColorBrush(Color.FromRgb(224,79,98));icon.Stroke=icon.Fill;
        }
        row.Children.Add(icon);if(label.Length>0)row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});return row;
    }
    Button ActionButton(string label,string icon,Action action,string style="QuietButton")
    {
        var button=Button(label,action);button.Content=IconLabel(label,icon);button.Style=(Style)FindResource(style);System.Windows.Automation.AutomationProperties.SetName(button,label);return button;
    }
    void ClearSearch(object sender,RoutedEventArgs e)
    {
        Search.Clear();
        if(current!=null||section is "Загрузки" or "Сохранённое"){searchDelay.Stop();submittedQuery="";searchCategory="";liveRequest?.Cancel();liveLoading=false;liveKey="";Render();}
        else SubmitSearch(sender,e);
        Search.Focus();
    }
    void ToggleTheme(object sender,RoutedEventArgs e){prefs.Light=!prefs.Light;prefs.Save();ApplyTheme();Render();}
    void FocusCatalogSearch(){if(SearchBar.Visibility!=Visibility.Visible){ShowCatalogSection(lastCatalogSection);Render();}Search.Focus();Search.SelectAll();}
    void EnableShortcuts()
    {
        InitializeInterfacePolish();
        PreviewKeyDown+=(_,e)=>
        {
            if((e.Key is Key.K or Key.F or Key.L)&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                FocusCatalogSearch();e.Handled=true;
            }
            else if(e.Key==Key.Escape&&Search.IsKeyboardFocusWithin&&Search.Text.Length>0){ClearSearch(Search,e);e.Handled=true;}
        };
    }
    static void ClipPoster(Border poster)
    {
        if(poster.ActualWidth>0&&poster.ActualHeight>0)poster.Clip=new RectangleGeometry(new Rect(0,0,poster.ActualWidth,poster.ActualHeight),poster.CornerRadius.TopLeft,poster.CornerRadius.TopLeft);
    }
    static IEnumerable<T> VisualElements<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T value)yield return value;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var item in VisualElements<T>(VisualTreeHelper.GetChild(root,index)))yield return item;
    }

}

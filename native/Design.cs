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
        row.Children.Add(icon);if(label.Length>0){var caption=new TextBlock{VerticalAlignment=VerticalAlignment.Center};UiText.Bind(caption,label);row.Children.Add(caption);}return row;
    }
    Button ActionButton(string label,string icon,Action action,string style="QuietButton")
    {
        var button=Button(label,action);button.Content=IconLabel(label,icon);button.Style=(Style)FindResource(style);System.Windows.Automation.AutomationProperties.SetName(button,AppLanguage.T(label));return button;
    }
    void ClearSearch(object sender,RoutedEventArgs e)
    {
        Search.Clear();
        if(current!=null||section is "Загрузки" or "Сохранённое"){searchDelay.Stop();submittedQuery="";searchCategory="";liveRequest?.Cancel();liveLoading=false;liveKey="";Render();}
        else SubmitSearch(sender,e);
        Search.Focus();
    }
    // One refresh for every page: checks the shared catalog and the award lists now, then reloads what the current page shows.
    async void RefreshCatalogClicked(object sender,RoutedEventArgs e)
    {
        RefreshCatalogButton.IsEnabled=false;Status.Text=AppLanguage.T("Проверяем каталог…");
        try
        {
            var changed=await sharedCatalog.RefreshAsync(force:true);
            try{changed|=await Awards.RefreshAsync(force:true);}catch(Exception error)when(error is not OperationCanceledException){ErrorLog.Write(error);}
            if(closed)return;
            onlineIndex.RetryNow();ResetCatalogQualityChecks();catalogPages.Clear();ResetDiscoveryData();
            Status.Text=AppLanguage.T(changed?"Каталог обновлён. Новые названия появятся в разделах.":"Новых названий пока нет.");
            if(current!=null)return;
            if(section is "Фильмы" or "Сериалы" or "ТВ Шоу"){catalogRefreshRequested=true;liveKey="";Render();}
            else if(section=="Игры")RefreshGames();
            else if(section==FeaturedSection){featuredGroups=null;Render();}
        }
        catch(Exception error)when(error is not OperationCanceledException){ErrorLog.Write(error);if(!closed)Status.Text=AppLanguage.T("Не удалось проверить каталог: ")+error.Message;}
        finally{RefreshCatalogButton.IsEnabled=true;}
    }
    // A second click inside the search field takes the whole query instead of the word under the pointer.
    bool SearchDoubleClick(int clickCount){if(clickCount<2)return false;Search.Focus();Search.SelectAll();return true;}
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
    // The width an element has while its page's vertical scroll bar is shown, whether or not the bar is shown now.
    // Columns or a narrow/wide switch based on the plain width can loop: another column shortens the page, the bar
    // goes away, the wider element takes one more column, the page grows, the bar comes back, and so on. WPF then
    // never finishes the layout at that window width (the window stops responding to resizes).
    static double ScrollStableWidth(FrameworkElement element)
    {
        for(var node=VisualTreeHelper.GetParent(element);node!=null;node=VisualTreeHelper.GetParent(node))
        {
            if(node is not ScrollViewer scroll||scroll.VerticalScrollBarVisibility!=ScrollBarVisibility.Auto)continue;
            if(scroll.ComputedVerticalScrollBarVisibility==Visibility.Visible)return element.ActualWidth;
            // The bar's slot is what layout gives it: the theme's MinWidth (17) wins over the style's Width (12).
            var bar=scroll.Template?.FindName("PART_VerticalScrollBar",scroll) as System.Windows.Controls.Primitives.ScrollBar;
            var set=bar!=null&&double.IsFinite(bar.Width)?bar.Width:SystemParameters.VerticalScrollBarWidth;
            var slot=bar==null?set:Math.Max(bar.MinWidth,Math.Min(set,bar.MaxWidth))+bar.Margin.Left+bar.Margin.Right;
            return Math.Max(0,element.ActualWidth-slot);
        }
        return element.ActualWidth;
    }
    static void ClipPoster(Border poster)
    {
        if(poster.ActualWidth>0&&poster.ActualHeight>0)poster.Clip=new RectangleGeometry(new Rect(0,0,poster.ActualWidth,poster.ActualHeight),poster.CornerRadius.TopLeft,poster.CornerRadius.TopLeft);
    }
    // The hover and focus rings reach 4 px outside a poster card. Returns the first ancestor (up to stop)
    // whose clip cuts that ring, so a layout change cannot silently crop the first column again.
    static string? RingClipper(FrameworkElement card,DependencyObject stop)
    {
        var ring=new Rect(-4,-4,card.ActualWidth+8,card.ActualHeight+8);
        for(var parent=VisualTreeHelper.GetParent(card);parent is Visual visual;parent=VisualTreeHelper.GetParent(parent))
        {
            if(VisualTreeHelper.GetClip(visual) is {} clip)
            {
                // Only the sides matter: a scroll viewer legitimately clips a row scrolled out of view.
                var bounds=card.TransformToAncestor(visual).TransformBounds(ring);
                if(bounds.Left<clip.Bounds.Left-.5||bounds.Right>clip.Bounds.Right+.5)
                    return $"{visual.GetType().Name} {(visual as FrameworkElement)?.Name} clips at {clip.Bounds} while the ring needs {bounds}";
            }
            if(ReferenceEquals(parent,stop))break;
        }
        return null;
    }
    static IEnumerable<T> VisualElements<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T value)yield return value;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var item in VisualElements<T>(VisualTreeHelper.GetChild(root,index)))yield return item;
    }

}

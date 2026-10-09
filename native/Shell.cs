using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Kachalka;

// Resolves a palette key to the frozen brush that is current for the active theme.
// Bindings that use it re-evaluate when DownloadItem.Refresh() raises PropertyChanged.
sealed class ThemeBrush:IValueConverter
{
    public static readonly ThemeBrush Instance=new();
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is string key&&Application.Current.TryFindResource(key) is Brush brush?brush:Brushes.Transparent;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

public partial class MainWindow
{
    Action? headerBackAction;
    // Width of the content gutter currently applied; 32 on wide windows, narrower on small or heavily scaled ones.
    double contentGutter=32;

    void HeaderBack(object sender,RoutedEventArgs e)=>headerBackAction?.Invoke();
    // The shared back button lives in the header, left of the search field.
    // Pages call this instead of adding their own button.
    void SetBack(string label,Action action)
    {
        headerBackAction=action;BackButton.Visibility=Visibility.Visible;BackButton.ToolTip=label;
        System.Windows.Automation.AutomationProperties.SetName(BackButton,label);
    }
    void ClearBack(){headerBackAction=null;BackButton.Visibility=Visibility.Collapsed;}

    // Navigation entry content: 20 px icon, label and an optional trailing badge.
    // badge: 0 none, 1 plain counter (Saved), 2 accent pill (active downloads).
    Grid NavContent(string label,string geometry,bool narrow,int badge=0)
    {
        var grid=new Grid{VerticalAlignment=VerticalAlignment.Center};
        grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=new Path{Data=(Geometry)FindResource(geometry),Width=20,Height=20,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Tag="NavIcon",VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=narrow?HorizontalAlignment.Center:HorizontalAlignment.Left,Margin=new(0,0,narrow?0:12,0)};
        icon.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});
        grid.Children.Add(icon);
        if(!narrow&&label.Length>0){var text=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};Grid.SetColumn(text,1);grid.Children.Add(text);}
        if(badge>0)
        {
            var value=new TextBlock{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            var frame=new Border{Child=value,Visibility=Visibility.Collapsed,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
            if(badge==2)
            {
                var small=narrow;
                value.FontSize=small?10:12;value.FontWeight=FontWeights.Bold;value.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
                frame.CornerRadius=new(small?8:11);frame.Height=small?16:22;frame.MinWidth=small?16:22;frame.Padding=new(small?4:7,0,small?4:7,0);frame.SetResourceReference(Border.BackgroundProperty,"AccentSoft");
                if(small){frame.HorizontalAlignment=HorizontalAlignment.Right;frame.VerticalAlignment=VerticalAlignment.Top;frame.Margin=new(0,-8,-10,0);}
            }
            else
            {
                value.FontFamily=(FontFamily)FindResource("MonoFont");value.FontSize=12;value.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");
                if(narrow)frame.Visibility=Visibility.Collapsed;
            }
            if(badge==2&&narrow)Grid.SetColumn(frame,0);else Grid.SetColumn(frame,2);
            grid.Children.Add(frame);grid.Tag=frame;
        }
        return grid;
    }
    void SetNavBadge(Button button,string text)
    {
        if(button.Content is not Grid {Tag:Border frame})return;
        var value=(TextBlock)frame.Child;value.Text=text;
        var wanted=text.Length>0&&text!="0"&&(frame.Background!=null||!compactWidth);
        frame.Visibility=wanted?Visibility.Visible:Visibility.Collapsed;
    }
    void StyleNav(Button button,bool selected)
    {
        if(selected)button.SetResourceReference(Control.BackgroundProperty,"NavSelected");else button.Background=Brushes.Transparent;
        button.SetResourceReference(Control.ForegroundProperty,selected?"Text":"Muted");
        button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Medium;
        if(button.Content is Grid grid&&grid.Children.OfType<Path>().FirstOrDefault(x=>Equals(x.Tag,"NavIcon")) is {} icon)
        {
            if(selected){BindingOperations.ClearBinding(icon,Shape.StrokeProperty);icon.SetResourceReference(Shape.StrokeProperty,"Accent");}
            else{icon.ClearValue(Shape.StrokeProperty);icon.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});}
        }
    }
    void UpdateSavedCount(){SetNavBadge(SavedButton,prefs.LiveFavorites.Select(x=>x.Id).Distinct().Count().ToString());}

    // ----- Downloads widget in the sidebar -----
    TextBlock? widgetDown,widgetUp,widgetHeading;
    Button? widgetAll;
    StackPanel? widgetRowsHost;
    readonly ContentControl[] widgetRows=[new(),new(),new()];
    int widgetVisibleRows;
    void BuildDownloadsWidget()
    {
        if(widgetDown!=null)return;
        var host=DownloadsWidgetContent;
        var head=new DockPanel{Margin=new(0,0,0,10)};
        widgetAll=new Button{Style=(Style)FindResource("QuietButton"),MinHeight=0,Padding=new(6,2,2,2),Margin=new(0),FontSize=12,ToolTip="Открыть загрузки"};
        var allRow=new StackPanel{Orientation=Orientation.Horizontal};allRow.Children.Add(new TextBlock{Text="Все",VerticalAlignment=VerticalAlignment.Center,FontSize=12,FontWeight=FontWeights.SemiBold});
        allRow.Children.Add(new Path{Data=(Geometry)FindResource("IconChevron"),Width=12,Height=12,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(2,0,0,0),VerticalAlignment=VerticalAlignment.Center,Stroke=null});
        widgetAll.Content=allRow;((Path)allRow.Children[1]).SetBinding(Shape.StrokeProperty,new Binding("Foreground"){Source=widgetAll});
        widgetAll.SetResourceReference(Control.ForegroundProperty,"Muted");widgetAll.Click+=ShowDownloads;System.Windows.Automation.AutomationProperties.SetName(widgetAll,"Все загрузки");
        DockPanel.SetDock(widgetAll,Dock.Right);head.Children.Add(widgetAll);
        widgetHeading=new TextBlock{Text="Загрузки",FontSize=14,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center};head.Children.Add(widgetHeading);host.Children.Add(head);
        var speeds=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,12)};
        widgetDown=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=12,Margin=new(0,0,14,0)};widgetDown.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        widgetUp=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=12};widgetUp.SetResourceReference(TextBlock.ForegroundProperty,"Info");
        speeds.Children.Add(widgetDown);speeds.Children.Add(widgetUp);host.Children.Add(speeds);
        widgetRowsHost=new StackPanel{Name="DownloadsWidgetRows"};
        foreach(var row in widgetRows){row.ContentTemplate=(DataTemplate)FindResource("WidgetRow");row.Visibility=Visibility.Collapsed;widgetRowsHost.Children.Add(row);}
        host.Children.Add(widgetRowsHost);
    }
    static int WidgetRank(DownloadItem item)=>item.StatusKind switch{DownloadStatusKind.Downloading=>0,DownloadStatusKind.Checking=>1,DownloadStatusKind.Waiting=>2,DownloadStatusKind.Seeding=>3,DownloadStatusKind.Paused=>4,_=>5};
    // Cheap by design: it only reassigns a row when the selected download changed,
    // so the 2 second tick never rebuilds visuals. Row text follows DownloadItem bindings.
    void UpdateDownloadsWidget()
    {
        if(!ready||closed||downloads==null)return;
        var items=downloads.Items;
        var active=items.Count(x=>x.IsActiveDownload);
        SetNavBadge(DownloadsButton,active>0?active.ToString():"");
        var narrow=compactWidth;
        // How many rows fit depends on the DIP height, which shrinks as the Windows scale grows.
        var height=ActualHeight;var rows=height>=820?3:height>=700?2:height>=620?1:0;
        var onDownloads=section=="Загрузки";
        var visible=!narrow&&items.Count>0&&(rows>0||onDownloads)&&(!veryShortHeight());
        if(!visible){DownloadsWidget.Visibility=Visibility.Collapsed;return;}
        BuildDownloadsWidget();
        DownloadsWidget.Visibility=Visibility.Visible;
        long down=0,up=0;foreach(var item in items){down+=Math.Max(0,item.DownloadRate);up+=Math.Max(0,item.UploadRate);}
        widgetDown!.Text="↓ "+DownloadService.FormatBytes(down)+"/с";widgetUp!.Text="↑ "+DownloadService.FormatBytes(up)+"/с";
        widgetHeading!.Text=onDownloads?"Скорость сейчас":"Загрузки";widgetHeading.FontWeight=onDownloads?FontWeights.Medium:FontWeights.Bold;widgetHeading.FontSize=onDownloads?12:14;
        widgetAll!.Visibility=onDownloads?Visibility.Collapsed:Visibility.Visible;
        var shown=onDownloads?0:rows;widgetRowsHost!.Visibility=shown>0?Visibility.Visible:Visibility.Collapsed;
        var top=shown==0?[]:items.OrderBy(WidgetRank).ThenByDescending(x=>x.AddedUtc).Take(shown).ToArray();
        for(var index=0;index<widgetRows.Length;index++)
        {
            var row=widgetRows[index];
            if(index<top.Length){if(!ReferenceEquals(row.Content,top[index]))row.Content=top[index];row.Visibility=Visibility.Visible;}
            else if(row.Visibility!=Visibility.Collapsed){row.Content=null;row.Visibility=Visibility.Collapsed;}
        }
        widgetVisibleRows=top.Length;
    }
    bool veryShortHeight()=>ActualHeight>0&&ActualHeight<520;
    void OpenWidgetDownload(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement {Tag:DownloadItem item})return;
        searchDelay.Stop();section="Загрузки";current=null;activePerson=null;downloadReturnItem=item;Render();
    }
}

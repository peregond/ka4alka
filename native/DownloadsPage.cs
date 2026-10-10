using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;

namespace Kachalka;
public partial class MainWindow
{
    void OpenDownloadCard(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.Tag is not DownloadItem item)return;
        var card=DownloadMetadata.Card(item);if(card==null)return;
        activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;
        downloadReturnItem=item;
        current=DownloadMetadata.EnrichMedia(card,new[]{sharedCatalog.Find(card)}.OfType<MediaItem>().Concat(prefs.LiveFavorites).Concat(catalogIndex.Recent(card.Section,200)).Concat(BundledCatalog.Search(card.Section,card.Title)));
        Render();
    }
    ListCollectionView? downloadView;
    ListBox? downloadList;
    DownloadItem? downloadReturnItem;
    string? DownloadBackLabel()=>downloadReturnItem is {} returned&&downloads.Items.Contains(returned)?"Загрузки":null;
    TextBlock? downloadSummary;
    TextBlock? downloadHeading;
    FrameworkElement? downloadHeadingRow,downloadHeadingHost,downloadToolbar;
    Button? downloadControls,downloadOrder;
    string downloadSort="newest";
    bool downloadCommandBusy;
    bool downloadMetadataBusy,downloadMetadataSortPending;
    string downloadMetadataFingerprint="";
    readonly List<ContextMenu> downloadMenus=[];
    static readonly (string Key,string Label,DownloadSort Sort)[] DownloadSortChoices=[("newest","Сначала новые",DownloadSort.Newest),("name","По названию",DownloadSort.Name),("speed","По скорости",DownloadSort.Speed),("size","По размеру",DownloadSort.Size),("progress","По готовности",DownloadSort.Progress)];
    // ----- tiles, tabs and summary -----
    string downloadTab="all";
    TextBlock? tileDownValue,tileUpValue,tileDiskValue,tileDiskCaption,tileDiskPercent;
    ProgressBar? tileDiskBar;
    Grid? downloadTiles;
    readonly Dictionary<string,TextBlock> downloadTabCounts=[];
    DateTime diskReadUtc;long diskFree,diskTotal;bool diskReading;
    bool MatchesDownloadTab(DownloadItem item)=>downloadTab switch
    {
        "active"=>!item.Completed&&!item.Paused&&item.StatusKind!=DownloadStatusKind.Error,
        "seeding"=>item.Completed&&!item.Paused,
        "errors"=>item.StatusKind==DownloadStatusKind.Error,
        _=>true
    };
    static string Plural(int count,string one,string few,string many)
    {
        var tail=count%100;if(tail is >=11 and <=14)return many;
        return (count%10) switch{1=>one,2 or 3 or 4=>few,_=>many};
    }
    void RenderDownloads()
    {
        downloads.Update();downloadMenus.Clear();downloadTabCounts.Clear();
        foreach(var item in downloads.Items){item.MinimumQualityHeight=QualityMinimum;item.Refresh();}
        RepairDownloadMetadata();
        downloadSort=DownloadSortChoices.Any(x=>x.Key==prefs.DownloadSort)?prefs.DownloadSort:"newest";
        if(downloads.Items.Count==0)downloadTab="all";
        // Heading: page title and summary on the left, queue actions on the right.
        var heading=new DockPanel{Margin=new(0,0,0,16)};downloadHeadingRow=heading;PageHeader.Children.Add(heading);
        var toolbar=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};downloadToolbar=toolbar;DockPanel.SetDock(toolbar,Dock.Right);heading.Children.Add(toolbar);
        var titles=new StackPanel{VerticalAlignment=VerticalAlignment.Center};downloadHeadingHost=titles;heading.Children.Add(titles);
        var title=Text("Загрузки",28);downloadHeading=title;title.FontFamily=(FontFamily)FindResource("DisplayFont");title.FontWeight=FontWeights.Bold;title.Margin=new(0,0,16,0);title.VerticalAlignment=VerticalAlignment.Center;titles.Children.Add(title);
        downloadSummary=Text("",14,true);downloadSummary.TextWrapping=TextWrapping.NoWrap;downloadSummary.TextTrimming=TextTrimming.CharacterEllipsis;downloadSummary.Margin=new(0,4,0,0);titles.Children.Add(downloadSummary);
        var controls=ActionButton("Управление","IconSettings",()=>{});downloadControls=controls;controls.Margin=new(0,0,10,0);controls.Padding=new(16,0,16,0);controls.Height=48;controls.MinHeight=48;controls.ToolTip="Управление загрузками и раздачами";AutomationProperties.SetName(controls,"Управление загрузками");
        var commands=Menu();controls.ContextMenu=commands;
        foreach(var command in new[]{("Начать загрузки",false,false,"IconPlay"),("Остановить загрузки",false,true,"IconPause"),("Запустить раздачи",true,false,"IconPlay"),("Остановить раздачи",true,true,"IconPause")})
        {
            var choice=MenuEntry(command.Item1,command.Item4);choice.IsEnabled=downloads.Items.Count>0;choice.ToolTip=command.Item2?"Для завершённых торрентов":"Для незавершённых загрузок";
            choice.Click+=async(_,_)=>
            {
                if(downloadCommandBusy)return;downloadCommandBusy=true;controls.IsEnabled=false;
                try{var count=await downloads.SetGroupAsync(command.Item2,command.Item3);Status.Text=$"Задач изменено: {count}";RefreshDownloadView();}
                catch(Exception error){Status.Text="Не удалось изменить очередь: "+error.Message;}
                finally{downloadCommandBusy=false;controls.IsEnabled=true;SyncTimer();}
            };commands.Items.Add(choice);
        }
        commands.Items.Add(new Separator());var limits=MenuEntry("Лимиты скорости…","IconSettings");limits.Click+=(_,_)=>DownloadLimits();commands.Items.Add(limits);controls.Click+=(_,_)=>OpenDownloadMenu(controls);toolbar.Children.Add(controls);
        var add=ActionButton("Добавить торрент","IconPlus",()=>AddTorrent(this,new RoutedEventArgs()),"PrimaryButton");add.Margin=new(0);add.Height=48;add.MinHeight=48;add.Padding=new(20,0,20,0);AutomationProperties.SetName(add,"Добавить торрент в очередь");downloadAddButton=add;toolbar.Children.Add(add);

        // Tiles: speed in, speed out, free disk space.
        downloadTiles=new Grid{Name="DownloadTiles",Margin=new(0,0,0,16)};PageHeader.Children.Add(downloadTiles);
        for(var index=0;index<3;index++)downloadTiles.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        Border Tile(int column,string icon,string caption,string accentKey,out TextBlock value,out TextBlock unit)
        {
            var stack=new StackPanel();
            var head=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,10)};
            var glyph=new System.Windows.Shapes.Path{Data=(Geometry)FindResource(icon),Width=13,Height=13,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,accentKey);head.Children.Add(glyph);
            var label=new TextBlock{Text=caption,FontSize=12,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");head.Children.Add(label);stack.Children.Add(head);
            var numbers=new StackPanel{Orientation=Orientation.Horizontal};
            value=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=24,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Bottom};value.SetResourceReference(TextBlock.ForegroundProperty,"Text");
            unit=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=14,Margin=new(8,0,0,4),VerticalAlignment=VerticalAlignment.Bottom};unit.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            numbers.Children.Add(value);numbers.Children.Add(unit);stack.Children.Add(numbers);
            var tile=new Border{Child=stack,CornerRadius=new(16),BorderThickness=new(1),Padding=new(18,16,18,16),Margin=new(column==0?0:6,0,column==2?0:6,0)};tile.SetResourceReference(Border.BackgroundProperty,"Panel");tile.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");Grid.SetColumn(tile,column);downloadTiles.Children.Add(tile);return tile;
        }
        var downTile=Tile(0,"IconArrowDown","Скачивание","Accent",out var downValue,out var downUnit);var upTile=Tile(1,"IconArrowUp","Отдача","Info",out var upValue,out var upUnit);
        tileDownValue=downValue;tileUpValue=upValue;downTile.Tag=downUnit;upTile.Tag=upUnit;
        var diskTile=Tile(2,"IconFolder","Диск","Muted",out var diskValue,out var diskUnit);tileDiskValue=diskValue;tileDiskCaption=diskUnit;
        if(diskTile.Child is StackPanel diskStack&&diskStack.Children[0] is StackPanel diskHead&&diskHead.Children[1] is TextBlock diskLabel)
        {
            diskLabel.Text="Диск · "+System.IO.Path.GetPathRoot(prefs.Folder)+"  ·  папка Ka4alka";
            tileDiskPercent=new TextBlock{FontSize=12,HorizontalAlignment=HorizontalAlignment.Right};tileDiskPercent.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");
            diskStack.Children.Add(tileDiskBar=new ProgressBar{Minimum=0,Maximum=100,Height=4,Margin=new(0,10,0,0),BorderThickness=new(0)});tileDiskBar.SetResourceReference(Control.BackgroundProperty,"Track");tileDiskBar.SetResourceReference(Control.ForegroundProperty,"Subtle");
        }
        downTile.Tag=downUnit;upTile.Tag=upUnit;tileDownUnit=downUnit;tileUpUnit=upUnit;
        UpdateDownloadTiles();

        // Tabs and sort.
        var tabsRow=new DockPanel{Margin=new(0,0,0,16)};PageHeader.Children.Add(tabsRow);
        var order=ActionButton(DownloadSortChoices.First(x=>x.Key==downloadSort).Label,"IconFilter",()=>{},"PillButton");downloadOrder=order;order.Margin=new(12,0,0,0);order.Padding=new(14,0,16,0);AutomationProperties.SetName(order,"Сортировка загрузок");order.ContextMenu=Menu();
        foreach(var entry in DownloadSortChoices)
        {
            var choice=MenuEntry(entry.Label);choice.Tag=entry.Key;choice.IsCheckable=true;choice.IsChecked=downloadSort==entry.Key;
            choice.Click+=(_,_)=>
            {
                var previous=downloadSort;downloadSort=entry.Key;prefs.DownloadSort=entry.Key;
                try{prefs.Save();}catch(Exception error){downloadSort=previous;prefs.DownloadSort=previous;choice.IsChecked=false;Status.Text="Не удалось сохранить сортировку: "+error.Message;return;}
                foreach(MenuItem other in order.ContextMenu.Items)other.IsChecked=Equals(other.Tag,downloadSort);
                FitDownloadsToolbar();if(downloadView!=null)downloadView.CustomSort=DownloadOrdering.Comparer(entry.Sort);if(downloadView?.Cast<DownloadItem>().FirstOrDefault() is {} first)downloadList?.ScrollIntoView(first);
            };order.ContextMenu.Items.Add(choice);
        }
        order.Click+=(_,_)=>OpenDownloadMenu(order);DockPanel.SetDock(order,Dock.Right);tabsRow.Children.Add(order);
        var segmentFrame=new Border{CornerRadius=new(12),BorderThickness=new(1),Padding=new(4),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};segmentFrame.SetResourceReference(Border.BackgroundProperty,"Panel");segmentFrame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");AutomationProperties.SetName(segmentFrame,"Вкладки загрузок");
        var segmentRow=new StackPanel{Orientation=Orientation.Horizontal};segmentFrame.Child=segmentRow;
        // On narrow windows the strip scrolls sideways instead of being cut off; the wheel still scrolls the page.
        var segmentScroll=new ScrollViewer{Name="DownloadTabsScroll",Content=segmentFrame,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
        segmentScroll.PreviewMouseWheel+=(sender,e)=>{if(e.Handled||sender is not ScrollViewer {Parent:UIElement parent})return;e.Handled=true;parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta){RoutedEvent=UIElement.MouseWheelEvent,Source=sender});};
        tabsRow.Children.Add(segmentScroll);
        foreach(var (key,label) in new[]{("all","Все"),("active","Качаются"),("seeding","Раздаются"),("errors","Ошибки")})
        {
            var count=new TextBlock{FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=11,Opacity=.7,Margin=new(6,1,0,0),VerticalAlignment=VerticalAlignment.Center};downloadTabCounts[key]=count;
            var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});content.Children.Add(count);
            var tab=new RadioButton{Style=(Style)FindResource("SegmentButton"),GroupName="DownloadTabs",Content=content,IsChecked=downloadTab==key,Tag=key};AutomationProperties.SetName(tab,"Загрузки: "+label);
            tab.Checked+=(_,_)=>{downloadTab=key;if(downloadView!=null){downloadView.Refresh();}};
            segmentRow.Children.Add(tab);
        }
        UpdateDownloadSummary();FitDownloadsToolbar();
        if(downloads.Items.Count>0)
        {
            downloadView=new ListCollectionView(downloads.Items){CustomSort=DownloadOrdering.Comparer(DownloadSortChoices.First(x=>x.Key==downloadSort).Sort),Filter=x=>x is DownloadItem item&&MatchesDownloadTab(item)};
            downloadList=new ListBox{ItemsSource=downloadView,ItemTemplate=(DataTemplate)FindResource("DownloadRow"),Margin=new(-6,0,-6,0),Padding=new(6,0,6,0)};AutomationProperties.SetName(downloadList,"Очередь загрузок");
            if(downloadReturnItem is {} returned&&downloads.Items.Contains(returned))
            {
                var list=downloadList;list.Loaded+=(_,_)=>list.ScrollIntoView(returned);
            }
            downloadReturnItem=null;
            Body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Body.RowDefinitions.Add(new(){Height=GridLength.Auto});
            Body.Children.Add(downloadList);
            var zone=DropZone();Grid.SetRow(zone,1);Body.Children.Add(zone);downloadDropZone=zone;
            return;
        }
        var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=360};
        var illustration=IconLabel("","IconDownload",32);foreach(var path in VisualElements<Path>(illustration)){BindingOperations.ClearBinding(path,Shape.StrokeProperty);path.SetResourceReference(Shape.StrokeProperty,"Muted");}illustration.HorizontalAlignment=HorizontalAlignment.Center;illustration.Margin=new(0,0,0,18);empty.Children.Add(illustration);
        var caption=Text("Загрузок пока нет",22);caption.TextAlignment=TextAlignment.Center;caption.FontWeight=FontWeights.Bold;empty.Children.Add(caption);
        var hint=Text("Выбери раздачу в карточке фильма или добавь magnet-ссылку либо .torrent-файл.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);
        var emptyAdd=ActionButton("Добавить торрент","IconPlus",()=>AddTorrent(this,new RoutedEventArgs()),"PrimaryButton");emptyAdd.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(emptyAdd);Body.Children.Add(empty);
    }
    Button? downloadAddButton;
    TextBlock? tileDownUnit,tileUpUnit;
    Border? downloadDropZone;
    // Dashed drop target for .torrent files and pasted magnet links.
    Border DropZone()
    {
        var zone=new Border{Name="DownloadDropZone",CornerRadius=new(18),Padding=new(28,20,28,20),Margin=new(0,4,0,12),Background=Brushes.Transparent};
        var dash=new System.Windows.Shapes.Rectangle{RadiusX=18,RadiusY=18,StrokeThickness=1.5,StrokeDashArray=[4,4],IsHitTestVisible=false};dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"TrackOff");
        var row=new Grid{HorizontalAlignment=HorizontalAlignment.Center};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var disc=new Border{Width=44,Height=44,CornerRadius=new(22),Margin=new(0,0,16,0),Child=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconMagnet"),Width=20,Height=20,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round}};disc.SetResourceReference(Border.BackgroundProperty,"AccentSoft");((System.Windows.Shapes.Path)disc.Child).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Accent");
        Grid.SetColumn(disc,0);row.Children.Add(disc);
        var text=new TextBlock{Text="Перетащите .torrent-файл сюда или вставьте magnet-ссылку",FontSize=14,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=420};text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(text,1);row.Children.Add(text);
        var key=new Border{CornerRadius=new(7),BorderThickness=new(1),Padding=new(7,4,7,4),Margin=new(16,0,0,0),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text="Ctrl V",FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=11}};key.SetResourceReference(Border.BackgroundProperty,"Raised");key.SetResourceReference(Border.BorderBrushProperty,"Edge");((TextBlock)key.Child).SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(key,2);row.Children.Add(key);
        var host=new Grid();host.Children.Add(dash);host.Children.Add(row);zone.Child=host;
        // On very narrow windows the shortcut hint goes away so the sentence keeps enough room.
        zone.SizeChanged+=(_,e)=>{var small=e.NewSize.Width<340;key.Visibility=small?Visibility.Collapsed:Visibility.Visible;zone.Padding=small?new(14,14,14,14):new(28,20,28,20);disc.Margin=small?new(0,0,10,0):new(0,0,16,0);};
        AutomationProperties.SetName(zone,"Перетащите .torrent-файл или вставьте magnet-ссылку");
        // The window handles the actual drop; the zone only shows the target while the pointer is over it.
        return zone;
    }
    void UpdateDownloadTiles()
    {
        if(section!="Загрузки"||tileDownValue==null)return;
        long down=0,up=0;foreach(var item in downloads.Items){down+=Math.Max(0,item.DownloadRate);up+=Math.Max(0,item.UploadRate);}
        void Speed(TextBlock value,TextBlock? unit,long rate)
        {
            var text=DownloadService.FormatBytes(rate);var cut=text.LastIndexOf(' ');
            value.Text=cut>0?text[..cut]:text;if(unit!=null)unit.Text=(cut>0?text[(cut+1)..]:"Б")+"/с";
        }
        Speed(tileDownValue,tileDownUnit,down);Speed(tileUpValue!,tileUpUnit,up);
        if(DateTime.UtcNow-diskReadUtc>TimeSpan.FromSeconds(10)&&!diskReading)
        {
            diskReading=true;var folder=prefs.Folder;
            _=ReadDiskAsync(folder);
        }
        PaintDiskTile();
    }
    async Task ReadDiskAsync(string folder)
    {
        (long Free,long Total)? volume=await Task.Run(()=>
        {
            try{var root=System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder));if(string.IsNullOrEmpty(root))return ((long Free,long Total)?)null;var drive=new System.IO.DriveInfo(root);return (drive.AvailableFreeSpace,drive.TotalSize);}
            catch{return ((long Free,long Total)?)null;}
        });
        diskReading=false;diskReadUtc=DateTime.UtcNow;
        if(closed)return;
        if(volume is {} value){diskFree=value.Free;diskTotal=value.Total;}
        PaintDiskTile();
    }
    void PaintDiskTile()
    {
        if(section!="Загрузки"||tileDiskValue==null||tileDiskCaption==null||tileDiskBar==null)return;
        if(diskTotal<=0){tileDiskValue.Text="—";tileDiskCaption.Text="";tileDiskBar.Value=0;return;}
        var text=DownloadService.FormatBytes(diskFree);var cut=text.LastIndexOf(' ');
        tileDiskValue.Text=cut>0?text[..cut]:text;tileDiskCaption.Text=(cut>0?text[(cut+1)..]:"Б")+" свободно";
        tileDiskBar.Value=Math.Clamp((diskTotal-diskFree)*100d/diskTotal,0,100);
        // Less than 10 GB free is a warning; less than what active downloads still need is an error.
        var needed=DownloadSpace.SafetyBytes+downloads.Items.Where(x=>!x.Completed).Sum(x=>x.Files.Sum(f=>Math.Max(0,(long)(f.Size*(1-Math.Clamp(f.Progress,0,100)/100)))));
        var key=diskFree<needed?"Danger":diskFree<10L*1024*1024*1024?"Warning":"Subtle";
        tileDiskBar.SetResourceReference(Control.ForegroundProperty,key);tileDiskValue.SetResourceReference(TextBlock.ForegroundProperty,key=="Subtle"?"Text":key);
    }
    void FitDownloadsToolbar()
    {
        if(section!="Загрузки"||downloadControls==null||downloadOrder==null)return;
        var tiny=ActualWidth>0&&ActualWidth<560;var shortView=ActualHeight>0&&ActualHeight<560;
        var veryShort=ActualHeight>0&&ActualHeight<360;
        downloadControls.Content=IconLabel(tiny?"":"Управление","IconSettings");
        if(downloadAddButton!=null)downloadAddButton.Content=IconLabel(tiny?"":"Добавить торрент","IconPlus");
        var label=DownloadSortChoices.FirstOrDefault(x=>x.Key==downloadSort).Label??"Сначала новые";
        downloadOrder.Content=ChipContent(tiny?"":label,false,"IconFilter");downloadOrder.ToolTip="Сортировка: "+label;
        if(downloadHeading!=null)downloadHeading.FontSize=ActualWidth>0&&ActualWidth<400?16:ActualWidth<440?20:shortView?22:28;
        foreach(var count in downloadTabCounts.Values)count.Visibility=ActualWidth>0&&ActualWidth<640?Visibility.Collapsed:Visibility.Visible;
        if(downloadHeadingHost!=null)downloadHeadingHost.Visibility=veryShort?Visibility.Collapsed:Visibility.Visible;
        if(downloadHeadingRow!=null)downloadHeadingRow.Margin=new(0,0,0,veryShort?2:shortView?8:16);
        if(downloadToolbar!=null)downloadToolbar.Margin=new(0);
        if(downloadSummary!=null)downloadSummary.Visibility=shortView?Visibility.Collapsed:Visibility.Visible;
        // Speed and disk tiles need height; on short windows (large Windows scale) they give way to the queue.
        if(downloadTiles!=null)
        {
            var show=ActualHeight>=760;downloadTiles.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            // Narrow windows stack the tiles into one column instead of squeezing the numbers.
            var columns=ActualWidth<780?1:3;
            for(var index=0;index<3;index++)
            {
                var tile=downloadTiles.Children[index];Grid.SetColumn(tile,columns==1?0:index);Grid.SetRow(tile,columns==1?index:0);Grid.SetColumnSpan(tile,columns==1?3:1);
                if(tile is FrameworkElement element)element.Margin=columns==1?new(0,0,0,index==2?0:8):new(index==0?0:6,0,index==2?0:6,0);
            }
            if(columns==1&&downloadTiles.RowDefinitions.Count<3)for(var index=0;index<3;index++)downloadTiles.RowDefinitions.Add(new(){Height=GridLength.Auto});
            if(columns==3)downloadTiles.RowDefinitions.Clear();
        }
        if(downloadDropZone!=null)downloadDropZone.Visibility=ActualHeight>=700?Visibility.Visible:Visibility.Collapsed;
    }
    void UpdateDownloadSummary()
    {
        var all=downloads.Items;
        var active=all.Count(x=>MatchesTab("active",x));var seeding=all.Count(x=>MatchesTab("seeding",x));var errors=all.Count(x=>MatchesTab("errors",x));
        if(downloadSummary!=null)
        {
            var parts=new List<string>{$"{all.Count} {Plural(all.Count,"торрент","торрента","торрентов")}"};
            if(active>0)parts.Add($"{active} в работе");
            if(seeding>0)parts.Add($"{seeding} {Plural(seeding,"раздаётся","раздаются","раздаются")}");
            if(errors>0)parts.Add($"{errors} с ошибкой");
            downloadSummary.Text=string.Join(" · ",parts);downloadSummary.ToolTip=downloadSummary.Text;
        }
        foreach(var (key,label) in downloadTabCounts)
            label.Text=(key switch{"active"=>active,"seeding"=>seeding,"errors"=>errors,_=>all.Count}).ToString();
        UpdateDownloadTiles();
    }
    static bool MatchesTab(string tab,DownloadItem item)=>tab switch
    {
        "active"=>!item.Completed&&!item.Paused&&item.StatusKind!=DownloadStatusKind.Error,
        "seeding"=>item.Completed&&!item.Paused,
        "errors"=>item.StatusKind==DownloadStatusKind.Error,
        _=>true
    };
    void RefreshDownloadView()
    {
        UpdateDownloadsWidget();ApplyPosterDownloads();RefreshDetailActions();
        if(section!="Загрузки"||downloadView==null)return;RepairDownloadMetadata();UpdateDownloadSummary();
        // A status change can move a task between tabs; re-evaluate the filter only when membership changed.
        if(downloadTab!="all"&&!downloadView.Cast<DownloadItem>().Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(downloads.Items.Where(MatchesDownloadTab).Select(x=>x.Id).OrderBy(x=>x))&&!(downloadList?.IsMouseOver==true))downloadView.Refresh();
        if(downloadSort is not ("speed" or "progress" or "size")&&!(downloadSort=="name"&&downloadMetadataSortPending))return;
        // Keep the row under the pointer stable while the user chooses an action.
        if(downloadList?.IsMouseOver==true||downloadList?.IsKeyboardFocusWithin==true||downloadMenus.Any(menu=>menu.IsOpen))return;
        var order=DownloadSortChoices.First(x=>x.Key==downloadSort).Sort;
        if(downloadView.Cast<DownloadItem>().Select(x=>x.Id).SequenceEqual(DownloadOrdering.Sort(downloads.Items.Where(MatchesDownloadTab),order).Select(x=>x.Id))){downloadMetadataSortPending=false;return;}
        var selected=downloadList?.SelectedItem;downloadView.Refresh();if(downloadList!=null&&selected!=null)downloadList.SelectedItem=selected;
        downloadMetadataSortPending=false;
    }
    // ----- row events -----
    // On narrow windows the action buttons wrap below the information column.
    void DownloadRowResized(object sender,SizeChangedEventArgs e)
    {
        if(sender is not Grid grid||!e.WidthChanged)return;
        if(grid.Children.OfType<StackPanel>().FirstOrDefault(x=>x.Name=="RowActions") is not {} actions)return;
        var wrap=e.NewSize.Width<560;
        if(wrap){Grid.SetColumn(actions,1);Grid.SetRow(actions,1);Grid.SetColumnSpan(actions,2);actions.HorizontalAlignment=HorizontalAlignment.Left;actions.Margin=new(8,10,0,0);}
        else{Grid.SetColumn(actions,2);Grid.SetRow(actions,0);Grid.SetColumnSpan(actions,1);actions.HorizontalAlignment=HorizontalAlignment.Right;actions.Margin=new(0);}
    }
    // "Подробнее" opens the queue row in place: files, folder, sources and the added time.
    void ToggleDownloadDetails(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement {Tag:DownloadItem item})return;
        item.Expanded=!item.Expanded;item.Refresh();
    }
    void DownloadDetailsPanelResized(object sender,SizeChangedEventArgs e)
    {
        if(sender is not Grid grid||!e.WidthChanged||grid.Children.Count<2||grid.ColumnDefinitions.Count<2)return;
        var stacked=e.NewSize.Width<560;var info=grid.Children[1];
        grid.ColumnDefinitions[1].MinWidth=stacked?0:220;grid.ColumnDefinitions[1].Width=stacked?new GridLength(0):new GridLength(1,GridUnitType.Star);
        Grid.SetColumn(info,stacked?0:1);Grid.SetRow(info,stacked?1:0);
        if(info is FrameworkElement element)element.Margin=stacked?new(0,10,0,0):new(0);
        if(grid.Children[0] is FrameworkElement files)files.Margin=stacked?new(0):new(0,0,24,0);
    }
    void WatchDownloadClick(object sender,RoutedEventArgs e){if(sender is FrameworkElement {Tag:DownloadItem item})WatchDownload(item);}
    void DownloadMoreClick(object sender,RoutedEventArgs e)
    {
        if(sender is not Button {Tag:DownloadItem item} button)return;
        var menu=new ContextMenu{PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,HorizontalOffset=0,VerticalOffset=4};downloadMenus.Add(menu);
        MenuItem Entry(string text,string icon,string name,RoutedEventHandler click,string? tip=null,bool danger=false)
        {
            var entry=MenuEntry(text,icon);entry.Tag=item;AutomationProperties.SetName(entry,name);entry.ToolTip=tip;entry.Click+=click;
            if(danger){entry.SetResourceReference(Control.ForegroundProperty,"Danger");if(entry.Icon is Path glyph){BindingOperations.ClearBinding(glyph,Shape.StrokeProperty);glyph.SetResourceReference(Shape.StrokeProperty,"Danger");}}
            menu.Items.Add(entry);return entry;
        }
        Entry("Сведения о торренте","IconInfo","Сведения о торренте",DownloadDetails,"Файлы, серии и источники");
        Entry("Почему не скачивается?","IconAlert","Почему не скачивается?",ShowDownloadDiagnostics,"Проверить соединения и свободное место");
        menu.Items.Add(new Separator());
        Entry("Убрать из списка","IconQueueRemove","Удалить из загрузок",RemoveDownload,"Удалить из загрузок, сохранив скачанные файлы");
        Entry("Удалить вместе с файлами","IconTrash","Удалить файлы",DeleteDownloadFiles,"Удалить загрузку вместе со скачанными и частичными файлами",true);
        button.ContextMenu=menu;menu.IsOpen=true;
    }
    // ----- drag and drop / paste -----
    void InitializeDownloadDrop()
    {
        AllowDrop=true;
        DragOver+=(_,e)=>{e.Effects=DropPayload(e.Data)!=null?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=async(_,e)=>
        {
            var payload=DropPayload(e.Data);if(payload==null)return;e.Handled=true;
            foreach(var source in payload)await AddDroppedSource(source);
        };
        PreviewKeyDown+=async(_,e)=>
        {
            if(section!="Загрузки"||e.Key!=Key.V||!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)||Keyboard.FocusedElement is TextBox)return;
            string text;try{text=Clipboard.ContainsText()?Clipboard.GetText().Trim():"";}catch{return;}
            if(!text.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase))return;
            e.Handled=true;await AddDroppedSource(text);
        };
    }
    static List<string>? DropPayload(IDataObject data)
    {
        var result=new List<string>();
        if(data.GetDataPresent(DataFormats.FileDrop)&&data.GetData(DataFormats.FileDrop) is string[] files)result.AddRange(files.Where(x=>x.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase)));
        if(result.Count==0&&data.GetDataPresent(DataFormats.UnicodeText)&&data.GetData(DataFormats.UnicodeText) is string text&&text.Trim().StartsWith("magnet:",StringComparison.OrdinalIgnoreCase))result.Add(text.Trim());
        return result.Count==0?null:result;
    }
    async Task AddDroppedSource(string source)
    {
        if(closing||closed)return;
        if(!EnsureDownloadFolder()){Status.Text="Папка для загрузок не выбрана. Её можно выбрать в настройках.";return;}
        try
        {
            var added=await downloads.Add(source,prefs.Folder);if(closing||closed)return;
            section="Загрузки";current=null;activePerson=null;Render();
            Status.Text=added.LowSpacePaused?added.SpacePauseMessage:"Раздача добавлена. Ищем участников.";
        }
        catch(Exception error)when(!closing&&!closed){Status.Text=error.Message;}
    }
    async void RepairDownloadMetadata()
    {
        if(closed||downloadMetadataBusy||downloads.Items.Count==0)return;
        // Retry when a magnet learns its real name or the local catalog gains
        // metadata, rather than rereading the persisted indexes every tick.
        var known=prefs.LiveFavorites.Concat(catalogIndex.Recent("Фильмы",200)).Concat(catalogIndex.Recent("Сериалы",200)).Concat(Catalog.Items).ToArray();
        var identity=System.Text.Json.JsonSerializer.Serialize(new
        {
            CatalogRevision=catalogIndex.Revision,
            Queue=downloads.Items.Select(item=>new{item.Id,item.Name,item.InfoHash,item.MediaTitle,item.MediaSection,item.MediaYear,item.ImageUrl,item.MediaPageUrl,item.ReleaseTitle,item.ReleaseId,item.ReleaseSource,item.ReleasePageUrl,item.ReleaseUrl}),
            Catalog=known.Select(item=>new{item.Id,item.Title,item.OriginalTitle,item.Year,item.Section,item.ImageUrl,item.PageUrl})
        });
        var fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        if(downloadMetadataFingerprint==fingerprint)return;
        downloadMetadataFingerprint=fingerprint;downloadMetadataBusy=true;
        try
        {
            if(await DownloadMetadata.RepairAsync(downloads,known,updateCancellation.Token)>0&&!closed)
            {
                downloadMetadataSortPending=true;RefreshDownloadView();
            }
        }
        catch(OperationCanceledException)when(closed||updateCancellation.IsCancellationRequested){}
        catch(Exception error){if(!closed)Status.Text="Не удалось восстановить обложки: "+error.Message;}
        finally{downloadMetadataBusy=false;}
    }
    ContextMenu Menu()
    {
        var menu=new ContextMenu{Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,HorizontalOffset=0,VerticalOffset=4};downloadMenus.Add(menu);return menu;
    }
    MenuItem MenuEntry(string label,string? icon=null)
    {
        var entry=new MenuItem{Header=label};AutomationProperties.SetName(entry,label);
        if(icon!=null){var path=new Path{Data=(Geometry)FindResource(icon),Width=16,Height=16,Stretch=Stretch.Uniform,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};path.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){Source=entry});entry.Icon=path;}
        return entry;
    }
    void OpenDownloadMenu(Button button){var menu=button.ContextMenu;if(menu==null)return;menu.PlacementTarget=button;menu.IsOpen=true;}
    void DownloadLimits()
    {
        var dialog=new Window{ShowInTaskbar=false,Title="Лимиты скорости",Owner=this,Width=Math.Min(440,Math.Max(320,ActualWidth-32)),MaxHeight=Math.Max(260,SystemParameters.WorkArea.Height-40),SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel{Margin=new(24)};dialog.Content=new ScrollViewer{Content=panel,Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var title=Text("Скорость передачи",22);title.FontWeight=FontWeights.SemiBold;panel.Children.Add(title);panel.Children.Add(Text("Общий лимит для всей очереди. 0 — без ограничения.",12,true));
        TextBox Field(string label,int value){panel.Children.Add(Text(label,12));var field=new TextBox{Text=value.ToString(),Margin=new(0,0,0,14)};AutomationProperties.SetName(field,label);panel.Children.Add(field);return field;}
        var down=Field("Загрузка, КБ/с",downloads.DownloadLimitKbps);var up=Field("Отдача, КБ/с",downloads.UploadLimitKbps);var error=Text("",12);error.SetResourceReference(TextBlock.ForegroundProperty,"Danger");panel.Children.Add(error);
        var actions=new WrapPanel();panel.Children.Add(actions);
        var apply=AsyncButton("Применить",async()=>
        {
            if(!int.TryParse(down.Text,out var d)||!int.TryParse(up.Text,out var u)||d<0||u<0||d>int.MaxValue/1024||u>int.MaxValue/1024){error.Text="Укажи целое число от 0 до 2097151 КБ/с.";return;}
            var oldD=downloads.DownloadLimitKbps;var oldU=downloads.UploadLimitKbps;
            try
            {
                await downloads.SetLimitsAsync(d,u);prefs.MaxDownloadKbps=d;prefs.MaxUploadKbps=u;
                try{prefs.Save();}catch{prefs.MaxDownloadKbps=oldD;prefs.MaxUploadKbps=oldU;await downloads.SetLimitsAsync(oldD,oldU);throw;}
                Status.Text="Лимиты скорости сохранены.";dialog.Close();
            }catch(Exception ex){error.Text=ex.Message;}
        });apply.Style=(Style)FindResource("PrimaryButton");apply.IsDefault=true;AutomationProperties.SetName(apply,"Применить лимиты");actions.Children.Add(apply);var cancel=Button("Отмена",dialog.Close);cancel.IsCancel=true;actions.Children.Add(cancel);dialog.ShowDialog();
    }
    async void DeleteDownloadFiles(object sender,RoutedEventArgs e)
    {
        var item=(DownloadItem)((FrameworkElement)sender).Tag;
        if(item.Busy){Status.Text="Подожди завершения текущей операции с загрузкой.";return;}
        if(MessageBox.Show(this,"Удалить «"+item.DisplayName+"» из загрузок вместе со скачанными и частичными файлами?\n\nЭто действие нельзя отменить. Другие файлы в папке останутся.","Удалить загрузку и файлы",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        if(item.Busy){Status.Text="Загрузка ещё занята. Повтори удаление после завершения операции.";return;}
        try{await downloads.Remove(item,true);if(downloads.Items.Contains(item)){Status.Text="Загрузка ещё занята. Повтори удаление после завершения операции.";return;}Render();Status.Text="Загрузка и её файлы удалены.";}catch(Exception error){Status.Text="Не удалось удалить файлы: "+error.Message;}finally{SyncTimer();}
    }
}

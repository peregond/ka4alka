using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kachalka;
public partial class MainWindow
{
    void OpenDownloadCard(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.Tag is not DownloadItem item)return;
        var card=DownloadMetadata.Card(item);if(card==null)return;
        downloadReturnItem=item;
        current=DownloadMetadata.EnrichMedia(card,new[]{sharedCatalog.Find(card)}.OfType<MediaItem>().Concat(prefs.LiveFavorites).Concat(catalogIndex.Recent(card.Section,200)).Concat(BundledCatalog.Search(card.Section,card.Title)));
        Render();
    }
    ListCollectionView? downloadView;
    ListBox? downloadList;
    DownloadItem? downloadReturnItem;
    TextBlock? downloadSummary;
    TextBlock? downloadHeading;
    Button? downloadControls,downloadOrder;
    string downloadSort="newest";
    bool downloadCommandBusy;
    bool downloadMetadataBusy,downloadMetadataSortPending;
    string downloadMetadataFingerprint="";
    readonly List<ContextMenu> downloadMenus=[];
    static readonly (string Key,string Label,DownloadSort Sort)[] DownloadSortChoices=[("newest","Сначала новые",DownloadSort.Newest),("name","По названию",DownloadSort.Name),("speed","По скорости",DownloadSort.Speed),("size","По размеру",DownloadSort.Size),("progress","По готовности",DownloadSort.Progress)];
    void RenderDownloads()
    {
        downloads.Update();downloadMenus.Clear();
        foreach(var item in downloads.Items){item.MinimumQualityHeight=QualityMinimum;item.Refresh();}
        RepairDownloadMetadata();
        downloadSort=DownloadSortChoices.Any(x=>x.Key==prefs.DownloadSort)?prefs.DownloadSort:"newest";
        var heading=new DockPanel{Margin=new(0,0,8,8)};
        var title=Text("Очередь",25);downloadHeading=title;title.FontWeight=FontWeights.SemiBold;title.Margin=new(0);heading.Children.Add(title);PageHeader.Children.Add(heading);
        var toolbar=new WrapPanel{Margin=new(0,4,0,10)};PageHeader.Children.Add(toolbar);
        var controls=ActionButton("Управление","IconSettings",()=>{},"QuietButton");downloadControls=controls;controls.Margin=new(0,0,8,4);controls.Padding=new(10,6,10,6);controls.ToolTip="Управление загрузками и раздачами";AutomationProperties.SetName(controls,"Управление загрузками");
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
        var order=ActionButton(DownloadSortChoices.First(x=>x.Key==downloadSort).Label,"IconFilter",()=>{},"QuietButton");downloadOrder=order;order.Padding=new(10,6,10,6);order.Margin=new(0,0,8,4);AutomationProperties.SetName(order,"Сортировка загрузок");order.ContextMenu=Menu();
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
        order.Click+=(_,_)=>OpenDownloadMenu(order);toolbar.Children.Add(order);
        downloadSummary=Text("",12,true);downloadSummary.TextWrapping=TextWrapping.NoWrap;downloadSummary.TextTrimming=TextTrimming.CharacterEllipsis;downloadSummary.Margin=new(0,0,0,12);PageHeader.Children.Add(downloadSummary);UpdateDownloadSummary();FitDownloadsToolbar();
        if(downloads.Items.Count>0)
        {
            downloadView=new ListCollectionView(downloads.Items){CustomSort=DownloadOrdering.Comparer(DownloadSortChoices.First(x=>x.Key==downloadSort).Sort)};
            downloadList=new ListBox{ItemsSource=downloadView,ItemTemplate=(DataTemplate)FindResource("DownloadRow")};AutomationProperties.SetName(downloadList,"Очередь загрузок");
            if(downloadReturnItem is {} returned&&downloads.Items.Contains(returned))
            {
                var list=downloadList;list.Loaded+=(_,_)=>list.ScrollIntoView(returned);
            }
            downloadReturnItem=null;Body.Children.Add(downloadList);return;
        }
        var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=360};
        var illustration=IconLabel("","IconDownload",32);foreach(var path in VisualElements<Path>(illustration)){BindingOperations.ClearBinding(path,Shape.StrokeProperty);path.SetResourceReference(Shape.StrokeProperty,"Muted");}illustration.HorizontalAlignment=HorizontalAlignment.Center;illustration.Margin=new(0,0,0,18);empty.Children.Add(illustration);
        var caption=Text("Загрузок пока нет",22);caption.TextAlignment=TextAlignment.Center;empty.Children.Add(caption);
        var hint=Text("Выбери раздачу в карточке фильма или добавь magnet-ссылку либо .torrent-файл.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);
        empty.Children.Add(ActionButton("Добавить торрент","IconPlus",()=>AddTorrent(this,new RoutedEventArgs()),"PrimaryButton"));Body.Children.Add(empty);
    }
    void FitDownloadsToolbar()
    {
        if(section!="Загрузки"||downloadControls==null||downloadOrder==null)return;
        var tiny=ActualWidth>0&&ActualWidth<560;var shortView=ActualHeight>0&&ActualHeight<560;
        downloadControls.Content=IconLabel(tiny?"":"Управление","IconSettings");
        var label=DownloadSortChoices.FirstOrDefault(x=>x.Key==downloadSort).Label??"Сначала новые";
        downloadOrder.Content=IconLabel(tiny?"":label,"IconFilter");downloadOrder.ToolTip="Сортировка: "+label;
        if(downloadHeading!=null)downloadHeading.FontSize=shortView?20:25;
        if(downloadSummary!=null)downloadSummary.Margin=new(0,0,0,shortView?8:12);
    }
    void UpdateDownloadSummary()
    {
        if(downloadSummary==null)return;
        var active=downloads.Items.Count(x=>!x.Paused&&!x.Completed);var seeding=downloads.Items.Count(x=>!x.Paused&&x.Completed);
        downloadSummary.Text=$"Всего: {downloads.Items.Count} · Загружается: {active} · Раздаётся: {seeding}";
    }
    void RefreshDownloadView()
    {
        if(section!="Загрузки"||downloadView==null)return;RepairDownloadMetadata();UpdateDownloadSummary();
        if(downloadSort is not ("speed" or "progress" or "size")&&!(downloadSort=="name"&&downloadMetadataSortPending))return;
        // Keep the row under the pointer stable while the user chooses an action.
        if(downloadList?.IsMouseOver==true||downloadList?.IsKeyboardFocusWithin==true||downloadMenus.Any(menu=>menu.IsOpen))return;
        var order=DownloadSortChoices.First(x=>x.Key==downloadSort).Sort;
        if(downloadView.Cast<DownloadItem>().Select(x=>x.Id).SequenceEqual(DownloadOrdering.Sort(downloads.Items,order).Select(x=>x.Id))){downloadMetadataSortPending=false;return;}
        var selected=downloadList?.SelectedItem;downloadView.Refresh();if(downloadList!=null&&selected!=null)downloadList.SelectedItem=selected;
        downloadMetadataSortPending=false;
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
        var dialog=new Window{Title="Лимиты скорости",Owner=this,Width=Math.Min(440,Math.Max(320,ActualWidth-32)),MaxHeight=Math.Max(260,SystemParameters.WorkArea.Height-40),SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel{Margin=new(22)};dialog.Content=new ScrollViewer{Content=panel,Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
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

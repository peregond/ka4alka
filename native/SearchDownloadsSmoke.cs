using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;
public partial class MainWindow
{
    async Task SearchDownloadsSmoke(string output,Action<bool,string> check)
    {
        current=null;section="Фильмы";favoritesOnly=false;
        var films=Enumerable.Range(1,45).Select(id=>new MediaItem(1300+id,"Поисковая искра · фильм "+id,"Фильмы","драма",2024,"8.0","8.0","#526B69")).ToArray();
        var series=Enumerable.Range(1,5).Select(id=>new MediaItem(2300+id,"Поисковая искра · сериал "+id,"Сериалы","драма",2024,"8.0","8.0","#526B69")).ToArray();
        foreach(var item in films.Concat(series))cardMetadata[item.Id]=Task.FromResult(item);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
        searchProvider=async(kind,query,token)=>{calls++;await release.Task.WaitAsync(token);return kind=="Фильмы"?films:series;};
        Search.Text="Поисковая искра";check(!SearchActive,"typing does not submit a query before Search or Enter");
        SubmitSearch(Search,new RoutedEventArgs());UpdateLayout();
        check(SearchActive&&searchCategory==""&&LoadingIndicator.Visibility==Visibility.Visible,"submitted query defaults to both types with visible animated loading indicator");
        await Task.Delay(100);check(calls==2,"search dispatches film and series requests together");release.SetResult();
        var end=DateTime.UtcNow.AddSeconds(15);while(liveLoading&&DateTime.UtcNow<end)await Task.Delay(30);UpdateLayout();
        check(!liveLoading&&LoadingIndicator.Visibility==Visibility.Collapsed&&liveItems.Count==50&&catalogDisplay.Any(x=>x.Section=="Сериалы")&&catalogDisplay.Any(x=>x.Section=="Фильмы"),"combined results include both types and hide completed loading indicator");
        Button Filter(string type)=>FindVisual<Button>(PageHeader,b=>AutomationProperties.GetName(b)=="Результаты: "+type)??throw new Exception("Missing search type "+type);
        Filter("Сериалы").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
        check(catalogDisplay.Count==5&&catalogDisplay.All(x=>x.Section=="Сериалы")&&calls==2,"series tab filters existing results without another network request");
        Filter("Фильмы").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();check(catalogDisplay.Count==40&&catalogHasNext,"film results keep paged navigation");
        ShowDownloads(this,new RoutedEventArgs());ShowCatalogSection("Фильмы");UpdateLayout();
        check(SearchActive&&Search.Text=="Поисковая искра"&&searchCategory=="Фильмы"&&calls==2,"returning from downloads restores submitted query and selected search type");
        ShowCatalogSection("Фильмы");await Task.Delay(150);UpdateLayout();
        check(!SearchActive&&Search.Text==""&&CatalogSelection.IsDefault,"repeated active catalog navigation clears query and filters");searchProvider=null;

        var folder=Path.Combine(Preferences.DataDir,"details-fixture");Directory.CreateDirectory(folder);
        downloads.Items.Clear();
        for(var i=0;i<6;i++)downloads.Items.Add(new DownloadItem{Name="Сериал · сезон "+(i+1),Folder=folder,Progress=50,Paused=true,Stats="50% · 1.0 ГБ из 2.0 ГБ · ↓ 0 КБ/с",Files=[new("Сезон 1/Серия 01.mkv",Path.Combine(folder,"episode1.mkv"),Path.Combine(folder,"episode1.mkv"),1024*1024,100),new("Сезон 1/Серия 02.mkv",Path.Combine(folder,"episode2.mkv"),Path.Combine(folder,"episode2.mkv"),1024*1024,35)]});
        ShowDownloads(this,new RoutedEventArgs());UpdateLayout();
        foreach(var label in new[]{"Начать загрузки","Остановить загрузки","Запустить раздачи","Остановить раздачи"})check(FindVisual<Button>(PageHeader,b=>AutomationProperties.GetName(b)==label)!=null,"download toolbar includes "+label);
        var down=FindVisual<TextBox>(PageHeader,b=>AutomationProperties.GetName(b)=="Загрузка, КБ/с")!;var up=FindVisual<TextBox>(PageHeader,b=>AutomationProperties.GetName(b)=="Отдача, КБ/с")!;
        down.Text="512";up.Text="128";FindVisual<Button>(PageHeader,b=>b.Content?.ToString()=="Применить")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Task.Delay(50);
        check(downloads.DownloadLimitKbps==512&&downloads.UploadLimitKbps==128&&Preferences.Load().MaxUploadKbps==128,"speed-limit controls apply and persist both rates");
        var list=FindVisual<ListBox>(Body,_=>true)!;var first=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);check(first.ActualHeight<155&&first.ActualHeight>0,"download rows use compact height below 155 pixels");
        check(VisualElements<Button>(first).Any(b=>AutomationProperties.GetName(b)=="Удалить загрузку и файлы")&&VisualElements<Button>(first).Any(b=>AutomationProperties.GetName(b)=="Убрать из очереди"),"keep-files and delete-files actions are separate");
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"downloads.png")))png.Save(file);
        Exception? detailError=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>
        {
            var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Owner==this&&w.Title.StartsWith("Файлы · "));
            if(dialog==null)return;timer.Stop();
            try{dialog.UpdateLayout();var files=FindVisual<ListBox>(dialog,b=>AutomationProperties.GetName(b)=="Файлы загрузки")!;check(files.Items.Count==2&&VisualElements<TextBlock>(dialog).Any(t=>t.Text.Contains("Готово файлов: 1 из 2")),"details show episode names, individual progress and completed-file count");}
            catch(Exception error){detailError=error;}finally{dialog.Close();}
        };
        timer.Start();FindVisual<Button>(first,b=>b.Content?.ToString()=="Подробнее")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));timer.Stop();if(detailError!=null)throw detailError;
        downloads.Items.Clear();
        check(SidebarUpdateButton.Visibility==Visibility.Collapsed,"sidebar update prompt stays hidden until a package is ready");
        preparedUpdateJob="fixture";RefreshSidebarUpdate();UpdateLayout();
        check(SidebarUpdateButton.Visibility==Visibility.Visible&&SidebarUpdateButton.IsEnabled&&SidebarUpdateButton.TransformToAncestor(SidebarFooter).Transform(new Point()).Y<DownloadsButton.TransformToAncestor(SidebarFooter).Transform(new Point()).Y,"ready update appears above Downloads in the sidebar");
        check(!SystemParameters.ClientAreaAnimation||SidebarUpdateButton.HasAnimatedProperties,"ready update softly animates when system animations are enabled");
        Width=680;await Task.Delay(100);UpdateLayout();check(SidebarUpdateButton.Visibility==Visibility.Visible&&SidebarUpdateButton.ActualWidth>0,"sidebar update remains available in narrow layout");
        preparedUpdateJob=null;RefreshSidebarUpdate();Width=1280;
    }
}

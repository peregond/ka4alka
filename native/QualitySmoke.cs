using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace Kachalka;

public partial class MainWindow
{
    async Task QualitySmoke(Action<bool,string> check)
    {
        var oldHide=prefs.HidePoorQuality;var oldMinimum=prefs.MinimumReleaseHeight;var oldFavorites=prefs.LiveFavorites;
        var queued=new DownloadItem{Name="file.mkv",MediaTitle="Качество",ReleaseTitle="Качество HDCAM 1080p",Paused=true};
        try
        {
            prefs.HidePoorQuality=false;prefs.MinimumReleaseHeight=720;
            var items=Enumerable.Range(1,4).Select(id=>new MediaItem(-9000-id,"Качество "+id,"Фильмы","",2026,"—","—","#526B69"){PageUrl=LiveCatalog.Base+"/movies/quality-fixture-"+id}).ToArray();
            SourceEntry Row(string id,string quality)=>new(id,"Качество (2026) "+quality,"Fixture","","magnet:?xt=urn:btih:"+new string(id[0],40),null);
            var releases=new[]{new[]{Row("a","HDCAM 1080p")},new[]{Row("b","1080p")},new[]{Row("c","720p")},Array.Empty<SourceEntry>()};
            for(var i=0;i<items.Length;i++){requestedDetails.Add(items[i].Id);cardMetadata[items[i].Id]=Task.FromResult(items[i]);liveReleases[items[i].Id]=releases[i];releaseViews[items[i].Id]=new(){ReceivedUtc=DateTime.UtcNow};}
            await catalogIndex.CacheReleasesAsync(items[0],releases[0]);
            check(new CatalogIndex(Preferences.DataDir).CachedReleaseSnapshot(items[0]) is {Items.Length:1},"known poor-quality releases survive cache reload");
            prefs.LiveFavorites=items.ToList();favoritesOnly=true;current=null;section="Фильмы";submittedQuery="";searchCategory="";Search.Text="";searchDelay.Stop();liveLoading=false;livePage=1;Render();UpdateLayout();
            check(catalogDisplay.Count==4&&catalogDisplay.Count(x=>x.OnlyPoorQuality)==1,"catalog marks only confirmed poor-only titles and keeps unknown quality unmarked");
            check(VisualElements<Border>(Body).Any(x=>x.IsVisible&&AutomationProperties.GetName(x)=="Плохое качество фильма"&&Equals(x.ToolTip,"Плохое качество")),"catalog poster shows the poor-quality badge and its tooltip");
            var catalogToggle=FindVisual<Button>(PageHeader,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")??throw new Exception("Catalog quality toggle is missing.");
            check(catalogToggle.Parent is WrapPanel qualityRow&&ReferenceEquals(qualityRow.Parent,topSaved?.Parent),"catalog quality controls share the row with Saved and the other catalog filters");
            catalogToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
            check(catalogDisplay.Count==3&&Preferences.Load().HidePoorQuality,"top quality filter hides poor-only titles and persists the choice");
            check(AutomationProperties.GetItemStatus(FindVisual<Button>(PageHeader,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")!)=="Включён","enabled catalog quality filter exposes its active state");
            var minimum=FindVisual<Button>(PageHeader,x=>AutomationProperties.GetName(x)=="Минимальное качество")??throw new Exception("Catalog minimum-quality menu is missing.");
            minimum.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Task.Delay(80);UpdateLayout();
            check(minimum.ContextMenu is {IsOpen:true,ActualHeight:>0},"catalog quality minimum opens a visible menu in the filter row");
            minimum.ContextMenu!.Items.OfType<MenuItem>().Single(x=>Equals(x.Tag,1080)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));UpdateLayout();
            check(catalogDisplay.Count==2&&catalogDisplay.Any(x=>x.Id==items[3].Id)&&Preferences.Load().MinimumReleaseHeight==1080,"Full HD minimum hides 720p titles while retaining unknown-quality titles");
            prefs.HidePoorQuality=false;await catalogIndex.CacheReleasesAsync(items[3],[],[new("Fixture",SourceState.Empty,CheckedUtc:DateTime.UtcNow)]);
            favoritesOnly=false;liveItems=items;liveKey=CurrentCatalogKey;Render();UpdateLayout();
            check(catalogDisplay.Count==3&&!catalogDisplay.Any(x=>x.Id==items[3].Id),"feed hides a successfully checked title with no download options");
            favoritesOnly=true;Render();UpdateLayout();
            check(catalogDisplay.Count==4,"a hidden title remains accessible in Saved for future rechecking");
            prefs.HidePoorQuality=true;
            var selected=items[1];liveReleases[selected.Id]=[Row("a","HDCAM 1080p"),Row("b","720p"),Row("c","1080p"),Row("d","WEBRip")];
            current=selected;Render();UpdateLayout();
            check(VisualElements<Button>(Body).Select(x=>x.Tag).OfType<SourceEntry>().Count()==2,"the global filter carries into film detail and retains Full HD and unknown releases");
            FindVisual<CheckBox>(Body,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")!.IsChecked=false;UpdateLayout();
            check(VisualElements<Button>(Body).Select(x=>x.Tag).OfType<SourceEntry>().Count()==4&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="💩"&&Equals(x.ToolTip,"Плохое качество")),"disabling the filter restores all releases with poor-quality tooltips");
            prefs.HidePoorQuality=true;prefs.Save();downloads.Items.Add(queued);current=null;section="Загрузки";Render();UpdateLayout();
            check(downloadList?.Items.Contains(queued)==true&&VisualElements<TextBlock>(Body).Any(x=>x.IsVisible&&AutomationProperties.GetName(x)=="Плохое качество загрузки"&&Equals(x.ToolTip,"Плохое качество")),"existing poor-quality downloads stay in the queue and carry a visible badge");
            foreach(var width in new[]{360d,510d,1280d})
            {
                MinWidth=360;Width=width;Height=640;await Task.Delay(50);current=selected;section="Фильмы";Render();UpdateLayout();
                check(FindVisual<CheckBox>(Body,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество") is {ActualHeight:>0}&&Body.ActualWidth>100,"quality controls remain available at window width "+width);
                current=null;Render();UpdateLayout();
                check(FindVisual<Button>(PageHeader,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество") is {IsVisible:true,ActualWidth:>0,ActualHeight:>0}&&FindVisual<Button>(PageHeader,x=>AutomationProperties.GetName(x)=="Минимальное качество") is {IsVisible:true,ActualWidth:>0},"catalog quality buttons remain available in the filter row at window width "+width);
            }
        }
        finally
        {
            downloads.Items.Remove(queued);prefs.HidePoorQuality=oldHide;prefs.MinimumReleaseHeight=oldMinimum;prefs.LiveFavorites=oldFavorites;prefs.Save();favoritesOnly=false;current=null;MinWidth=1280;Width=1280;Height=800;
        }
    }
}

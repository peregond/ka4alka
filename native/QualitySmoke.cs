using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace Kachalka;

public partial class MainWindow
{
    async Task QualitySmoke(Action<bool,string> check)
    {
        var oldHide=prefs.HidePoorQuality;var oldMinimum=prefs.MinimumReleaseHeight;var oldCatalogHeight=prefs.CatalogQualityHeight;var oldFavorites=prefs.LiveFavorites;
        var queued=new DownloadItem{Name="file.mkv",MediaTitle="Качество",ReleaseTitle="Качество HDCAM 1080p",Paused=true};
        try
        {
            prefs.HidePoorQuality=false;prefs.MinimumReleaseHeight=720;prefs.CatalogQualityHeight=0;
            var items=Enumerable.Range(1,7).Select(id=>new MediaItem(-9000-id,"Качество "+id,"Фильмы","",2026,"—","—","#526B69"){PageUrl=LiveCatalog.Base+"/movies/quality-fixture-"+id}).ToArray();
            SourceEntry Row(string id,string quality)=>new(id,"Качество (2026) "+quality,"Fixture","","magnet:?xt=urn:btih:"+new string(id[0]=='g'?'0':id[0],40),null);
            var releases=new[]{new[]{Row("a","HDCAM 2160p")},new[]{Row("b","1080p")},new[]{Row("c","720p")},Array.Empty<SourceEntry>(),new[]{Row("d","2160p")},new[]{Row("e","720p"),Row("f","2160p")},new[]{Row("g","WEBRip")}};
            for(var i=0;i<items.Length;i++){requestedDetails.Add(items[i].Id);cardMetadata[items[i].Id]=Task.FromResult(items[i]);liveReleases[items[i].Id]=releases[i];releaseViews[items[i].Id]=new(){ReceivedUtc=DateTime.UtcNow};}
            await catalogIndex.CacheReleasesAsync(items[0],releases[0]);
            check(new CatalogIndex(Preferences.DataDir).CachedReleaseSnapshot(items[0]) is {Items.Length:1},"known poor-quality releases survive cache reload");
            prefs.LiveFavorites=items.ToList();favoritesOnly=true;current=null;section="Фильмы";submittedQuery="";searchCategory="";Search.Text="";searchDelay.Stop();liveLoading=false;livePage=1;Render();UpdateLayout();
            check(catalogDisplay.Count==7&&catalogDisplay.Count(x=>x.OnlyPoorQuality)==1,"catalog marks only confirmed poor-only titles and keeps unknown quality unmarked");
            check(VisualElements<Border>(Body).Any(x=>x.IsVisible&&AutomationProperties.GetName(x)=="Качество на обложке"&&Equals(x.ToolTip,"Экранка")),"catalog poster shows the poor-quality badge and its tooltip");
            Button QualityButton()=>FindVisual<Button>(RootGrid,x=>AutomationProperties.GetName(x)=="Качество каталога")??throw new Exception("Catalog quality menu is missing.");
            async Task ChooseQuality(object choice)
            {
                var button=QualityButton();button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Task.Delay(50);UpdateLayout();
                check(button.ContextMenu is {IsOpen:true,ActualHeight:>0},"single catalog quality button opens its menu for "+choice);
                button.ContextMenu!.Items.OfType<MenuItem>().Single(x=>Equals(x.Tag,choice)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));UpdateLayout();
            }
            check(VisualElements<Button>(inlineCatalogFilters!).Count(x=>AutomationProperties.GetName(x)=="Качество каталога")==1,"catalog combines all quality choices in one button");
            var options=QualityButton().ContextMenu!.Items.OfType<MenuItem>().ToArray();
            check(options.Where(x=>x.Tag is int).Select(x=>(int)x.Tag).SequenceEqual([0,2160,1080,720])&&options.Where(x=>x.Tag is int).Select(x=>x.Header?.ToString()).SequenceEqual(["Любое качество","4K","FullHD","HD Ready"]),"quality menu presents exact resolution choices instead of minimum thresholds");
            check(options.Any(x=>Equals(x.Tag,"hide-poor")&&x.IsCheckable),"quality menu includes the poor-quality toggle");
            await ChooseQuality("hide-poor");
            check(catalogDisplay.Count==6&&Preferences.Load().HidePoorQuality,"menu quality toggle hides poor-only titles and persists the choice");
            check(QualityButton().ContextMenu!.Items.OfType<MenuItem>().Single(x=>Equals(x.Tag,"hide-poor")).IsChecked,"enabled poor-quality toggle exposes its checked state");
            await ChooseQuality(2160);
            check(catalogDisplay.Select(x=>x.Id).Order().SequenceEqual(new[]{items[4].Id,items[5].Id}.Order()),"4K selection shows titles with downloadable 2160p releases and excludes unknown quality and screen recordings");
            await ChooseQuality(1080);
            check(catalogDisplay.Select(x=>x.Id).SequenceEqual([items[1].Id]),"FullHD selection excludes 720p and 2160p-only titles");
            await ChooseQuality(720);
            check(catalogDisplay.Select(x=>x.Id).Order().SequenceEqual(new[]{items[2].Id,items[5].Id}.Order()),"HD Ready selection keeps exact 720p availability including titles that also have 4K");
            await ChooseQuality(720);
            check(catalogDisplay.Count==6&&prefs.CatalogQualityHeight==0,"selecting an active resolution again returns to any quality");
            await ChooseQuality("hide-poor");await ChooseQuality(2160);
            check(!prefs.HidePoorQuality&&catalogDisplay.Count==2&&!catalogDisplay.Any(x=>x.Id==items[0].Id),"a 4K screen recording does not qualify as a 4K release even when poor-quality hiding is disabled");
            await ChooseQuality(1080);
            check(Preferences.Load().CatalogQualityHeight==1080&&Preferences.Load().MinimumReleaseHeight==720,"catalog resolution persists independently of the release minimum");
            await ChooseQuality(0);
            prefs.HidePoorQuality=false;await catalogIndex.CacheReleasesAsync(items[3],[],[new("Fixture",SourceState.Empty,CheckedUtc:DateTime.UtcNow)]);
            favoritesOnly=false;liveItems=items;liveKey=CurrentCatalogKey;Render();UpdateLayout();
            check(catalogDisplay.Count==6&&!catalogDisplay.Any(x=>x.Id==items[3].Id),"feed hides a successfully checked title with no download options");
            favoritesOnly=true;Render();UpdateLayout();
            check(catalogDisplay.Count==7,"a hidden title remains accessible in Saved for future rechecking");
            prefs.HidePoorQuality=true;prefs.MinimumReleaseHeight=1080;prefs.CatalogQualityHeight=1080;prefs.Save();
            var selected=items[1];liveReleases[selected.Id]=[Row("a","HDCAM 1080p"),Row("b","720p"),Row("c","1080p"),Row("d","WEBRip"),Row("e","2160p")];
            current=selected;Render();UpdateLayout();
            check(!FiltersPanel.IsVisible&&prefs.CatalogQualityHeight==1080,"opening a film hides catalog filters while retaining the selected resolution");
            check(VisualElements<Button>(Body).Where(x=>x.Name!="DetailDownload").Select(x=>x.Tag).OfType<SourceEntry>().Count()==3,"film detail retains its separate minimum and shows Full HD, 4K and unknown releases");
            FindVisual<CheckBox>(Body,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")!.IsChecked=false;UpdateLayout();
            check(VisualElements<Button>(Body).Where(x=>x.Name!="DetailDownload").Select(x=>x.Tag).OfType<SourceEntry>().Count()==5&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="Экранка"&&x.ToolTip is string tooltip&&tooltip.Contains("плохое качество")),"disabling the filter restores all releases with poor-quality tooltips");
            prefs.HidePoorQuality=true;prefs.Save();downloads.Items.Add(queued);current=null;section="Загрузки";Render();UpdateLayout();
            check(downloadList?.Items.Contains(queued)==true&&VisualElements<Border>(Body).Any(x=>x.IsVisible&&AutomationProperties.GetName(x)=="Качество загрузки"&&Equals(x.ToolTip,"Экранка")),"existing poor-quality downloads stay in the queue and carry a visible badge");
            foreach(var width in new[]{360d,510d,1280d})
            {
                MinWidth=360;Width=width;Height=640;await Task.Delay(50);current=selected;section="Фильмы";ReleaseSelection(selected.Id).More=true;Render();UpdateLayout();
                check(FindVisual<CheckBox>(Body,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество") is {ActualHeight:>0}&&Body.ActualWidth>100,"quality controls remain available at window width "+width);
                current=null;Render();UpdateLayout();
                check(QualityButton() is {IsVisible:true,ActualWidth:>0,ActualHeight:>0},"single catalog quality dropdown remains available in the filter line at window width "+width);
                CheckCatalogFilterLine(check,"quality-width-"+width);
            }
        }
        finally
        {
            downloads.Items.Remove(queued);prefs.HidePoorQuality=oldHide;prefs.MinimumReleaseHeight=oldMinimum;prefs.CatalogQualityHeight=oldCatalogHeight;prefs.LiveFavorites=oldFavorites;prefs.Save();favoritesOnly=false;current=null;MinWidth=1280;Width=1280;Height=800;
        }
    }
}

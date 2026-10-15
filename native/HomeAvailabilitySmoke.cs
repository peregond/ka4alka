using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Kachalka;

public partial class MainWindow
{
    public async Task HomeAvailabilitySmokeTest(string output)
    {
        Directory.CreateDirectory(output);metadataLifetime.Cancel();liveRequest?.Cancel();searchDelay.Stop();catalogRefreshTimer.Stop();refresh.Stop();ResetDiscoveryData();CancelCatalogQualityCheck();
        var settingsPath=Path.Combine(Preferences.DataDir,"settings.json");var original=File.Exists(settingsPath)?File.ReadAllBytes(settingsPath):null;
        var oldProvider=catalogQualityProvider;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=new List<int>();var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        async Task Until(Func<bool> condition)
        {
            for(var i=0;i<240;i++){UpdateLayout();if(condition())return;await Task.Delay(25);}throw new Exception("Home availability fixture did not settle.");
        }
        SourceEntry Link(int id)=>new("home-"+id,"Проверка (2026) WEB-DL 1080p RUS","Fixture","","magnet:?xt=urn:btih:"+new string('a',40),null);
        try
        {
            foreach(var category in new[]{"Фильмы","Сериалы"})
            {
                CancelCatalogQualityCheck();ResetDiscoveryData();qualitySnapshots.Clear();catalogQualityAttempts.Clear();catalogIndex.ClearReleaseMemory();
                section=category;current=null;activePerson=null;submittedQuery="";Search.Text="";searchDelay.Stop();favoritesOnly=false;livePage=1;ResetCatalogFilters();
                prefs.CatalogQualityHeight=0;prefs.HidePoorQuality=false;prefs.HiddenTitles=[];prefs.HiddenGenres=[];
                var home=CatalogRegions.HomeCountry(prefs.HomeCountry);var away=home=="ssha"?"rossiia":"ssha";
                MediaItem Item(int id,string title,string country)=>new(id,title,category,"драма",2026,"7","7","#526B69"){PageUrl=LiveCatalog.Base+(category=="Фильмы"?"/movies/":"/tvseries/")+"home-fixture-"+id,GenreKeys=["drama"],CountryKeys=[country]};
                var good=Item(category=="Фильмы"?-998701:-998711,"Доступная карточка",away);
                var empty=Item(good.Id-1,"Без раздач",home);
                var popular=Item(good.Id-2,"Доступное популярное",away);
                var native=Item(good.Id-3,"Доступное отечественное",home);
                var fixtures=new[]{good,empty,popular,native};
                foreach(var item in fixtures)cardMetadata[item.Id]=Task.FromResult(item);
                CatalogPage Page(MediaItem[] items)=>new(items,false,CatalogChoices.Genres,CatalogChoices.Countries);
                liveItems=[good,empty];liveLoading=false;liveError="";catalogPages[category+"|sort-date|1"]=Page([good,empty]);catalogPages[category+"||1"]=Page([popular,empty]);
                SetDiscoveryRegionalItems(category,home,[good],[native,empty]);
                ready=new(TaskCreationOptions.RunContinuationsAsynchronously);calls.Clear();
                catalogQualityProvider=async(item,token)=>{calls.Add(item.Id);await ready.Task.WaitAsync(token);return item.Id==empty.Id?[]:[Link(item.Id)];};
                liveKey=CurrentCatalogKey;Render();UpdateLayout();
                Check(catalogDisplay.Count==0&&carousel==null,category+": unchecked records do not populate the cold homepage or banner");
                await Until(()=>calls.Count>0);ready.SetResult();await Until(()=>catalogQualityRequest==null&&catalogDisplay.Any(item=>item.Id==good.Id)&&discoveryShelves[DiscoveryShelfKind.Native].Row.Content is CatalogRow row&&row.Items.Any(item=>item.Id==native.Id));
                Check(calls.Distinct().Count()==4,category+": main, popular and late regional records share one bounded check queue");
                Check(catalogDisplay.All(item=>item.Id!=empty.Id)&&carousel?.Slides.All(item=>item.Id!=empty.Id)==true&&discoveryShelves.Values.All(view=>view.Row.Content is not CatalogRow row||row.Items.All(item=>item.Id!=empty.Id)),category+": an empty reply stays out of all recommendation surfaces");
                Check(liveItems.Any(item=>item.Id==empty.Id),category+": metadata remains available for deliberate search");
                var menu=CatalogPersonalMenu(good);((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Render();UpdateLayout();
                Check(catalogDisplay.All(item=>item.Id!=good.Id)&&discoveryShelves.Values.All(view=>view.Row.Content is not CatalogRow row||row.Items.All(item=>item.Id!=good.Id)),category+": a hidden title disappears from homepage and shelves");
                prefs.HiddenTitles.Clear();var genreMenu=(MenuItem)CatalogPersonalMenu(good).Items[1];var genreOption=(MenuItem)genreMenu.Items[0];genreOption.IsChecked=true;genreOption.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Render();UpdateLayout();
                Check(catalogDisplay.Count==0&&carousel==null,category+": hidden genre filters banners and the main catalog");
                prefs.HiddenGenres.Clear();prefs.Save();Render();UpdateLayout();Check(catalogDisplay.Any(item=>item.Id==good.Id),category+": restoring choices returns the title without another network scan");
                qualitySnapshots[good.Id]=new(DateTime.UtcNow.AddMinutes(-10),[]);
                Render();UpdateLayout();Check(catalogDisplay.Any(item=>item.Id==good.Id)&&good.HasReleaseResolution(1080),category+": newer remembered releases replace an older empty quality view after returning from details");
            }
            section="Настройки";current=null;Render();UpdateLayout();
            Check(VisualElements<TextBlock>(Body).Any(text=>text.Text=="Игнорируемое"),"native Settings expose the reversible hidden-content controls");
            await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,NativeHomeAvailability=true,MovieAndSeries=true,PersonalChoices=true,BoundedQueue=true}));
        }
        finally
        {
            ready.TrySetResult();CancelCatalogQualityCheck();catalogQualityProvider=oldProvider;
            if(original!=null)File.WriteAllBytes(settingsPath,original);else if(File.Exists(settingsPath))File.Delete(settingsPath);
        }
        Close();
    }
}

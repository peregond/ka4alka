using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    public async Task PosterActionsSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        section="Настройки";current=null;activePerson=null;metadataLifetime.Cancel();liveRequest?.Cancel();liveLoading=false;searchDelay.Stop();catalogRefreshTimer.Stop();
        ResetDiscoveryData();CancelCatalogQualityCheck();CancelPeopleSearch();BeginCatalogNavigationRender();StopCoverViewport();
        designFixedViewport=true;MaxWidth=1200;MaxHeight=900;MinWidth=900;MinHeight=680;Width=1000;Height=760;
        var light=prefs.Light;var favorites=new HashSet<int>(prefs.Favorites);var saved=prefs.LiveFavorites.ToList();var hidden=prefs.HiddenTitles.ToList();
        var cursorSaved=GetCursorPos(out var cursor);var checks=new List<string>();
        void Check(bool good,string label){if(!good)throw new Exception(label);checks.Add(label);}
        async Task Settle(){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(90);UpdateLayout();}
        try
        {
            foreach(var category in new[]{"Фильмы","Сериалы","Игры"})foreach(var isLight in new[]{false,true})
            {
                prefs.Light=isLight;ApplyTheme();section="Настройки";current=null;favoritesOnly=false;
                SettingsButton.Focus();var away=SettingsButton.PointToScreen(new Point(10,10));SetCursorPos((int)away.X,(int)away.Y);Mouse.Synchronize();
                Body.Children.Clear();Body.RowDefinitions.Clear();PageHeader.Children.Clear();FilterControls.Children.Clear();FiltersPanel.Visibility=Visibility.Collapsed;
                await Settle();
                var id=category=="Фильмы"?-990123:category=="Сериалы"?-990124:-990125;
                var item=new MediaItem(id,"Карточка: "+category,category,"Драма",2026,"8.0","7.9","#526B69");
                var row=new ContentControl{Content=new CatalogRow([item,item with{Id=id-10,Title="Соседняя карточка"}],3),ContentTemplate=(DataTemplate)FindResource(category=="Игры"?"GameRow":"MediaRow"),VerticalAlignment=VerticalAlignment.Top};Body.Children.Add(row);await Settle();
                var card=VisualElements<Button>(row).First(x=>x.Tag is MediaItem media&&media.Id==id);
                var actions=FindVisual<StackPanel>(card,x=>x.Name=="PosterQuickActions")!;
                var save=FindVisual<Button>(card,x=>x.Name=="PosterSave")!;var hide=FindVisual<Button>(card,x=>x.Name=="PosterHide")!;
                Check(actions.Opacity==0&&!actions.IsHitTestVisible,category+": actions start hidden outside hover/focus");
                card.Focus();await Settle();Check(actions.Opacity==1&&actions.IsHitTestVisible&&save.Focus(),category+": keyboard focus exposes reachable actions");
                SettingsButton.Focus();SetForegroundWindow(new WindowInteropHelper(this).Handle);
                var point=card.PointToScreen(new Point(card.ActualWidth/2,30));Check(SetCursorPos((int)point.X,(int)point.Y),"native pointer reaches poster");Mouse.Synchronize();await Settle();
                Check(card.IsMouseOver&&actions.Opacity==1&&actions.IsHitTestVisible,category+": native hover exposes both actions");
                var renders=renderCount;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
                Check(current==null&&renderCount==renders&&item.SavedForUi&&IsSaved(item)&&Preferences.Load().Favorites.Contains(id),category+": heart persists Saved without opening or rebuilding the page");
                var heart=FindVisual<System.Windows.Shapes.Path>(save,_=>true)!;
                Check(heart.Fill is SolidColorBrush fill&&fill.Color.A==255,category+": saved heart is filled");
                var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,(category=="Фильмы"?"movies":category=="Сериалы"?"series":"games")+(isLight?"-light":"-dark")+".png")))encoder.Save(file);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!item.SavedForUi&&!IsSaved(item),category+": second click removes Saved");
                hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
                Check(current==null&&!PersonallyVisible(item)&&HiddenCatalog.TitleHidden(item,Preferences.Load().HiddenTitles),category+": eye hides title without opening it and persists ignored state");
                Check(VisualElements<TextBlock>(Body).Any(x=>x.Text=="Игнорируемое"),"Settings expose the ignored section");
                var ignored=VisualElements<DockPanel>(Body).First(x=>VisualElements<TextBlock>(x).Any(text=>text.Text.StartsWith(item.Title+" ·",StringComparison.Ordinal)));
                VisualElements<Button>(ignored).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
                Check(PersonallyVisible(item)&&!HiddenCatalog.TitleHidden(item,Preferences.Load().HiddenTitles),category+": restoring in Settings makes the title visible again");
            }
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,MoviesSeriesGames=true,LightAndDark=true,NativeHover=true,KeyboardActions=true,NoAccidentalNavigation=true,PersistentSaved=true,PersistentIgnored=true,RestoreInSettings=true},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            prefs.Light=light;prefs.Favorites=favorites;prefs.LiveFavorites=saved;prefs.HiddenTitles=hidden;prefs.Save();
            if(cursorSaved)SetCursorPos(cursor.X,cursor.Y);Close();
        }
    }
}

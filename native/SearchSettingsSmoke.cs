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
    async Task<object> CheckSearchAndSettings(string output)
    {
        var initialLight=prefs.Light;var initialResume=prefs.AutoResumeDownloads;
        var initialFolder=prefs.Folder;var initialConfigured=prefs.FolderConfigured;
        var checks=new List<object>();
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        async Task Settle(){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(90);UpdateLayout();}
        void Shot(string name)
        {
            var bmp=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
        }
        void CheckText(string query,string stage)
        {
            Check(Search.Text==query,stage+": query changed unexpectedly.");
            var host=Search.Template.FindName("PART_ContentHost",Search) as ScrollViewer??throw new Exception("Search text host missing.");
            var rect=Search.GetRectFromCharacterIndex(Search.CaretIndex);
            Check(!rect.IsEmpty&&rect.Height>0&&host.ViewportHeight>=rect.Height-.5,stage+": search text is clipped vertically.");
            Check(rect.Top>=0&&rect.Bottom<=Search.ActualHeight+.5,stage+": search caret is outside the field.");
            Check(host.ViewportWidth>20,stage+": search text has no horizontal space.");
            var expected=((SolidColorBrush)FindResource("Text")).Color;
            Check(Search.Foreground is SolidColorBrush ink&&ink.Color==expected,stage+": text uses the wrong theme colour.");
            checks.Add(new{Stage=stage,Query=query,WindowWidth=ActualWidth,SearchHeight=Search.ActualHeight,TextViewportHeight=host.ViewportHeight,CharacterHeight=rect.Height});
        }
        async Task Type(string query)
        {
            Search.Clear();Search.Focus();
            foreach(var letter in query)
            {
                liveKey=section+"|"+Search.Text+letter+"|1|"+CatalogSelection.Filter+"|"+CatalogSelection.Collection;
                Search.SelectedText=letter.ToString();Search.CaretIndex=Search.Text.Length;
                await Task.Delay(15);
            }
            submittedQuery=Search.Text.Trim();searchCategory="";liveKey=CurrentCatalogKey;Render();await Task.Delay(450);await Settle();
            Check(Search.CaretIndex==query.Length,"Updating results moved the search caret.");
        }
        try
        {
            section="Фильмы";current=null;
            prefs.Light=true;ApplyTheme();Render();await Settle();await Type("Интерстеллар");CheckText("Интерстеллар","typing-light");Shot("search-light");
            Render();await Settle();CheckText("Интерстеллар","refresh-results");
            prefs.Light=false;ApplyTheme();Render();await Settle();CheckText("Интерстеллар","typing-dark");Shot("search-dark");
            Width=510;Height=520;await Settle();CheckText("Интерстеллар","small-short-window");Shot("search-small");
            Width=360;await Settle();CheckText("Интерстеллар","minimum-width");Shot("search-minimum");
            foreach(var kind in new[]{"Фильмы","Сериалы","Загрузки"})
            {
                section=kind;current=kind=="Загрузки"?null:new MediaItem(-88000-(kind=="Сериалы"?1:0),"Поиск в карточке",kind,"",2026,"—","—","#526B69");
                Render();await Settle();FocusCatalogSearch();await Settle();
                Check(SearchBar.IsVisible&&section==kind&&(kind=="Загрузки"||current!=null),"Focusing persistent search changed the current page.");
                CheckText("Интерстеллар","minimum-width-"+kind);Shot("search-minimum-"+kind);
            }
            section="Фильмы";lastCatalogSection="Фильмы";current=null;Render();Width=510;await Settle();
            var windowCount=Application.Current.Windows.Count;
            Search.Text="Интерстелла";Search.Text="Интерстеллар";
            SettingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));submittedQuery=Search.Text.Trim();searchCategory="";liveKey=CurrentCatalogKey;Render();await Task.Delay(450);await Settle();
            Check(section=="Настройки"&&SearchBar.Visibility==Visibility.Collapsed,"Settings did not open as a page.");
            Check(Application.Current.Windows.Count==windowCount,"Settings opened another window.");
            Check(Search.Text=="Интерстеллар","Opening settings discarded the query.");
            var light=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Светлая тема")??throw new Exception("Light theme action missing.");
            light.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            Check(prefs.Light&&Preferences.Load().Light&&section=="Настройки","Theme choice did not save while staying on settings page.");
            var resume=FindVisual<CheckBox>(Body,b=>b.Name=="SettingsAutoResume")??throw new Exception("Resume preference missing.");
            resume.IsChecked=!initialResume;resume.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Preferences.Load().AutoResumeDownloads==!initialResume,"Resume preference did not persist.");
            var folder=Path.Combine(output,"configured-folder");Directory.CreateDirectory(folder);SetDownloadFolder(folder);Render();await Settle();
            var actualFolder=Path.Combine(folder,"Ka4alka");
            Check(Directory.Exists(actualFolder)&&FindVisual<TextBlock>(Body,t=>t.Name=="SettingsFolder")?.Text==actualFolder&&Preferences.Load().Folder==actualFolder,"Settings did not create and show saved Ka4alka download folder.");
            Width=Math.Min(1220,MaxWidth-24);Height=Math.Min(900,MaxHeight-24);await Settle();Shot("settings-light");
            var dark=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Тёмная тема")??throw new Exception("Dark theme action missing.");
            dark.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();Shot("settings-dark");
            Width=510;Height=820;await Settle();Shot("settings-small");
            foreach(var block in VisualElements<TextBlock>(Body))
            {
                var origin=block.TransformToAncestor(Body).Transform(new Point());
                Check(origin.X>=-.5&&origin.X+block.ActualWidth<=Body.ActualWidth+1,"Settings content overflows narrow window.");
            }
            Width=360;await Settle();Shot("settings-minimum");
            foreach(var block in VisualElements<TextBlock>(Body))
            {
                var origin=block.TransformToAncestor(Body).Transform(new Point());
                Check(origin.X>=-.5&&origin.X+block.ActualWidth<=Body.ActualWidth+1,"Settings content overflows minimum window width.");
            }
            var copy=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Скопировать логи")??throw new Exception("Diagnostics copy button missing.");
            Clipboard.Clear();
            copy.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            for(var attempt=0;attempt<100&&(!Clipboard.ContainsText()||!Clipboard.GetText().Contains("Диагностика Качалки"));attempt++)await Task.Delay(25);
            Check(Clipboard.ContainsText()&&Clipboard.GetText().Contains("Диагностика Качалки"),"Settings copies a diagnostic report to the real Windows clipboard.");
            Check(FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Сообщить об ошибке")!=null,"Bug report action is available in settings.");
            Width=510;await Settle();
            var back=FindVisual<Button>(PageHeader,b=>AutomationProperties.GetName(b)=="Вернуться")??throw new Exception("Settings back action missing.");
            back.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            Check(section=="Фильмы"&&Search.Text=="Интерстеллар","Returning from settings lost catalog context.");
            var selectedMovie=liveItems.First();current=selectedMovie;Render();SettingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var detailBack=FindVisual<Button>(PageHeader,b=>AutomationProperties.GetName(b)=="Вернуться")??throw new Exception("Settings return to detail missing.");
            detailBack.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(current?.Id==selectedMovie.Id&&Search.Text=="Интерстеллар","Settings did not return to the opened movie.");current=null;Render();
            ShowDownloads(this,new RoutedEventArgs());liveKey=CurrentCatalogKey;ShowCatalogSection("Фильмы");await Settle();CheckText("Интерстеллар","return-from-downloads");
            liveKey="Сериалы||1";ShowCatalogSection("Сериалы");await Type("Severance");CheckText("Severance","latin-series-query");
            liveKey="Фильмы|Интерстеллар|1";ShowCatalogSection("Фильмы");await Settle();Check(Search.Text=="","Navigation to another catalog did not reset search.");
            liveKey="Сериалы|Severance|1";ShowCatalogSection("Сериалы");await Type("Severance");SettingsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));FocusCatalogSearch();await Settle();
            Check(section=="Сериалы"&&lastCatalogSection=="Сериалы"&&Search.Text=="Severance"&&Search.IsKeyboardFocusWithin&&Search.SelectionLength==Search.Text.Length,"Search shortcut did not restore the previous series catalog, submitted query and focus.");
            ClearSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));searchDelay.Stop();
            Check(Search.Text==""&&ClearSearchButton.Visibility==Visibility.Collapsed,"Clear search action did not clear query.");
            return new{Checks=checks,SettingsIsPage=true,ThemePersisted=true,ResumePersisted=true,FolderPersisted=true,QueriesRetainedAcrossNavigation=true,SearchShortcutRestoresContext=true,SettingsFitsNarrowWindow=true,PendingSearchDoesNotChangeSettingsPage=true,SettingsReturnsToOpenedMovie=true};
        }
        finally
        {
            prefs.Light=initialLight;prefs.AutoResumeDownloads=initialResume;prefs.Folder=initialFolder;prefs.FolderConfigured=initialConfigured;prefs.Save();ApplyTheme();
            section="Фильмы";current=null;Search.Clear();searchDelay.Stop();submittedQuery="";searchCategory="";lastCatalogSection="Фильмы";Status.Text="";
            Width=Math.Min(1760,MaxWidth-24);Height=Math.Min(950,MaxHeight-24);liveKey="";Render();await Settle();
        }
    }
}

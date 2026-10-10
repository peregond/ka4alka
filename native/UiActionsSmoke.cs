using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Kachalka.Updates;
namespace Kachalka;

public partial class MainWindow
{
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    async Task ClickWithMouse(FrameworkElement element,double fractionX=.5,double fractionY=.5)
    {
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        if(PresentationSource.FromVisual(element)==null)
            throw new Exception($"Cannot click {AutomationProperties.GetName(element)} ({element.GetType().Name} {element.Name}): it is no longer in the window after {renderCount} renders. Last render:\n{lastRenderCaller}");
        var point=element.PointToScreen(new Point(element.ActualWidth*fractionX,element.ActualHeight*fractionY));
        if(!SetCursorPos((int)point.X,(int)point.Y))throw new Exception("Cannot position the mouse for a native input test.");
        mouse_event(2,0,0,0,0);await Task.Delay(70);mouse_event(4,0,0,0,0);await Task.Delay(150);UpdateLayout();
    }
    async Task<object> CheckUiActions(string output)
    {
        var originalOffer=updateOffer;var originalJob=preparedUpdateJob;var originalChecking=checkingUpdate;
        var originalStatus=updateStatus;var originalSection=section;var originalItem=current;
        var originalHide=prefs.HidePoorQuality;var originalLight=prefs.Light;
        var originalWidth=Width;var originalHeight=Height;var originalMinimum=MinWidth;var originalMinHeight=MinHeight;
        var originalLeft=Left;var originalTop=Top;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        void Shot(Window window,string name)
        {
            window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        try
        {
            MinWidth=1000;MinHeight=720;Width=1000;Height=720;Left=0;Top=0;
            section="Фильмы";current=null;Search.Clear();submittedQuery="";searchCategory="";favoritesOnly=false;ResetCatalogFilters();
            // A catalog request left over from the search checks would finish mid-test and rebuild the page under the mouse.
            liveRequest?.Cancel();liveLoading=false;searchDelay.Stop();
            if(catalogPages.TryGetValue(section+"|"+CatalogSelection.Filter+"|"+livePage,out var fixturePage))liveItems=fixturePage.Items;
            liveKey=CurrentCatalogKey;Render();await Task.Delay(200);UpdateLayout();
            foreach(var popupAnimation in new[]{PopupAnimation.None,PopupAnimation.Fade})
            foreach(var label in new[]{"Подборка","Жанр","Страна","Рейтинг от","Год выхода","Порядок","Качество каталога"})
            {
                var owner=FindVisual<Button>(RootGrid,b=>AutomationProperties.GetName(b)==label)??throw new Exception("Missing menu "+label);
                owner.BringIntoView();await Task.Delay(80);UpdateLayout();
                await ClickWithMouse(owner);Check(owner.ContextMenu?.IsOpen==true,label+": first mouse click did not open the menu.");
                var popup=owner.ContextMenu!.Parent as Popup??throw new Exception("Menu popup parent is missing.");popup.PopupAnimation=popupAnimation;
                await ClickWithMouse(owner);Check(owner.ContextMenu?.IsOpen==false,label+": second mouse click reopened the menu.");
                await ClickWithMouse(owner);Check(owner.ContextMenu?.IsOpen==true,label+": third mouse click did not reopen the menu.");
                await ClickWithMouse(Search);Check(owner.ContextMenu?.IsOpen==false,label+": outside click did not dismiss the menu.");
                owner.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(owner.ContextMenu?.IsOpen==true,label+": keyboard action failed to open.");
                owner.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(owner.ContextMenu?.IsOpen==false,label+": keyboard action failed to close.");
            }
            Button About()=>FindVisual<Button>(discoveryHero!,b=>AutomationProperties.GetName(b).StartsWith("Подробнее",StringComparison.Ordinal))??throw new Exception("The carousel's film action is not keyboard accessible.");
            var banner=About();
            var featured=(MediaItem)banner.Tag;
            await ClickWithMouse(banner);
            Check(current?.Id==featured.Id&&SearchBar.IsVisible,"Clicking the carousel's film action did not open its internal media card with search retained.");
            current=null;Render();UpdateLayout();
            banner=About();banner.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(current?.Id==featured.Id,"Keyboard activation of the carousel action did not open the same media card.");
            section="Настройки";current=null;Render();UpdateLayout();
            checkingUpdate=false;updateOffer=null;preparedUpdateJob=null;UpdateStatus("Проверка ещё не выполнялась.");
            Check(updateActionButton is {IsEnabled:true}&&Equals(updateActionButton.Content,"Проверить обновления"),"Initial update action is not Check.");
            var manifest=new UpdateManifest(1,"kachalka","stable","99.0.0","win-x64","10",new("https://github.com/peregond/ka4alka/releases/download/v99.0.0/Kachalka-win-x64.zip","Kachalka-win-x64.zip",1,new string('0',64),"portable-zip"),"https://github.com/peregond/ka4alka/releases/tag/v99.0.0",true);
            updateOffer=new(manifest,[],[]);UpdateStatus("Доступна версия 99.0.0.");
            Check(Equals(updateActionButton!.Content,"Скачать")&&updateActionButton.IsEnabled,"Found update did not replace Check with Download.");
            checkingUpdate=true;UpdateStatus("Скачиваем обновление…");Check(!updateActionButton.IsEnabled,"Update action allows duplicate downloads.");
            checkingUpdate=false;UpdateStatus("Загрузка не удалась. Можно повторить.");Check(Equals(updateActionButton.Content,"Скачать")&&updateActionButton.IsEnabled,"Download failure did not retain a retry action.");
            preparedUpdateJob="ui-state-fixture-only";UpdateStatus("Обновление готово.");
            Check(Equals(updateActionButton.Content,"Обновить")&&updateActionButton.IsEnabled,"Prepared update did not replace Download with Update.");
            Render();UpdateLayout();Check(VisualElements<Button>(Body).Count(b=>b.Name=="SettingsUpdateAction")==1&&Equals(updateActionButton!.Content,"Обновить"),"Re-entering settings lost the single prepared update action.");
            Check(!VisualElements<Button>(Body).Any(b=>Equals(b.Content,"Проверить обновления")||Equals(b.Content,"Обновить и перезапустить")||Equals(b.Content,"Что нового")),"Settings displays competing update buttons.");
            var originalStartup=WindowsIntegration.StartupEnabled;
            try
            {
                var startupToggle=FindVisual<CheckBox>(Body,b=>b.Name=="SettingsStartup")??throw new Exception("Startup switch is missing.");
                startupToggle.IsChecked=!originalStartup;startupToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(WindowsIntegration.StartupEnabled==!originalStartup,"Startup switch did not update Windows registration.");
            }
            finally{WindowsIntegration.SetStartup(originalStartup);}
            updateActionButton.BringIntoView();await Task.Delay(150);Shot(this,"update-ready-dark");
            // This UI fixture never downloads, invokes the updater, or modifies the firewall.
            preparedUpdateJob=null;updateOffer=null;UpdateStatus(originalStatus);
            using(var setupScope=new SetupWindowScope(CreateFirstRunSetup()))
            {
                var setup=setupScope.Window;setup.Show();await Task.Delay(150);setup.UpdateLayout();
                Check(VisualElements<Button>(setup).Any(b=>AutomationProperties.GetName(b)=="Разрешить Качалку в брандмауэре"),"First-run setup is missing its explicit firewall action.");
                Check(VisualElements<TextBlock>(setup).Any(b=>b.Text==prefs.Folder),"First-run setup does not show the chosen download folder.");Shot(setup,"first-run-dark");
            }
            return new{NativeMouseMenuToggle=true,MenuDismissAnimations=new[]{"None","Fade"},MenuOutsideDismiss=true,KeyboardMenuToggle=true,WholeBannerOpensCard=true,KeyboardBanner=true,SingleUpdateAction=true,UpdateRetry=true,ReadyStatePreserved=true,FirstRunFirewallOptIn=true,StartupSwitchWorks=true};
        }
        finally
        {
            preparedUpdateJob=originalJob;updateOffer=originalOffer;checkingUpdate=originalChecking;UpdateStatus(originalStatus);
            prefs.HidePoorQuality=originalHide;prefs.Light=originalLight;section=originalSection;current=originalItem;
            MinWidth=originalMinimum;MinHeight=originalMinHeight;Width=originalWidth;Height=originalHeight;Left=originalLeft;Top=originalTop;ApplyTheme();Render();
        }
    }
    sealed class SetupWindowScope(Window window):IDisposable
    {
        public Window Window=>window;
        public void Dispose()=>window.Close();
    }
}

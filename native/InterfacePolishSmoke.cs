using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);

    public async Task InterfacePolishSmokeTest(string output)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();var originalLight=prefs.Light;var originalQuality=prefs.CatalogQualityHeight;var originalHide=prefs.HidePoorQuality;
        var cursorSaved=GetCursorPos(out var originalCursor);
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        async Task Settle(){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(120);UpdateLayout();}
        void Shot(string name,double scale=1)
        {
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth*scale),(int)Math.Ceiling(ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        async Task Size(double width,double height)
        {
            MaxWidth=1800;MaxHeight=1000;MinWidth=width;MinHeight=height;Width=width;Height=height;await Settle();
        }
        void CheckCards(string stage)
        {
            var posters=VisualElements<Border>(Body).Where(element=>element.Name=="CardPoster"&&element.IsVisible).ToArray();
            var titles=VisualElements<TextBlock>(Body).Where(element=>element.Name=="CardTitleText"&&element.IsVisible).ToArray();
            Check(posters.Length>0&&titles.Length==posters.Length,stage+": every poster has its readable title.");
            Check(titles.All(title=>title.TextWrapping==TextWrapping.Wrap&&Math.Abs(title.ActualHeight-36)<1&&title.LineHeight==18),stage+": titles share two readable lines without stretching individual cards.");
            Check(VisualElements<TextBlock>(Body).Where(element=>element.Name=="CardGenreText"&&element.IsVisible).All(genre=>genre.ActualHeight<=17),stage+": genre remains one compact line.");
            Check(!VisualElements<UIElement>(RootGrid).Any(element=>element.Effect!=null),stage+": shell and cards contain no blur or shadow effects.");
        }
        try
        {
            PrepareDesignSmoke();await Size(1500,920);liveKey=CurrentCatalogKey;Render();await Settle();
            foreach(var light in new[]{false,true})
            {
                prefs.Light=light;ApplyTheme();Render();await Settle();SearchSubmitButton.Focus();await Settle();
                Check(InterfacePalette.For(light).All(pair=>pair.Value.IsFrozen&&ReferenceEquals(FindResource(pair.Key),pair.Value)),"Theme reuses its frozen palette: "+(light?"light":"dark"));
                CheckCards(light?"light":"dark");Shot("interface-"+(light?"light":"dark"));
            }
            prefs.Light=false;ApplyTheme();Render();await Settle();
            SetForegroundWindow(new WindowInteropHelper(this).Handle);await ClickWithMouse(Search);SearchSubmitButton.Focus();await Settle();
            keybd_event(0x11,0,0,0);keybd_event(0x4B,0,0,0);
            try{await Task.Delay(70);}finally{keybd_event(0x4B,0,2,0);keybd_event(0x11,0,2,0);}
            await Settle();
            Check(Search.IsKeyboardFocused,"Native Ctrl+K focuses the persistent search field.");
            var focus=Search.Template.FindName("FocusRing",Search) as Border;
            Check(focus is {IsVisible:true}&&SearchShortcutHint.Visibility==Visibility.Collapsed,"Keyboard search exposes its focus ring and removes the shortcut hint.");
            Shot("interface-search-focus");
            Search.Text="Проверка";searchDelay.Stop();await Settle();
            Check(ClearSearchButton.IsVisible&&!SearchShortcutHint.IsVisible,"Clear action is available without overlapping a typed query.");
            keybd_event(0x1B,0,0,0);try{await Task.Delay(70);}finally{keybd_event(0x1B,0,2,0);}await Settle();
            Check(Search.Text.Length==0&&!ClearSearchButton.IsVisible,"Native Escape clears the current search query.");
            var hoverPoint=SearchSubmitButton.PointToScreen(new Point(SearchSubmitButton.ActualWidth/2,SearchSubmitButton.ActualHeight/2));
            Check(SetCursorPos((int)hoverPoint.X,(int)hoverPoint.Y),"Native pointer can reach the primary search action.");Mouse.Synchronize();await Settle();
            var frame=SearchSubmitButton.Template.FindName("Frame",SearchSubmitButton) as Border;
            Check(SearchSubmitButton.IsMouseOver&&frame!=null&&ReferenceEquals(frame.Background,FindResource("PrimaryHover")),"Native pointer activates the primary button hover state.");
            SearchSubmitButton.IsEnabled=false;await Settle();
            Check(!SearchSubmitButton.IsEnabled&&frame is {Opacity:.55},"Disabled action remains legible and cannot be activated.");SearchSubmitButton.IsEnabled=true;
            peopleSearchProvider=(_,_)=>Task.FromResult<IReadOnlyList<CinemaPerson>>([]);
            Search.Text="Интерстеллар";searchDelay.Stop();submittedQuery=Search.Text;searchCategory="";
            catalogGenre=catalogGenres.FirstOrDefault()?.Key??"drama";catalogYear=2026;prefs.CatalogQualityHeight=1080;
            liveKey=CurrentCatalogKey;Render();await Settle();
            var removeGenre=FindVisual<Button>(FilterControls,button=>button.Name=="ActiveFilter_genre")??throw new Exception("Selected genre chip is missing.");
            await ClickWithMouse(removeGenre);await Settle();
            Check(catalogGenre.Length==0&&catalogYear==2026&&prefs.CatalogQualityHeight==1080,"Native chip click removes only its genre and retains year and quality.");
            Shot("interface-selected-filters");
            var reset=FindVisual<Button>(FilterControls,button=>button.Name=="ResetActiveCatalogFilters")??throw new Exception("Selected filter reset is missing.");
            await ClickWithMouse(reset);await Settle();
            Check(CatalogSelection.IsDefault&&prefs.CatalogQualityHeight==0&&Search.Text=="Интерстеллар"&&submittedQuery==Search.Text&&prefs.HidePoorQuality==originalHide,"Native filter reset clears selected filters while preserving query and poor-quality preference.");
            Check(!VisualElements<Button>(FilterControls).Any(button=>button.Name.StartsWith("ActiveFilter_",StringComparison.Ordinal)),"Removing all selected filters also removes their chip row.");
            Search.Clear();searchDelay.Stop();submittedQuery="";searchCategory="";CancelPeopleSearch();liveKey=CurrentCatalogKey;Render();await Settle();
            await Size(510,720);Render();await Settle();CheckCards("narrow");Shot("interface-narrow-dark");
            prefs.Light=true;ApplyTheme();Render();await Settle();Shot("interface-narrow-light");
            foreach(var width in new[]{360d,510d,720d})
            {
                await Size(width,580);Render();await Settle();
                foreach(var action in new Control[]{Search,SearchSubmitButton,AddTorrentButton,ThemeButton})
                {
                    var bounds=action.TransformToAncestor(RootGrid).TransformBounds(new Rect(new Point(),action.RenderSize));
                    Check(action.IsVisible&&action.ActualWidth>0&&bounds.Left>=-1&&bounds.Right<=RootGrid.ActualWidth+1,"Header action stays inside "+width+" DIP viewport: "+AutomationProperties.GetName(action));
                }
                Check(!SearchShortcutHint.IsVisible,"Compact search hides its keyboard hint: "+width);
            }
            await Size(360,360);
            Search.Text="Интерстеллар";searchDelay.Stop();submittedQuery=Search.Text;searchCategory="";
            catalogGenre="drama";catalogCountry="russia";catalogYear=2026;catalogRating=8;catalogRegion="native";catalogOrder="По рейтингу";prefs.CatalogQualityHeight=2160;
            liveKey=CurrentCatalogKey;Render();await Settle();
            var chipsScroll=activeCatalogFilterScroll??throw new Exception("Compact selected-filter scroller is missing.");
            var compactReset=FindVisual<Button>(chipsScroll,button=>button.Name=="ResetActiveCatalogFilters")??throw new Exception("Compact filter reset is missing.");
            var resetBounds=compactReset.TransformToAncestor(chipsScroll).TransformBounds(new Rect(new Point(),compactReset.RenderSize));
            Check(compactReset.IsVisible&&resetBounds.Top>=-1&&resetBounds.Bottom<=chipsScroll.ActualHeight+1,"Reset is visible at the start of the compact selected-filter viewport.");
            Check(chipsScroll.MaxHeight==68&&chipsScroll.ActualHeight<=69&&Body.ActualHeight>40,"Many selected filters preserve catalog space at 360×360 DIP.");
            Check(chipsScroll.ScrollableHeight>0,"Many selected filters expose a real compact scrolling range.");
            var compactChips=VisualElements<Button>(chipsScroll).Where(button=>button.Name.StartsWith("ActiveFilter_",StringComparison.Ordinal)).ToArray();
            Check(compactChips.Length>=7,"Compact fixture exercises every selected filter.");
            foreach(var chip in compactChips)
            {
                var close=FindVisual<TextBlock>(chip,label=>label.Text=="×")??throw new Exception("Selected filter close glyph is missing.");
                var closeBounds=close.TransformToAncestor(chip).TransformBounds(new Rect(new Point(),close.RenderSize));
                Check(close.ActualWidth>0&&closeBounds.Left>=chip.Padding.Left-1&&closeBounds.Right<=chip.ActualWidth-chip.Padding.Right+1,"Chip close glyph is not clipped: "+chip.Name);
                chip.BringIntoView();await Settle();
                var chipBounds=chip.TransformToAncestor(chipsScroll).TransformBounds(new Rect(new Point(),chip.RenderSize));
                Check(chipBounds.Top>=-1&&chipBounds.Bottom<=chipsScroll.ActualHeight+1,"Compact scrolling can reach the whole filter control: "+chip.Name);
            }
            Shot("interface-short-selected-filters");
            chipsScroll.ScrollToTop();await Settle();await ClickWithMouse(compactReset);await Settle();
            Check(CatalogSelection.IsDefault&&prefs.CatalogQualityHeight==0&&Search.Text=="Интерстеллар"&&Body.ActualHeight>40,"Compact native reset restores catalog space and preserves the search query.");
            Search.Clear();searchDelay.Stop();submittedQuery="";searchCategory="";CancelPeopleSearch();liveKey=CurrentCatalogKey;
            await Size(1500,920);prefs.Light=false;ApplyTheme();Render();await Settle();
            foreach(var scale in new[]{1.2,1.25,1.5,2})Shot("interface-dark-"+(int)(scale*100),scale);
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,NativeKeyboard=true,NativeHover=true,FrozenSharedPalette=true,TitleLines=2,NoEffects=true,Viewports=new[]{360,510,720,1500}},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());throw;}
        finally
        {
            keybd_event(0x11,0,2,0);SearchSubmitButton.IsEnabled=true;if(cursorSaved)SetCursorPos(originalCursor.X,originalCursor.Y);
            peopleSearchProvider=null;prefs.CatalogQualityHeight=originalQuality;prefs.HidePoorQuality=originalHide;prefs.Light=originalLight;ApplyTheme();Close();
        }
    }
}

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
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint WindowFromPoint(DownloadSmokePoint point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint window,uint flags);

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
                Check(SearchShortcutHint.IsVisible&&SearchPlaceholder.Margin.Right>=70,"Wide idle search shows its keyboard hint with reserved text space: "+(light?"light":"dark"));
                CheckCards(light?"light":"dark");Shot("interface-"+(light?"light":"dark"));
            }
            // A RenderTargetBitmap can capture a viewport larger than the actual
            // GitHub runner desktop. Native input cannot reach that viewport:
            // SetCursorPos succeeds while Windows clamps off-screen coordinates.
            // Keep the wide captures, then fit real input to the physical monitor.
            var handle=new WindowInteropHelper(this).Handle;
            var monitorInfo=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
            Check(GetMonitorInfo(MonitorFromWindow(handle,MonitorDefaultNearest),ref monitorInfo),"Native input monitor exposes its physical work area.");
            var dpi=VisualTreeHelper.GetDpi(this);
            var nativeFit=WindowSizing.FitPixels(monitorInfo.Work.Width,monitorInfo.Work.Height,dpi.DpiScaleX,dpi.DpiScaleY);
            // The submit action replaces the shortcut hint once a query exists.
            Search.Text="Проверка";searchDelay.Stop();await Settle();
            var wideTopLeft=SearchSubmitButton.PointToScreen(new Point());
            var wideBottomRight=SearchSubmitButton.PointToScreen(new Point(SearchSubmitButton.ActualWidth,SearchSubmitButton.ActualHeight));
            Search.Clear();searchDelay.Stop();await Settle();
            File.WriteAllText(Path.Combine(output,"native-pointer-layout.json"),JsonSerializer.Serialize(new
            {
                RequestedViewport=new{Width,Height},PhysicalWorkArea=monitorInfo.Work,
                WideButtonBounds=new{Left=wideTopLeft.X,Top=wideTopLeft.Y,Right=wideBottomRight.X,Bottom=wideBottomRight.Y},
                InputViewport=new{Width=Math.Min(1500,nativeFit.Width),Height=Math.Min(920,nativeFit.Height)}
            },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
            await Size(Math.Min(1500,nativeFit.Width),Math.Min(920,nativeFit.Height));
            PlaceWithinWorkArea(handle,monitorInfo.Work,true);await Settle();
            prefs.Light=false;ApplyTheme();Render();await Settle();
            SetForegroundWindow(new WindowInteropHelper(this).Handle);await ClickWithMouse(Search);SearchSubmitButton.Focus();await Settle();
            keybd_event(0x11,0,0,0);keybd_event(0x4B,0,0,0);
            try{await Task.Delay(70);}finally{keybd_event(0x4B,0,2,0);keybd_event(0x11,0,2,0);}
            await Settle();
            Check(Search.IsKeyboardFocused,"Native Ctrl+K focuses the persistent search field.");
            var focus=Search.Template.FindName("TextFrame",Search) as Border;
            Check(focus!=null&&ReferenceEquals(focus.BorderBrush,FindResource("EdgeFocus"))&&SearchShortcutHint.Visibility==Visibility.Collapsed,"Keyboard search lights its border (EdgeFocus) and removes the shortcut hint.");
            Shot("interface-search-focus");
            Search.Text="Проверка";searchDelay.Stop();await Settle();
            Check(ClearSearchButton.IsVisible&&!SearchShortcutHint.IsVisible,"Clear action is available without overlapping a typed query.");
            keybd_event(0x1B,0,0,0);try{await Task.Delay(70);}finally{keybd_event(0x1B,0,2,0);}await Settle();
            Check(Search.Text.Length==0&&!ClearSearchButton.IsVisible,"Native Escape clears the current search query.");
            Search.Text="Проверка";searchDelay.Stop();await Settle();
            var hoverPoint=SearchSubmitButton.PointToScreen(new Point(SearchSubmitButton.ActualWidth/2,SearchSubmitButton.ActualHeight/2));
            Check(SetCursorPos((int)hoverPoint.X,(int)hoverPoint.Y),"Native pointer can reach the primary search action.");Mouse.Synchronize();await Settle();
            var frame=SearchSubmitButton.Template.FindName("Frame",SearchSubmitButton) as Border;
            var gotCursor=GetCursorPos(out var actualCursor);
            var buttonTopLeft=SearchSubmitButton.PointToScreen(new Point());
            var buttonBottomRight=SearchSubmitButton.PointToScreen(new Point(SearchSubmitButton.ActualWidth,SearchSubmitButton.ActualHeight));
            var pointerEvidence=new
            {
                Requested=new{X=(int)hoverPoint.X,Y=(int)hoverPoint.Y},Actual=new{actualCursor.X,actualCursor.Y},GotCursor=gotCursor,
                PhysicalWorkArea=monitorInfo.Work,ButtonBounds=new{Left=buttonTopLeft.X,Top=buttonTopLeft.Y,Right=buttonBottomRight.X,Bottom=buttonBottomRight.Y},
                ForegroundIsApp=GetForegroundWindow()==handle,PointerRootIsApp=GetAncestor(WindowFromPoint(actualCursor),2)==handle,
                SearchSubmitButton.IsMouseOver,MouseTarget=Mouse.DirectlyOver?.GetType().Name,
                FrameBrush=frame?.Background?.ToString(),ExpectedBrush=((Brush)FindResource("PrimaryHover")).ToString(),
                BrushIsShared=frame!=null&&ReferenceEquals(frame.Background,FindResource("PrimaryHover"))
            };
            var pointerJson=JsonSerializer.Serialize(pointerEvidence,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true});
            File.WriteAllText(Path.Combine(output,"native-pointer.json"),pointerJson);
            Check(gotCursor&&Math.Abs(actualCursor.X-(int)hoverPoint.X)<=1&&Math.Abs(actualCursor.Y-(int)hoverPoint.Y)<=1,"Native cursor reaches the actual requested button center. "+pointerJson);
            Check(pointerEvidence.ForegroundIsApp&&pointerEvidence.PointerRootIsApp&&buttonTopLeft.X>=monitorInfo.Work.Left&&buttonBottomRight.X<=monitorInfo.Work.Right&&buttonTopLeft.Y>=monitorInfo.Work.Top&&buttonBottomRight.Y<=monitorInfo.Work.Bottom,"Primary action is physically visible and receives native input in the foreground app. "+pointerJson);
            Check(SearchSubmitButton.IsMouseOver&&frame!=null&&pointerEvidence.BrushIsShared,"Native pointer activates the primary button hover state. "+pointerJson);
            SearchSubmitButton.IsEnabled=false;await Settle();
            Check(!SearchSubmitButton.IsEnabled&&frame is {Opacity:.55},"Disabled action remains legible and cannot be activated.");SearchSubmitButton.IsEnabled=true;Search.Clear();searchDelay.Stop();
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
                await Size(width,580);Render();await Settle();Search.Text="Проверка";searchDelay.Stop();await Settle();
                foreach(var action in width<480?new Control[]{Search,SearchSubmitButton,AddTorrentButton}:new Control[]{Search,SearchSubmitButton,AddTorrentButton,ThemeButton})
                {
                    var bounds=action.TransformToAncestor(RootGrid).TransformBounds(new Rect(new Point(),action.RenderSize));
                    Check(action.IsVisible&&action.ActualWidth>0&&bounds.Left>=-1&&bounds.Right<=RootGrid.ActualWidth+1,"Header action stays inside "+width+" DIP viewport: "+AutomationProperties.GetName(action));
                }
                Check(!SearchShortcutHint.IsVisible&&SearchPlaceholder.Margin.Right==14,"Compact search hides its keyboard hint and returns text space: "+width);Search.Clear();searchDelay.Stop();
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
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,NativeKeyboard=true,NativeHover=true,NativePointer=pointerEvidence,FrozenSharedPalette=true,TitleLines=2,NoEffects=true,Viewports=new[]{360,510,720,1500}},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());throw;}
        finally
        {
            keybd_event(0x11,0,2,0);SearchSubmitButton.IsEnabled=true;if(cursorSaved)SetCursorPos(originalCursor.X,originalCursor.Y);
            peopleSearchProvider=null;prefs.CatalogQualityHeight=originalQuality;prefs.HidePoorQuality=originalHide;prefs.Light=originalLight;ApplyTheme();Close();
        }
    }
}

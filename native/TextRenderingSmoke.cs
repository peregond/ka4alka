using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    // A controlled WM_DPICHANGED drives the real WPF HWND render target and
    // visual text metrics. Changing only RenderTargetBitmap's DPI would leave
    // those metrics at 96 DPI and would not reproduce fractional-scale text.
    public async Task TextRenderingSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        var checks=new List<string>();var cases=new List<object>();var variants=new List<object>();var screenshots=new List<object>();var fonts=new List<object>();var transitions=new List<object>();
        var previousLight=prefs.Light;var previousSection=section;var previousCurrent=current;var previousQuery=Search.Text;
        var previousDpi=VisualTreeHelper.GetDpi(this);var windows=new List<Window>();
        var monitor=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(this).Handle,MonitorDefaultNearest),ref monitor))throw new Exception("Typography physical work area is unavailable.");
        var threadContext=TextProbeSetThreadDpiAwarenessContext(new IntPtr(-4));
        using var smoothing=new TextProbeFontSmoothing();
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks.Add(message);}
        async Task Settle(FrameworkElement element)
        {
            element.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(150);element.UpdateLayout();
        }
        async Task Dpi(FrameworkElement element,double scale)
        {
            var source=PresentationSource.FromVisual(element) as HwndSource??throw new Exception("Text probe has no native HWND render target.");
            Check(TextProbeGetWindowRect(source.Handle,out var rect),"Native typography HWND exposes its physical bounds.");
            var dpi=(int)Math.Round(96*scale);
            var popup=element is ContextMenu;
            if(!popup)
            {
                TextProbeSendMessage(source.Handle,0x02E0,new IntPtr(dpi|(dpi<<16)),ref rect);
                await Settle(element);
            }
            source=PresentationSource.FromVisual(element) as HwndSource??throw new Exception("Native DPI transition detached the "+element.GetType().Name+" visual.");
            var target=source.CompositionTarget??throw new Exception("Native DPI transition disposed the "+element.GetType().Name+" HWND target.");
            var actual=VisualTreeHelper.GetDpi(element);
            var nativeMessageEffective=Math.Abs(actual.DpiScaleX-scale)<.001&&Math.Abs(actual.DpiScaleY-scale)<.001;
            var transitionMethod="No transition needed; native target already at requested DPI";
            if(!popup&&nativeMessageEffective)transitionMethod="WM_DPICHANGED";
            if(!nativeMessageEffective)
            {
                // Windows can discard synthetic monitor messages on a runner
                // whose physical monitor remains at 96 DPI. Exercise WPF's
                // same real HWND-target transition directly in this test;
                // it updates renderer transforms, glyph DPI and visual flags.
                // A popup closes when it receives a top-level DPI message.
                // The after-parent path updates the same real render target
                // without activating its HWND and dismissing the menu.
                var eventType=popup?typeof(HwndTarget).Assembly.GetType("System.Windows.HwndDpiChangedAfterParentEventArgs")??throw new Exception("WPF popup DPI event is unavailable."):typeof(HwndDpiChangedEventArgs);
                var methodName=popup?"OnDpiChangedAfterParent":"OnDpiChanged";
                var signature=new[]{typeof(DpiScale),typeof(DpiScale),typeof(Rect)};
                var constructor=eventType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,signature,null)
                    ??throw new Exception("WPF native DPI event constructor is unavailable.");
                var change=constructor.Invoke([actual,new DpiScale(scale,scale),new Rect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top)]);
                var method=typeof(HwndTarget).GetMethod(methodName,BindingFlags.Instance|BindingFlags.NonPublic,null,[eventType],null)
                    ??throw new Exception("WPF HWND-target DPI transition is unavailable.");
                method.Invoke(target,[change]);transitionMethod="Test-only HwndTarget."+methodName+", the native WPF renderer transition";
                if(ReferenceEquals(element,this))FitToMonitor(false);
                await Settle(element);actual=VisualTreeHelper.GetDpi(element);
            }
            source=PresentationSource.FromVisual(element) as HwndSource??throw new Exception("Scaled "+element.GetType().Name+" visual has no live HWND.");
            target=source.CompositionTarget??throw new Exception("Scaled "+element.GetType().Name+" visual has no live HWND target.");
            var matrix=target.TransformToDevice;
            Check(Math.Abs(actual.DpiScaleX-scale)<.001&&Math.Abs(actual.DpiScaleY-scale)<.001,
                $"Native DPI transition reaches the actual {scale*100:F0}% visual metrics ({element.GetType().Name}: {actual.DpiScaleX:F3},{actual.DpiScaleY:F3}).");
            Check(Math.Abs(matrix.M11-scale)<.001&&Math.Abs(matrix.M22-scale)<.001,
                $"The native HWND render target renders at {dpi} DPI.");
            transitions.Add(new{Surface=element.GetType().Name,RequestedDpi=dpi,ActualDpi=actual.PixelsPerInchX,NativeMessageEffective=!popup&&nativeMessageEffective,
                Method=transitionMethod});
        }
        void Fit(double scale)
        {
            Width=Math.Min(620,(monitor.Work.Width-32)/scale);Height=Math.Min(420,(monitor.Work.Height-64)/scale);Left=8;Top=8;
        }
        void Policy(DependencyObject element,string name)
        {
            Check(TextOptions.GetTextFormattingMode(element)==TextFormattingMode.Display,name+": text uses device-aware display metrics.");
            Check(TextOptions.GetTextRenderingMode(element)==TextRenderingMode.Auto,name+": text allows Windows to choose native antialiasing.");
        }
        void TextBounds(TextBlock block,string stage)
        {
            if(!block.IsVisible||block.ActualWidth<=0||string.IsNullOrEmpty(block.Text))return;
            Policy(block,stage+" "+block.Text);
            var dpi=VisualTreeHelper.GetDpi(block);
            var text=new FormattedText(block.Text,CultureInfo.CurrentCulture,block.FlowDirection,
                new Typeface(block.FontFamily,block.FontStyle,block.FontWeight,block.FontStretch),block.FontSize,Brushes.Black,
                null,TextFormattingMode.Display,dpi.PixelsPerDip);
            if(block.TextWrapping!=TextWrapping.NoWrap)text.MaxTextWidth=block.ActualWidth;
            if(!double.IsNaN(block.LineHeight))text.LineHeight=block.LineHeight;
            Check(block.ActualHeight+1>=Math.Min(text.Height,text.Baseline+block.FontSize*.3),stage+": Cyrillic baselines and descenders fit their text container.");
            if(block.TextWrapping==TextWrapping.NoWrap&&block.TextTrimming==TextTrimming.None)
                Check(text.WidthIncludingTrailingWhitespace<=block.ActualWidth+1,stage+": untrimmed action text fits horizontally.");
        }
        void SearchBounds(string stage,string? expectedFamily=null)
        {
            Policy(Search,stage+" search");
            Check(Search.FontFamily.Source==(expectedFamily??((FontFamily)FindResource("InterfaceFont")).Source),stage+": search inherits the requested interface typeface.");
            var host=Search.Template.FindName("PART_ContentHost",Search) as ScrollViewer??throw new Exception("Search text viewport is missing.");
            Search.CaretIndex=Search.Text.Length;
            var caret=Search.GetRectFromCharacterIndex(Search.CaretIndex);
            Check(!caret.IsEmpty&&caret.Height>0&&host.ViewportHeight+.5>=caret.Height,stage+": search Cyrillic text fits the real native-DPI viewport height.");
            Check(caret.Top>=-.5&&caret.Bottom<=Search.ActualHeight+.5&&host.ViewportWidth>25,stage+": search retains a usable caret and text area.");
            var dpi=VisualTreeHelper.GetDpi(Search);
            cases.Add(new{Stage=stage,VisualDpi=dpi.PixelsPerInchX,SearchFont=Search.FontFamily.Source,SearchSize=Search.FontSize,
                SearchViewportHeight=host.ViewportHeight,CharacterHeight=caret.Height,SearchWidth=Search.ActualWidth});
        }
        void Shot(FrameworkElement element,string filename,string policy)
        {
            var source=PresentationSource.FromVisual(element) as HwndSource??throw new Exception("Native screenshot HWND is missing.");
            var image=TextProbeScreenCapture(source.Handle);
            var dpi=VisualTreeHelper.GetDpi(element);
            var bitmap=BitmapSource.Create(image.Width,image.Height,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Bgr32,null,image.Pixels,image.Width*4);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,filename)))encoder.Save(file);
            screenshots.Add(new{File=filename,image.Width,image.Height,VisualDpi=dpi.PixelsPerInchX,
                NativeMonitorDpi=TextProbeGetDpiForWindow(source.Handle),Capture="BitBlt actual visible HWND client pixels",Policy=policy});
        }
        try
        {
            Check(threadContext!=IntPtr.Zero,"The typography test uses a PerMonitorV2-aware native thread.");
            Check(smoothing.Enabled&&smoothing.Type==2,"Windows font smoothing and ClearType are enabled for this isolated test and restored afterwards.");
            const string cyrillic="АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
            foreach(var key in new[]{"InterfaceFont","DisplayFont","MonoFont"})
            foreach(var weight in new[]{FontWeights.Normal,FontWeights.SemiBold,FontWeights.Bold})
            {
                var family=(FontFamily)FindResource(key);var typeface=new Typeface(family,FontStyles.Normal,weight,FontStretches.Normal);
                Check(typeface.TryGetGlyphTypeface(out var glyph),key+": the packaged typeface resolves for "+weight+".");
                Check(cyrillic.All(character=>glyph.CharacterToGlyphMap.TryGetValue(character,out var index)&&index!=0),key+": every upper- and lowercase Cyrillic letter, including Ё/ё, has a real glyph for "+weight+".");
                fonts.Add(new{Resource=key,Family=family.Source,Weight=weight.ToString(),CyrillicGlyphs=cyrillic.Length,ResolvedFamilies=glyph.FamilyNames.Values.Distinct().ToArray()});
            }
            liveRequest?.Cancel();CancelCatalogQualityCheck();searchDelay.Stop();catalogRefreshTimer.Stop();
            section="Загрузки";current=null;Render();
            Search.Text="Съешь ещё этих мягких французских булок";searchDelay.Stop();
            foreach(var light in new[]{false,true})
            foreach(var scale in new[]{1d,1.25,1.5,1.75,2})
            {
                prefs.Light=light;ApplyTheme();Render();await Dpi(this,scale);
                Fit(scale);await Settle(this);Activate();TextProbeSetForegroundWindow(new WindowInteropHelper(this).Handle);
                Search.Focus();Search.Select(0,0);Search.CaretIndex=Search.Text.Length;await Settle(this);
                var stage=(light?"light":"dark")+"-"+(int)(scale*100);
                Check(Math.Abs(VisualTreeHelper.GetDpi(Search).DpiScaleX-scale)<.001,stage+": the search glyph renderer inherits the actual scaled visual DPI.");
                Policy(this,stage+" main window");SearchBounds(stage);
                foreach(var block in VisualElements<TextBlock>(PageHeader).Concat(VisualElements<TextBlock>(Body)))TextBounds(block,stage);
                // The popup/modal cases below cover 100/125/150%. Extreme
                // scales additionally exercise the real compact search layout.
                if(scale>1.5)continue;
                if(scale==1.25)
                {
                    // Reproduce the published 0.40 rendering policy on the
                    // same real controls. This is not a binary-version comparison.
                    var previousFormatting=ReadLocalValue(TextOptions.TextFormattingModeProperty);var previousRendering=ReadLocalValue(TextOptions.TextRenderingModeProperty);
                    try
                    {
                        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(this,TextRenderingMode.Grayscale);
                        await Settle(this);Shot(this,"text-"+stage+"-previous-policy.png","Published 0.40 Ideal/Grayscale policy reproduced on these controls");
                    }
                    finally
                    {
                        Restore(TextOptions.TextFormattingModeProperty,previousFormatting);Restore(TextOptions.TextRenderingModeProperty,previousRendering);
                        await Settle(this);
                    }
                    SearchBounds(stage+" after restoring new policy");Shot(this,"text-"+stage+".png","Actual Display/Auto application policy");
                }
                var owner=downloadControls??throw new Exception("Actual download management action is missing.");
                owner.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var menu=owner.ContextMenu??throw new Exception("Actual download management popup is missing.");
                try
                {
                    await Settle(menu);Check(menu.IsOpen&&menu.ActualHeight>0,stage+": the real menu is open in its separate native popup.");
                    await Dpi(menu,scale);Policy(menu,stage+" context menu");
                    Check(menu.FontFamily.Source==((FontFamily)FindResource("InterfaceFont")).Source,stage+": native popup inherits the interface typeface.");
                    var text=VisualElements<TextBlock>(menu).Where(block=>block.IsVisible&&block.Text.Length>0).ToArray();
                    Check(text.Length>=5,stage+": all download management labels have rendered native popup text.");
                    foreach(var block in text){TextBounds(block,stage+" menu");Check(Math.Abs(VisualTreeHelper.GetDpi(block).DpiScaleX-scale)<.001,stage+": menu text renders at the actual requested DPI.");}
                    if(scale==1.25)Shot(menu,"text-menu-"+stage+".png","Actual native PopupRoot Display/Auto policy");
                }
                finally{menu.IsOpen=false;await Settle(this);}

                var modal=CreateFirstRunSetup();windows.Add(modal);modal.Width=400;modal.MaxHeight=440;
                var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var started=false;
                modal.ContentRendered+=async(_,_)=>
                {
                    if(started)return;started=true;
                    try
                    {
                        await Dpi(modal,scale);await Settle(modal);
                        Check(!TextProbeIsWindowEnabled(new WindowInteropHelper(this).Handle),stage+": the real setup dialog is modal and disables its native owner HWND.");Policy(modal,stage+" modal");
                        Check(modal.FontFamily.Source==((FontFamily)FindResource("InterfaceFont")).Source,stage+": the modal inherits the interface typeface.");
                        foreach(var block in VisualElements<TextBlock>(modal))TextBounds(block,stage+" modal");
                        if(scale==1.25)Shot(modal,"text-modal-"+stage+".png","Actual modal Display/Auto policy");
                        complete.TrySetResult();
                    }
                    catch(Exception error){complete.TrySetException(error);}
                    finally{modal.Close();}
                };
                modal.ShowDialog();await complete.Task;
                Check(TextProbeIsWindowEnabled(new WindowInteropHelper(this).Handle),stage+": closing the modal restores its native owner's input.");
            }
            var previousFamily=ReadLocalValue(FontFamilyProperty);
            try
            {
                foreach(var light in new[]{false,true})
                foreach(var family in new[]{(FontFamily)FindResource("InterfaceFont"),new FontFamily("Segoe UI"),new FontFamily("Tahoma"),new FontFamily("Verdana"),new FontFamily("Georgia")})
                {
                    prefs.Light=light;ApplyTheme();FontFamily=family;Render();await Dpi(this,1.25);
                    Fit(1.25);await Settle(this);Search.Focus();Search.CaretIndex=Search.Text.Length;await Settle(this);
                    var stage="variant-"+(light?"light":"dark")+"-"+family.Source;
                    SearchBounds(stage,family.Source);
                    var face=new Typeface(family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
                    Check(face.TryGetGlyphTypeface(out var glyph)&&cyrillic.All(character=>glyph.CharacterToGlyphMap.TryGetValue(character,out var index)&&index!=0),stage+": the unusual interface font has complete Cyrillic glyph coverage.");
                    var blocks=VisualElements<TextBlock>(PageHeader).Concat(VisualElements<TextBlock>(Body)).Where(block=>block.IsVisible&&block.ActualWidth>0&&block.Text.Length>0).ToArray();
                    foreach(var block in blocks)TextBounds(block,stage);
                    var caret=Search.GetRectFromCharacterIndex(Search.CaretIndex);
                    variants.Add(new{Theme=light?"light":"dark",Font=family.Source,VisualDpi=VisualTreeHelper.GetDpi(Search).PixelsPerInchX,
                        SearchHeight=Search.ActualHeight,CaretHeight=caret.Height,VisibleTextBlocks=blocks.Length,
                        WrappedBlocks=blocks.Count(block=>block.TextWrapping!=TextWrapping.NoWrap),EllipsisBlocks=blocks.Count(block=>block.TextTrimming!=TextTrimming.None),
                        CyrillicGlyphs=cyrillic.Length,Formatting=TextOptions.GetTextFormattingMode(Search).ToString(),Rendering=TextOptions.GetTextRenderingMode(Search).ToString()});
                }
            }
            finally{Restore(FontFamilyProperty,previousFamily);}
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Version=typeof(MainWindow).Assembly.GetName().Version?.ToString(3),
                DpiMethod="Controlled WM_DPICHANGED for windows; test-only HwndTarget.OnDpiChanged fallback and OnDpiChangedAfterParent for popups; actual WPF visual and render-target metrics verified; physical monitor DPI is unchanged and reported separately",
                Smoothing=new{smoothing.OriginalEnabled,smoothing.OriginalType,smoothing.Enabled,smoothing.Type},Fonts=fonts,Cases=cases,Variants=variants,DpiTransitions=transitions,Screenshots=screenshots,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            foreach(var window in windows.Where(window=>window.IsVisible))window.Close();
            prefs.Light=previousLight;prefs.Save();ApplyTheme();Search.Text=previousQuery;searchDelay.Stop();section=previousSection;current=previousCurrent;
            await Dpi(this,previousDpi.DpiScaleX);
            if(threadContext!=IntPtr.Zero)TextProbeSetThreadDpiAwarenessContext(threadContext);
        }
        Close();

        void Restore(DependencyProperty property,object value){if(value==DependencyProperty.UnsetValue)ClearValue(property);else SetValue(property,value);}
    }

    [StructLayout(LayoutKind.Sequential)] struct TextProbeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct TextProbePoint {public int X,Y;}
    [DllImport("user32.dll",EntryPoint="SetThreadDpiAwarenessContext")] static extern IntPtr TextProbeSetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll",EntryPoint="GetDpiForWindow")] static extern uint TextProbeGetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetWindowRect")] static extern bool TextProbeGetWindowRect(IntPtr hwnd,out TextProbeRect rect);
    [DllImport("user32.dll",EntryPoint="GetClientRect")] static extern bool TextProbeGetClientRect(IntPtr hwnd,out TextProbeRect rect);
    [DllImport("user32.dll",EntryPoint="ClientToScreen")] static extern bool TextProbeClientToScreen(IntPtr hwnd,ref TextProbePoint point);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern IntPtr TextProbeSendMessage(IntPtr hwnd,int message,IntPtr parameter,ref TextProbeRect rect);
    [DllImport("user32.dll",EntryPoint="SetForegroundWindow")] static extern bool TextProbeSetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="IsWindowEnabled")] static extern bool TextProbeIsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetSystemMetrics")] static extern int TextProbeGetSystemMetrics(int index);
    [DllImport("user32.dll",EntryPoint="GetDC")] static extern IntPtr TextProbeGetDc(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="ReleaseDC")] static extern int TextProbeReleaseDc(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll",EntryPoint="CreateCompatibleDC")] static extern IntPtr TextProbeCreateDc(IntPtr dc);
    [DllImport("gdi32.dll",EntryPoint="DeleteDC")] static extern bool TextProbeDeleteDc(IntPtr dc);
    [DllImport("gdi32.dll",EntryPoint="SelectObject")] static extern IntPtr TextProbeSelectObject(IntPtr dc,IntPtr bitmap);
    [DllImport("gdi32.dll",EntryPoint="DeleteObject")] static extern bool TextProbeDeleteObject(IntPtr value);
    [DllImport("gdi32.dll",EntryPoint="BitBlt")] static extern bool TextProbeBitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint operation);
    [StructLayout(LayoutKind.Sequential)] struct TextProbeBitmapInfo
    {
        public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;
        public int XPelsPerMeter,YPelsPerMeter;public uint ColorsUsed,ColorsImportant;
    }
    [DllImport("gdi32.dll",EntryPoint="CreateDIBSection")] static extern IntPtr TextProbeCreateBitmap(IntPtr dc,ref TextProbeBitmapInfo info,uint usage,out IntPtr pixels,IntPtr section,uint offset);
    sealed record TextProbeImage(int Width,int Height,byte[] Pixels);
    static TextProbeImage TextProbeScreenCapture(IntPtr hwnd)
    {
        if(!TextProbeGetClientRect(hwnd,out var rect))throw new Exception("Native screenshot client bounds are unavailable.");
        var origin=new TextProbePoint();if(!TextProbeClientToScreen(hwnd,ref origin))throw new Exception("Native screenshot location is unavailable.");
        var width=rect.Right-rect.Left;var height=rect.Bottom-rect.Top;
        var screenX=TextProbeGetSystemMetrics(76);var screenY=TextProbeGetSystemMetrics(77);
        if(width<=0||height<=0||origin.X<screenX||origin.Y<screenY||origin.X+width>screenX+TextProbeGetSystemMetrics(78)||origin.Y+height>screenY+TextProbeGetSystemMetrics(79))
            throw new Exception($"Typography HWND must fit the physical desktop for real LCD capture: {origin.X},{origin.Y} {width}x{height}.");
        var screen=TextProbeGetDc(IntPtr.Zero);var target=TextProbeCreateDc(screen);var bitmap=IntPtr.Zero;var previous=IntPtr.Zero;
        try
        {
            var info=new TextProbeBitmapInfo{Size=(uint)Marshal.SizeOf<TextProbeBitmapInfo>(),Width=width,Height=-height,Planes=1,BitCount=32};
            bitmap=TextProbeCreateBitmap(screen,ref info,0,out var pixels,IntPtr.Zero,0);
            if(screen==IntPtr.Zero||target==IntPtr.Zero||bitmap==IntPtr.Zero||pixels==IntPtr.Zero)throw new Exception("Native typography screen bitmap allocation failed.");
            previous=TextProbeSelectObject(target,bitmap);
            if(!TextProbeBitBlt(target,0,0,width,height,screen,origin.X,origin.Y,0x40CC0020))throw new Exception("Actual native LCD client pixels could not be captured.");
            var data=new byte[checked(width*height*4)];Marshal.Copy(pixels,data,0,data.Length);return new(width,height,data);
        }
        finally{if(previous!=IntPtr.Zero)TextProbeSelectObject(target,previous);if(bitmap!=IntPtr.Zero)TextProbeDeleteObject(bitmap);if(target!=IntPtr.Zero)TextProbeDeleteDc(target);if(screen!=IntPtr.Zero)TextProbeReleaseDc(IntPtr.Zero,screen);}
    }
    sealed class TextProbeFontSmoothing:IDisposable
    {
        public uint OriginalEnabled{get;}=Get(0x004A);public uint OriginalType{get;}=Get(0x200A);
        public bool Enabled=>Get(0x004A)!=0;public uint Type=>Get(0x200A);
        public TextProbeFontSmoothing(){Set(0x004B,1,IntPtr.Zero);Set(0x200B,0,new IntPtr(2));}
        static uint Get(uint action){uint value=0;if(!Read(action,0,ref value,0))throw new Exception("Windows font smoothing settings are unavailable.");return value;}
        static void Set(uint action,uint parameter,IntPtr value){if(!Write(action,parameter,value,2))throw new Exception("Windows font smoothing settings could not be changed for the typography test.");}
        public void Dispose(){Write(0x200B,0,new IntPtr(OriginalType),2);Write(0x004B,OriginalEnabled,IntPtr.Zero,2);}
        [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")] static extern bool Read(uint action,uint parameter,ref uint value,uint flags);
        [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")] static extern bool Write(uint action,uint parameter,IntPtr value,uint flags);
    }
}

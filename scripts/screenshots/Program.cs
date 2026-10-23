using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Kachalka;

internal static class Program
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static string releaseDirectory="",outputDirectory="";
    static readonly string CaptureLanguage=Environment.GetEnvironmentVariable("KACHALKA_SCREENSHOT_LANGUAGE")??"ru";
    static readonly bool SettingsOnly=Environment.GetEnvironmentVariable("KACHALKA_SCREENSHOT_SCOPE")=="settings";
    static readonly List<object> captures=[];
    static readonly List<string> skipped=[];
    static readonly DateTime Deadline=DateTime.UtcNow.AddSeconds(510);

    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length!=2){Console.Error.WriteLine("Usage: CaptureReleaseScreenshot <verified-release-directory> <output-directory>");return 2;}
        releaseDirectory=Path.GetFullPath(args[0]);outputDirectory=Path.GetFullPath(args[1]);Directory.CreateDirectory(outputDirectory);
        using var desktop=CaptureDesktop.Create();
        AssemblyLoadContext.Default.Resolving+=(_,name)=>
        {
            var path=Path.Combine(releaseDirectory,name.Name+".dll");
            return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
        };
        var released=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(releaseDirectory,"Kachalka.dll"));
        // WPF discovers pack resources from the process entry assembly. Set
        // the actual application before its Application type initializes.
        Assembly.SetEntryAssembly(released);
        return StartReleasedApplication();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int StartReleasedApplication()
    {
        var version=typeof(Kachalka.App).Assembly.GetName().Version?.ToString(3);
        var expected=Environment.GetEnvironmentVariable("KACHALKA_SCREENSHOT_VERSION")??"0.43.4";
        if(version!=expected)throw new InvalidOperationException("Screenshot driver requires the public "+expected+" assembly, got "+version);
        if(CaptureLanguage is not ("ru" or "en"))throw new InvalidOperationException("Screenshot language must be ru or en.");
        var state=Path.Combine(outputDirectory,"isolated-state-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        var downloads=Path.Combine(Path.GetPathRoot(state)!,"Ka4alka");Directory.CreateDirectory(downloads);
        Environment.SetEnvironmentVariable("KACHALKA_DATA",state);
        File.WriteAllText(Path.Combine(state,"settings.json"),JsonSerializer.Serialize(new
        {
            Folder=downloads,FolderConfigured=true,Light=false,UiLanguage=CaptureLanguage,CheckForUpdates=false,AutoUpdate=false,
            AutoResumeDownloads=false,NotifyDownloads=false,LanEnabled=false,QualityFilterConfigured=true,HidePoorQuality=true
        }));
        Directory.SetCurrentDirectory(releaseDirectory);
        var app=new Kachalka.App();app.InitializeComponent();
        var exitCode=1;
        var watchdog=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        watchdog.Tick+=(_,_)=>
        {
            if(DateTime.UtcNow<Deadline)return;
            File.WriteAllText(Path.Combine(outputDirectory,"error.txt"),"Real release screenshot capture timed out.");
            Console.Error.WriteLine("Real release screenshot capture timed out.");app.Shutdown(1);
        };
        app.Startup+=(_,_)=>
        {
            watchdog.Start();
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>
            {
                try
                {
                    await Capture(app);exitCode=0;
                }
                catch(Exception error)
                {
                    var root=error is TargetInvocationException invocation&&invocation.InnerException!=null?invocation.InnerException:error;
                    File.WriteAllText(Path.Combine(outputDirectory,"error.txt"),root.ToString());Console.Error.WriteLine(root);
                }
                finally
                {
                    watchdog.Stop();
                    if(app.MainWindow is { } window)window.Close();
                    else app.Shutdown(exitCode);
                }
            }));
        };
        app.Run();return exitCode;
    }

    static object? Field(object subject,string name)=>subject.GetType().GetField(name,Members)?.GetValue(subject);
    static T Value<T>(object subject,string name)=>Field(subject,name) is T value?value:throw new InvalidOperationException("Released application field missing: "+name);
    static object? Call(object subject,string name,params object?[] arguments)
    {
        var method=subject.GetType().GetMethods(Members).SingleOrDefault(method=>method.Name==name&&method.GetParameters().Length==arguments.Length)
            ??throw new InvalidOperationException("Released application navigation method missing: "+name);
        return method.Invoke(subject,arguments);
    }
    static IEnumerable<T> Visuals<T>(DependencyObject root)where T:DependencyObject
    {
        if(root is T item)yield return item;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var descendant in Visuals<T>(VisualTreeHelper.GetChild(root,index)))yield return descendant;
    }
    static async Task Settle(Window window)
    {
        window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);await Task.Delay(200);window.UpdateLayout();
    }
    static async Task Until(Func<bool> condition,TimeSpan timeout,string error)
    {
        var until=DateTime.UtcNow+timeout;
        while(!condition()&&DateTime.UtcNow<until&&DateTime.UtcNow<Deadline)await Task.Delay(200);
        if(!condition())throw new InvalidOperationException(error);
    }
    static bool PosterVisible(MainWindow window,Image image)
    {
        var viewport=Field(window,"coverViewport");
        var measure=viewport?.GetType().GetMethod("Measure",Members,[typeof(Image)]);
        var position=measure?.Invoke(viewport,[image]);
        return position?.GetType().GetProperty("Visible",Members)?.GetValue(position) is true;
    }
    static Image[] VisiblePosters(MainWindow window)=>Visuals<Image>(window)
        .Where(image=>image.IsLoaded&&image.DataContext is MediaItem&&image.Tag?.ToString() is not ("FeaturePoster" or "FeaturePlaceholderPoster")&&PosterVisible(window,image)).ToArray();
    static bool Loaded(Image image)=>image.Source is BitmapSource bitmap&&bitmap.PixelWidth>8&&bitmap.PixelHeight>8;

    static bool CatalogSettled(MainWindow window)
    {
        if(Field(window,"current")!=null||Field(window,"section") is not ("Фильмы" or "Сериалы"))return true;
        return window.GetType().GetProperty("CatalogQualityChecking",Members)?.GetValue(window) is not true
            &&Field(window,"liveLoading") is false&&Field(window,"featuredRequest")==null&&Field(window,"discoveryRegionRequest")==null;
    }
    static async Task WaitPosters(MainWindow window,string stage,int minimum=4)
    {
        var until=DateTime.UtcNow.AddSeconds(110);string last="";var stable=DateTime.UtcNow;
        while(DateTime.UtcNow<until&&DateTime.UtcNow<Deadline)
        {
            await Settle(window);
            var posters=VisiblePosters(window);
            var signature=CatalogSettled(window)+":"+string.Join("|",posters.Select(image=>((MediaItem)image.DataContext).PageUrl+":"+Loaded(image)));
            if(signature!=last){last=signature;stable=DateTime.UtcNow;}
            // A real source may return fewer cards than a complete row. Require
            // every actual visible poster, plus settled source/quality work.
            if(CatalogSettled(window)&&posters.Length>0&&posters.Count(Loaded)>=Math.Min(minimum,posters.Length)&&posters.All(Loaded)&&DateTime.UtcNow-stable>TimeSpan.FromSeconds(4))return;
            await Task.Delay(200);
        }
        var current=VisiblePosters(window);var loaded=current.Count(Loaded);
        if(!CatalogSettled(window)||current.Length==0||loaded<Math.Min(minimum,current.Length)||current.Any(image=>!Loaded(image)))throw new InvalidOperationException(stage+": real application loaded only "+loaded+" visible source posters; pending="+current.Count(image=>!Loaded(image))+"; catalogue error: "+Field(window,"liveError"));
        Console.WriteLine(stage+": actual source posters loaded="+loaded+", unavailable or still pending="+current.Count(image=>!Loaded(image)));
    }
    static async Task WaitFeature(MainWindow window)
    {
        var until=DateTime.UtcNow.AddSeconds(18);
        while(DateTime.UtcNow<until&&DateTime.UtcNow<Deadline)
        {
            var view=Field(window,"carousel");
            if(view==null||Field(view,"SummaryRequest")==null)break;
            await Task.Delay(200);
        }
        await Settle(window);
    }
    static void Save(MainWindow window,string filename)
    {
        window.UpdateLayout();
        if(VisiblePosters(window).Any(image=>!Loaded(image)))throw new InvalidOperationException("Refusing to capture pending visible posters: "+filename);
        var content=window.Content as FrameworkElement??throw new InvalidOperationException("Actual application client area is missing.");
        var bitmap=CaptureClient(window);var width=bitmap.PixelWidth;var height=bitmap.PixelHeight;
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(outputDirectory,filename)))png.Save(file);
        var posters=VisiblePosters(window);
        captures.Add(new{File=filename,Language=CaptureLanguage,Width=width,Height=height,LoadedVisiblePosters=posters.Count(Loaded),PendingVisiblePosters=posters.Count(image=>!Loaded(image)),
            Titles=posters.Where(Loaded).Select(image=>((MediaItem)image.DataContext).Title).Distinct().ToArray()});
        Console.WriteLine("Captured "+filename+" from actual public "+typeof(Kachalka.App).Assembly.GetName().Version!.ToString(3)+" client area.");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X,Y; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out NativeRect rectangle);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint operation);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static BitmapSource CaptureClient(MainWindow window)
    {
        // Read the actual composed Windows pixels. RenderTargetBitmap can
        // retain clipped glyph cache regions after live text/layout updates.
        var handle=new WindowInteropHelper(window).Handle;
        if(!GetClientRect(handle,out var rectangle))throw new InvalidOperationException("Cannot measure the released Windows client area.");
        var origin=new NativePoint();
        if(!ClientToScreen(handle,ref origin))throw new InvalidOperationException("Cannot locate the released application on the capture desktop.");
        DwmFlush();
        var width=rectangle.Right-rectangle.Left;var height=rectangle.Bottom-rectangle.Top;
        var desktop=GetDC(IntPtr.Zero);var memory=CreateCompatibleDC(desktop);var bitmap=CreateCompatibleBitmap(desktop,width,height);var previous=SelectObject(memory,bitmap);
        try
        {
            if(!BitBlt(memory,0,0,width,height,desktop,origin.X,origin.Y,0x00CC0020|0x40000000))throw new InvalidOperationException("Windows client capture failed.");
            var source=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();var pixels=new byte[width*height*4];source.CopyPixels(pixels,width*4,0);
            if(pixels.Distinct().Count()<32)throw new InvalidOperationException("Windows did not compose the real application; refusing a blank screenshot.");
            return source;
        }
        finally {SelectObject(memory,previous);DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(IntPtr.Zero,desktop);}
    }

    static async Task Capture(Kachalka.App app)
    {
        await Until(()=>app.MainWindow is MainWindow window&&window.IsLoaded,TimeSpan.FromSeconds(25),"Actual public application startup did not create its main window.");
        var window=(MainWindow)app.MainWindow;
        if(AppLanguage.Current!=CaptureLanguage)throw new InvalidOperationException("Released app did not apply the requested interface language.");
        window.Topmost=true;window.Activate();
        window.WindowState=WindowState.Normal;window.MaxWidth=1920;window.MaxHeight=1200;window.Width=1440;window.Height=900;window.Left=0;window.Top=0;
        await Settle(window);
        if(SettingsOnly)
        {
            await CaptureSettings(window);
            WriteCaptureInfo();
            return;
        }
        await Until(()=>Field(window,"liveLoading") is false&&Field(window,"liveItems") is IEnumerable rows&&rows.Cast<object>().Any(),TimeSpan.FromSeconds(80),"Actual default source catalogue did not load.");
        await Task.Delay(2000);await WaitPosters(window,"Film catalogue");await Task.Delay(8000);await WaitPosters(window,"Final film catalogue");await WaitFeature(window);await WaitPosters(window,"Settled film catalogue");Save(window,"catalog-dark.png");
        Call(window,"ToggleTheme",window,new RoutedEventArgs());await Settle(window);await WaitPosters(window,"Light film catalogue");Save(window,"catalog-light.png");
        Call(window,"ToggleTheme",window,new RoutedEventArgs());await Settle(window);
        var movieButton=Visuals<Button>(window).FirstOrDefault(button=>button.Tag is MediaItem movie&&movie.Section=="Фильмы"&&movie.IsLive&&
            Visuals<Image>(button).Any(image=>image.DataContext is MediaItem&&Loaded(image)));
        if(movieButton!=null)
        {
            movieButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle(window);
            var movie=Field(window,"current") as MediaItem;
            if(movie!=null)
            {
                await Task.Delay(4000);await WaitPosters(window,"Real film detail",1);Save(window,"detail-dark.png");
            }
            else skipped.Add("Detail navigation did not open a genuine source card.");
        }
        else skipped.Add("No genuine loaded film card was available for detail navigation.");
        Call(window,"ShowCatalogSection","Сериалы");await Settle(window);
        await Until(()=>Field(window,"liveLoading") is false&&Field(window,"liveItems") is IEnumerable rows&&rows.Cast<object>().Any(),TimeSpan.FromSeconds(70),"Actual series source catalogue did not load.");
        await Task.Delay(2000);await WaitPosters(window,"Series catalogue");await WaitFeature(window);await WaitPosters(window,"Settled series catalogue");Save(window,"series-dark.png");
        Call(window,"ShowGames");await Settle(window);
        // 0.42.0 opened a plain list; later releases open a home page whose shelves hold the games.
        await Until(()=>Field(window,"gameLoading") is false&&!(Field(window,"gameShelfLoading") is IEnumerable loadingShelves&&loadingShelves.Cast<object>().Any())&&(Field(window,"gameItems") is IEnumerable games&&games.Cast<object>().Any()||Field(window,"gameShelfItems") is IDictionary shelves&&shelves.Count>0),TimeSpan.FromSeconds(60),"Actual Steam game catalogue did not load.");
        await WaitPosters(window,"Steam game catalogue");await Task.Delay(8000);await WaitFeature(window);await WaitPosters(window,"Settled game catalogue");Save(window,"games-dark.png");
        Call(window,"ToggleTheme",window,new RoutedEventArgs());await Settle(window);await WaitPosters(window,"Light game catalogue");Save(window,"games-light.png");
        Call(window,"ToggleTheme",window,new RoutedEventArgs());await Settle(window);
        var gameButtons=Visuals<Button>(window).Where(button=>button.Tag is MediaItem game&&game.Section=="Игры"&&Visuals<Image>(button).Any(Loaded)).ToArray();
        var gameButton=gameButtons.FirstOrDefault(button=>button.Tag is MediaItem game&&game.PageUrl=="https://store.steampowered.com/app/730/")
            ??gameButtons.FirstOrDefault()??throw new InvalidOperationException("No real loaded Steam card was available to open.");
        gameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle(window);
        await Until(()=>Field(window,"gameDescription") is TextBlock description&&description.Text.Length>0&&!description.Text.StartsWith("Загружаем")&&!description.Text.Contains("временно недоступно")&&!description.Text.StartsWith("Loading")&&!description.Text.Contains("temporarily unavailable"),TimeSpan.FromSeconds(45),"Real Steam game description did not load.");
        await Until(()=>Field(window,"gameReleasesLoading") is false,TimeSpan.FromSeconds(30),"Real game source search did not complete.");
        await WaitPosters(window,"Steam game detail",1);Save(window,"game-detail-dark.png");
        Call(window,"ShowDownloads",window,new RoutedEventArgs());await Settle(window);Save(window,"downloads-dark.png");
        await CaptureSettings(window);
        WriteCaptureInfo();
    }

    static async Task CaptureSettings(MainWindow window)
    {
        var settings=Visuals<Button>(window).FirstOrDefault(button=>button.Name=="SettingsButton")??throw new InvalidOperationException("Actual settings navigation is missing.");
        settings.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle(window);
        if(!SettingsOnly)Save(window,"settings-dark.png");
        foreach(var (section,filename,controls) in new[]
        {
            ("Внешний вид","settings-lite-dark.png",new[]{"SettingsLiteMode"}),
            ("Устройства в сети","settings-lan-dark.png",new[]{"PhoneRemoteStart","SettingsLanEnabled"})
        })
        {
            var jump=Visuals<Button>(window).SingleOrDefault(button=>AutomationProperties.GetName(button)==AppLanguage.T("Раздел: "+section))
                ??throw new InvalidOperationException("Actual settings section navigation is missing: "+section);
            jump.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle(window);
            foreach(var name in controls)
            {
                var control=Visuals<FrameworkElement>(window).SingleOrDefault(element=>element.Name==name)
                    ??throw new InvalidOperationException("Actual released settings control is missing: "+name);
                DependencyObject? ancestor=control;
                while(ancestor!=null&&ancestor is not ScrollViewer)ancestor=VisualTreeHelper.GetParent(ancestor);
                var scroll=ancestor as ScrollViewer??throw new InvalidOperationException("Settings scroll viewport is missing: "+name);
                var bounds=control.TransformToAncestor(scroll).TransformBounds(new Rect(control.RenderSize));
                if(!control.IsVisible||bounds.Top<0||bounds.Bottom>scroll.ActualHeight||bounds.Left<0||bounds.Right>scroll.ActualWidth)
                    throw new InvalidOperationException("Settings control is clipped or hidden: "+name);
            }
            Save(window,filename);
        }
    }

    static void WriteCaptureInfo()
    {
        var releasedDll=Path.Combine(releaseDirectory,"Kachalka.dll");
        File.WriteAllText(Path.Combine(outputDirectory,"capture-info.json"),JsonSerializer.Serialize(new
        {
            Version=typeof(Kachalka.App).Assembly.GetName().Version!.ToString(3),ReleaseDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(releasedDll))),
            Language=CaptureLanguage,CaptureMethod="Windows desktop client pixels",ActualApplicationStartup=true,ModifiedApplicationBinary=false,SyntheticMedia=false,Utc=DateTimeOffset.UtcNow,Captures=captures,Skipped=skipped
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}

// The Windows runner starts with a 1024 × 768 desktop. Configure the display
// through Windows before WPF caches monitor bounds, then restore it on exit.
// This changes only the temporary capture desktop, never application state.
internal sealed class CaptureDesktop(CaptureDesktop.DisplayMode original,bool changed):IDisposable
{
    const int CurrentSettings=-1;
    const uint PixelDimensions=0x00080000|0x00100000,Test=0x00000002;

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    internal struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName;
        public ushort SpecVersion,DriverVersion,Size,DriverExtra;
        public uint Fields;
        public int PositionX,PositionY;
        public uint DisplayOrientation,DisplayFixedOutput;
        public short Color,Duplex,YResolution,TTOption,Collate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel,PelsWidth,PelsHeight,DisplayFlags,DisplayFrequency;
        public uint IcmMethod,IcmIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="EnumDisplaySettingsW")]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplaySettings(string? deviceName,int mode,ref DisplayMode settings);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="ChangeDisplaySettingsW")]
    static extern int ChangeDisplaySettings(ref DisplayMode mode,uint flags);

    public static CaptureDesktop Create()
    {
        var current=new DisplayMode{Size=(ushort)Marshal.SizeOf<DisplayMode>(),DeviceName="",FormName=""};
        if(!EnumDisplaySettings(null,CurrentSettings,ref current))throw new InvalidOperationException("Windows did not report its capture display mode.");
        if(current.PelsWidth>=1600&&current.PelsHeight>=1000)return new(current,false);
        foreach(var (width,height) in new[]{(1920u,1080u),(1600u,1000u)})
        {
            var requested=current;requested.Fields=PixelDimensions;requested.PelsWidth=width;requested.PelsHeight=height;
            var result=ChangeDisplaySettings(ref requested,Test);
            if(result==0)result=ChangeDisplaySettings(ref requested,0);
            Console.WriteLine("Windows capture display "+width+"x"+height+": "+result);
            if(result==0)return new(current,true);
        }
        throw new InvalidOperationException("Windows capture desktop does not support a resolution large enough for the actual 1440 × 900 application window.");
    }
    public void Dispose()
    {
        if(!changed)return;
        var restore=original;restore.Fields=PixelDimensions;
        ChangeDisplaySettings(ref restore,0);
    }
}

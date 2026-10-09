using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kachalka;

internal static class Program
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static string releaseDirectory="",outputDirectory="";
    static readonly List<object> captures=[];
    static readonly List<string> skipped=[];
    static readonly DateTime Deadline=DateTime.UtcNow.AddSeconds(270);

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
        if(version!="0.39.0")throw new InvalidOperationException("Screenshot driver requires the public 0.39.0 assembly, got "+version);
        var state=Path.Combine(outputDirectory,"isolated-state-"+Guid.NewGuid().ToString("N"));
        var downloads=Path.Combine(state,"Downloads","Ka4alka");Directory.CreateDirectory(downloads);
        Environment.SetEnvironmentVariable("KACHALKA_DATA",state);
        File.WriteAllText(Path.Combine(state,"settings.json"),JsonSerializer.Serialize(new
        {
            Folder=downloads,FolderConfigured=true,Light=false,CheckForUpdates=false,AutoUpdate=false,
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
        .Where(image=>image.IsLoaded&&image.DataContext is MediaItem&&PosterVisible(window,image)).ToArray();
    static bool Loaded(Image image)=>image.Source is BitmapSource bitmap&&bitmap.PixelWidth>8&&bitmap.PixelHeight>8;

    static async Task WaitPosters(MainWindow window,string stage,int minimum=4)
    {
        var until=DateTime.UtcNow.AddSeconds(45);string last="";var stable=DateTime.UtcNow;
        while(DateTime.UtcNow<until&&DateTime.UtcNow<Deadline)
        {
            await Settle(window);
            var posters=VisiblePosters(window);
            var signature=string.Join("|",posters.Select(image=>((MediaItem)image.DataContext).PageUrl+":"+Loaded(image)));
            if(signature!=last){last=signature;stable=DateTime.UtcNow;}
            if(posters.Count(Loaded)>=minimum&&posters.All(Loaded)&&DateTime.UtcNow-stable>TimeSpan.FromSeconds(2))return;
            await Task.Delay(200);
        }
        var current=VisiblePosters(window);var loaded=current.Count(Loaded);
        if(loaded<minimum)throw new InvalidOperationException(stage+": real application loaded only "+loaded+" visible source posters; catalogue error: "+Field(window,"liveError"));
        Console.WriteLine(stage+": actual source posters loaded="+loaded+", unavailable or still pending="+current.Count(image=>!Loaded(image)));
    }
    static void FreezeRefresh(MainWindow window)
    {
        if(Field(window,"catalogRefreshTimer") is DispatcherTimer timer)timer.Stop();
        // Only pause background source validation once actual catalogue records
        // have arrived. Existing data, source ratings and image bindings remain.
        foreach(var name in new[]{"CancelCatalogQualityCheck","CancelDiscoveryQualityCheck"})
        {
            var method=window.GetType().GetMethod(name,Members,[]);
            method?.Invoke(window,null);
        }
    }
    static void Save(MainWindow window,string filename)
    {
        window.UpdateLayout();var content=window.Content as FrameworkElement??throw new InvalidOperationException("Actual application client area is missing.");
        var dpi=VisualTreeHelper.GetDpi(content);var width=(int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX);var height=(int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY);
        var bitmap=new RenderTargetBitmap(width,height,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        // Window.Background is normally painted by the containing Window.
        // Render its actual brush under the actual client area, just as WPF
        // does onscreen, while excluding the native title bar and frame.
        var background=new DrawingVisual();
        using(var drawing=background.RenderOpen())drawing.DrawRectangle(window.Background,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));
        bitmap.Render(background);bitmap.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(outputDirectory,filename)))png.Save(file);
        var posters=VisiblePosters(window);
        captures.Add(new{File=filename,Width=width,Height=height,LoadedVisiblePosters=posters.Count(Loaded),PendingVisiblePosters=posters.Count(image=>!Loaded(image)),
            Titles=posters.Where(Loaded).Select(image=>((MediaItem)image.DataContext).Title).Distinct().ToArray()});
        Console.WriteLine("Captured "+filename+" from actual public 0.39.0 client area.");
    }

    static async Task Capture(Kachalka.App app)
    {
        await Until(()=>app.MainWindow is MainWindow window&&window.IsLoaded,TimeSpan.FromSeconds(25),"Actual public application startup did not create its main window.");
        var window=(MainWindow)app.MainWindow;
        window.WindowState=WindowState.Normal;window.MaxWidth=1920;window.MaxHeight=1200;window.Width=1440;window.Height=900;window.Left=0;window.Top=0;
        await Settle(window);
        await Until(()=>Field(window,"liveLoading") is false&&Field(window,"liveItems") is IEnumerable rows&&rows.Cast<object>().Any(),TimeSpan.FromSeconds(80),"Actual default source catalogue did not load.");
        await Task.Delay(2000);FreezeRefresh(window);await WaitPosters(window,"Film catalogue");await Task.Delay(8000);await Settle(window);Save(window,"catalog-dark.png");
        Call(window,"ToggleTheme",window,new RoutedEventArgs());await Settle(window);FreezeRefresh(window);await WaitPosters(window,"Light film catalogue");Save(window,"catalog-light.png");
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
        await Task.Delay(2000);FreezeRefresh(window);await WaitPosters(window,"Series catalogue");Save(window,"series-dark.png");
        var settings=Visuals<Button>(window).FirstOrDefault(button=>button.Name=="SettingsButton")??throw new InvalidOperationException("Actual settings navigation is missing.");
        settings.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle(window);Save(window,"settings-dark.png");
        var releasedDll=Path.Combine(releaseDirectory,"Kachalka.dll");
        File.WriteAllText(Path.Combine(outputDirectory,"capture-info.json"),JsonSerializer.Serialize(new
        {
            Version=typeof(Kachalka.App).Assembly.GetName().Version!.ToString(3),ReleaseDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(releasedDll))),
            ActualApplicationStartup=true,ModifiedApplicationBinary=false,SyntheticMedia=false,Utc=DateTimeOffset.UtcNow,Captures=captures,Skipped=skipped
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

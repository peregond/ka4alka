using System.Windows;
namespace Kachalka;
public partial class App : Application
{
    Mutex? instance, installPresence;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Preferences.DataDir)))[..16];
        instance=new Mutex(true,@"Local\Kachalka-"+key,out bool first);
        if(!first){if(!e.Args.Contains("--startup"))MessageBox.Show("Качалка уже запущена.","Качалка");Shutdown();return;}
        installPresence=new Mutex(false,@"Local\Kachalka-Install");
        DispatcherUnhandledException += (_, args) => { ErrorLog.Write(args.Exception); MessageBox.Show(args.Exception.Message, "Качалка"); args.Handled = true; };
        var window=new MainWindow();
        if(e.Args.Length==2 && e.Args[0]=="--update-health" && Guid.TryParseExact(e.Args[1],"N",out _))
        {
            var marker=System.IO.Path.Combine(Preferences.DataDir,"updates",e.Args[1],"healthy");
            bool acknowledged=false;
            window.ContentRendered+=(_,_)=>
            {
                if(acknowledged)return;acknowledged=true;
                System.IO.File.WriteAllText(marker,typeof(MainWindow).Assembly.GetName().Version!.ToString(3));
            };
        }
        if(e.Args.Length==2&&e.Args[0]=="--design-smoke-test")
        {
            try{window.PrepareDesignSmoke();}
            catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();return;}
        }
        // Smoke tests take screenshots and measure pixels, so they run without the decorative motion.
        Kachalka.MainWindow.MotionAllowed=!e.Args.Any(arg=>arg.StartsWith("--",StringComparison.Ordinal)&&arg.EndsWith("-smoke-test",StringComparison.Ordinal));
        if(!e.Args.Any(arg=>arg.StartsWith("--",StringComparison.Ordinal)&&arg.EndsWith("-smoke-test",StringComparison.Ordinal)))
        {
            bool prompted=false;
            window.ContentRendered+=async(_,_)=>{if(prompted)return;prompted=true;window.EnsureFirstRunSetup();await window.CheckUpdatesAsync();};
        }
        if(e.Args.Length==2&&e.Args[0]=="--design-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.DesignSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--live-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.LiveSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--broadcast-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.BroadcastSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.SmokeTest(e.Args[1]);}catch(Exception error){System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--layout-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.LayoutSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--end-to-end-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.EndToEndSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--catalog-paging-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.CatalogPagingSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--dpi-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.DpiSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--close-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.CloseSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--cinema-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.CinemaSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--cache-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.CacheSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--download-reliability-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.DownloadReliabilitySmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--person-performance-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.PersonPerformanceSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        void RegisterSmoke(string flag,Func<string,Task> check)
        {
            if(e.Args.Length!=2||e.Args[0]!=flag)return;
            bool running=false;
            window.ContentRendered+=async(_,_)=>
            {
                if(running)return;running=true;
                try{await check(e.Args[1]);}
                catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}
            };
        }
        RegisterSmoke("--cover-viewport-smoke-test",window.CoverViewportSmokeTest);
        RegisterSmoke("--catalog-navigation-smoke-test",window.CatalogNavigationSmokeTest);
        RegisterSmoke("--release-freshness-smoke-test",window.ReleaseFreshnessSmokeTest);
        RegisterSmoke("--interface-polish-smoke-test",window.InterfacePolishSmokeTest);
        RegisterSmoke("--bug-report-smoke-test",window.BugReportSmokeTest);
        RegisterSmoke("--lan-smoke-test",window.LanSmokeTest);
        RegisterSmoke("--text-rendering-smoke-test",window.TextRenderingSmokeTest);
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e){installPresence?.Dispose();instance?.Dispose();base.OnExit(e);}
}

using System.Windows;
namespace Kachalka;
public partial class App : Application
{
    Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Preferences.DataDir)))[..16];
        instance=new Mutex(true,@"Local\Kachalka-"+key,out bool first);
        if(!first){MessageBox.Show("Качалка уже запущена.","Качалка");Shutdown();return;}
        DispatcherUnhandledException += (_, args) => { ErrorLog.Write(args.Exception); MessageBox.Show(args.Exception.Message, "Качалка"); args.Handled = true; };
        var window=new MainWindow();
        if(e.Args.Length==2&&e.Args[0]=="--design-smoke-test")
        {
            try{window.PrepareDesignSmoke();}
            catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();return;}
        }
        if(!e.Args.Any(arg=>arg.StartsWith("--",StringComparison.Ordinal)&&arg.EndsWith("-smoke-test",StringComparison.Ordinal)))
        {
            bool prompted=false;
            window.ContentRendered+=(_,_)=>{if(prompted)return;prompted=true;window.EnsureDownloadFolder();};
        }
        if(e.Args.Length==2&&e.Args[0]=="--design-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.DesignSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--live-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.LiveSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--broadcast-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.BroadcastSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.SmokeTest(e.Args[1]);}catch(Exception error){System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--layout-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.LayoutSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        if(e.Args.Length==2&&e.Args[0]=="--end-to-end-smoke-test"){bool running=false;window.ContentRendered+=async(_,_)=>{if(running)return;running=true;try{await window.EndToEndSmokeTest(e.Args[1]);}catch(Exception error){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"error.txt"),error.ToString());window.Close();}};}
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
}

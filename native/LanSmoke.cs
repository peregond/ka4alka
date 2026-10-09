using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kachalka.Lan;
using MonoTorrent;
using MonoTorrent.Client;

namespace Kachalka;

public partial class MainWindow
{
    public async Task LanSmokeTest(string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var originalService=lanService;var originalEnabled=prefs.LanEnabled;var originalName=prefs.LanDeviceName;
        var originalLight=prefs.Light;var originalSection=section;var originalCurrent=current;
        var originalData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        var localQueueIds=downloads.Items.Select(item=>item.Id).ToArray();
        var windows=new List<Window>();var checks=new List<string>();
        LanService? sender=null,receiver=null,defaultService=null;DownloadService? receivingDownloads=null;
        var diagnosticGate=new object();
        static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks.Add(message);}
        async Task Until(Func<bool> condition,string message,int seconds=12)
        {
            var until=DateTime.UtcNow.AddSeconds(seconds);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(25);
            Check(condition(),message);
        }
        async Task Settle(Window dialog)
        {
            dialog.UpdateLayout();await dialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(90);dialog.UpdateLayout();
        }
        T Find<T>(Window dialog,string name)where T:FrameworkElement=>FindVisual<T>(dialog,element=>element.Name==name)??throw new Exception("Missing LAN control "+name);
        int Subscriptions(LanService service)=>(typeof(LanService).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(service)as Delegate)?.GetInvocationList().Length??0;
        void Shot(Window dialog,string name)
        {
            dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
        }
        void CheckGeometry(Window dialog,string context)
        {
            var root=(FrameworkElement)dialog.Content;
            var scroll=FindVisual<ScrollViewer>(dialog,value=>value.Content is StackPanel)??throw new Exception("LAN dialog has no content scroller.");
            Check(scroll.ViewportWidth>=235&&((FrameworkElement)scroll.Content).ActualWidth<=scroll.ViewportWidth+1,context+": content fits the viewport without horizontal overflow.");
            void Visit(DependencyObject element)
            {
                if(element is Control control&&control.IsVisible&&(control is System.Windows.Controls.Button or TextBox))
                {
                    var point=control.TranslatePoint(new Point(),root);
                    Check(point.X>=-1&&point.X+control.ActualWidth<=root.ActualWidth+1,context+": "+(control.Name.Length>0?control.Name:control.GetType().Name)+" fits the available width.");
                }
                for(var index=0;index<VisualTreeHelper.GetChildrenCount(element);index++)Visit(VisualTreeHelper.GetChild(element,index));
            }
            Visit(root);
        }
        async Task Click(Window dialog,ButtonBase button)
        {
            dialog.Activate();button.BringIntoView();await Settle(dialog);SetForegroundWindow(new WindowInteropHelper(dialog).Handle);
            var clicks=0;RoutedEventHandler handler=(_,_)=>clicks++;button.Click+=handler;
            try
            {
                var point=button.PointToScreen(new Point(button.ActualWidth*.5,button.ActualHeight*.5));
                Check(button.IsEnabled&&button.ActualWidth>0&&button.ActualHeight>0,"Native LAN click targets an enabled, measured action.");
                if(!SetCursorPos((int)point.X,(int)point.Y))throw new Exception("Cannot position native LAN input.");
                mouse_event(2,0,0,0,0);await Task.Delay(60);mouse_event(4,0,0,0,0);await Task.Delay(110);
                Check(clicks==1,"Native LAN pointer activates its action exactly once.");
            }
            finally{button.Click-=handler;}
        }
        void NetworkError(Exception error){lock(diagnosticGate)File.AppendAllText(Path.Combine(output,"lan-network-errors.log"),error+Environment.NewLine);}
        LanServiceOptions Options(string directory,string name)=>new(){StateDirectory=Path.Combine(output,directory),DeviceName=name,TcpPort=0,DiscoveryPort=0,AllowLoopbackForTests=true,RequestTimeout=TimeSpan.FromSeconds(20),PairingTimeout=TimeSpan.FromMinutes(2),DiagnosticError=NetworkError};
        try
        {
            Check(!new Preferences().LanEnabled,"A fresh installation leaves LAN download reception disabled until the user enables it.");
            Check(!string.IsNullOrWhiteSpace(DeviceDisplayName.Default())&&DeviceDisplayName.Default().Length<=60,"Native Windows device naming supplies a readable laptop model or machine-name fallback.");
            lanService=null;prefs.LanEnabled=false;section="Настройки";current=null;Render();await Settle(this);
            var initialToggle=FindVisual<CheckBox>(Body,value=>value.Name=="SettingsLanEnabled")??throw new Exception("LAN settings switch is missing.");
            var modelName=FindVisual<TextBox>(Body,value=>value.Name=="SettingsLanDeviceName")??throw new Exception("LAN model name is missing.");
            Check(initialToggle.IsChecked==false&&!string.IsNullOrWhiteSpace(modelName.Text)&&modelName.Text==LanDeviceDisplayName,"Native LAN settings show the laptop name and an explicit disabled reception switch.");
            var unavailable=CreateLanDownloadWindow(_=>{});windows.Add(unavailable);unavailable.Show();await Settle(unavailable);
            Check(!Find<Button>(unavailable,"LanDownloadConfirm").IsEnabled&&Find<TextBlock>(unavailable,"LanDownloadNotice").Text.Length==0,"An unconfigured LAN chooser cannot send a download or claim a receiver.");
            unavailable.Close();

            var receivingState=Path.Combine(output,"receiver-queue");Directory.CreateDirectory(receivingState);Environment.SetEnvironmentVariable("KACHALKA_DATA",receivingState);
            var engineSettings=new EngineSettingsBuilder{CacheDirectory=Path.Combine(output,"receiver-cache"),DhtEndPoint=null,AllowPortForwarding=false,AllowLocalPeerDiscovery=false};
            engineSettings.ListenEndPoints.Clear();engineSettings.ListenEndPoints.Add("ipv4",new IPEndPoint(IPAddress.Loopback,Port()));
            receivingDownloads=new(engineSettings.ToSettings(),publicTrackers:[]);
            Environment.SetEnvironmentVariable("KACHALKA_DATA",originalData);
            var receivingFolder=Path.Combine(output,"receiver-files","Ka4alka");Directory.CreateDirectory(receivingFolder);
            var receivingPrefs=new Preferences{Folder=receivingFolder,FolderConfigured=true};
            var receivingBridge=new LanDownloadReceiver(receivingDownloads,receivingPrefs,Path.Combine(receivingState,"inbox"));
            sender=new(Options("sender-network","Lenovo ThinkPad · рабочий"));receiver=new(Options("receiver-network","ASUS Zenbook · гостиная"));
            sender.ConfirmPairingAsync=(request,token)=>Dispatcher.InvokeAsync(()=>ConfirmLanPairingAsync(request,token)).Task.Unwrap();
            receiver.ConfirmPairingAsync=(request,token)=>Dispatcher.InvokeAsync(()=>ConfirmLanPairingAsync(request,token)).Task.Unwrap();
            var receives=0;var receivedOnDispatcher=false;LanDownloadRequest? receivedRequest=null;
            receiver.ReceiveDownloadAsync=(request,token)=>Dispatcher.InvokeAsync(async()=>
            {
                receivedOnDispatcher=Dispatcher.CheckAccess();receives++;receivedRequest=request;
                return await receivingBridge.ReceiveAsync(request,token);
            }).Task.Unwrap();
            await sender.StartAsync();await receiver.StartAsync();lanService=sender;prefs.LanEnabled=true;prefs.LanDeviceName=sender.Name;
            var peer=await sender.FindManualAsync("127.0.0.1:"+receiver.Port);
            receiver.AllowPairingFor(TimeSpan.FromMinutes(2));
            var pairing=sender.PairAsync(peer);
            await Until(()=>lanPairingWindows.Count==2,"Both real TLS peers open native code-confirmation windows before trust is saved.");
            var approvals=lanPairingWindows.ToArray();windows.AddRange(approvals);
            var codes=approvals.Select(window=>Find<TextBlock>(window,"LanPairingCode").Text).ToArray();
            Check(codes[0]==codes[1]&&codes[0].Count(Uri.IsHexDigit)==12,"Both native pairing windows show the same certificate-bound 48-bit comparison code.");
            foreach(var light in new[]{false,true})
            {
                prefs.Light=light;ApplyTheme();var dialog=approvals[0];dialog.Width=360;dialog.Height=410;await Settle(dialog);
                Shot(dialog,"lan-pairing-narrow-"+(light?"light":"dark"));
                var code=Find<TextBlock>(dialog,"LanPairingCode");var frame=(Border)VisualTreeHelper.GetParent(code);
                Check(code.ActualWidth<=frame.ActualWidth-frame.Padding.Left-frame.Padding.Right+1,"Pairing comparison code remains completely readable in a 360-DIP "+(light?"light":"dark")+" window.");
                CheckGeometry(dialog,"pairing 360 "+(light?"light":"dark"));
            }
            foreach(var dialog in approvals){dialog.Width=440;dialog.Height=410;await Settle(dialog);await Click(dialog,Find<Button>(dialog,"LanPairingConfirm"));}
            await pairing;Check(lanPairingWindows.Count==0&&sender.Devices.Single(device=>device.Id==receiver.Id).Paired&&receiver.Devices.Single(device=>device.Id==sender.Id).Paired,"Only both native confirmations create trusted peers, and the pairing windows release their lifetimes afterwards.");

            var beforeManagement=Subscriptions(sender);var management=CreateLanDevicesWindow();windows.Add(management);management.Show();await Settle(management);
            Check(FindVisual<Button>(management,button=>button.Name=="LanForgetDevice_"+receiver.Id.ToString("N"))!=null,"Device management displays the actual paired laptop and a forget-device action.");
            Find<Button>(management,"LanManualToggle").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            foreach(var light in new[]{false,true})
            foreach(var width in new[]{320d,360d,580d})
            {
                prefs.Light=light;ApplyTheme();management.Width=width;management.Height=560;await Settle(management);
                Shot(management,"lan-devices-"+(int)width+"-"+(light?"light":"dark"));CheckGeometry(management,"devices "+width+" "+(light?"light":"dark"));
            }
            management.Close();await Settle(this);Check(Subscriptions(sender)==beforeManagement,"Closing device management removes its service-event subscription.");
            var beforeChooser=Subscriptions(sender);var chooserResults=new List<LanDevice?>();var chooser=CreateLanDownloadWindow(device=>chooserResults.Add(device));windows.Add(chooser);chooser.Show();await Settle(chooser);
            Check(!Find<Button>(chooser,"LanDownloadConfirm").IsEnabled,"Sending stays disabled until a specific paired laptop is selected.");
            foreach(var light in new[]{false,true})
            foreach(var width in new[]{320d,360d,510d})
            {
                prefs.Light=light;ApplyTheme();chooser.Width=width;chooser.Height=480;await Settle(chooser);
                Shot(chooser,"lan-send-"+(int)width+"-"+(light?"light":"dark"));CheckGeometry(chooser,"send "+width+" "+(light?"light":"dark"));
            }
            chooser.Close();await Settle(this);Check(chooserResults.Count==1&&chooserResults[0]==null&&Subscriptions(sender)==beforeChooser,"Canceling the chooser completes once without sending and unsubscribes from device updates.");
            var declinedTask=ConfirmLanPairingAsync(new(){Device=peer,VerificationCode=codes[0],ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(1),Incoming=true},CancellationToken.None);
            await Until(()=>lanPairingWindows.Count==1,"An explicit native pairing cancellation presents its confirmation dialog.");
            var declinedDialog=lanPairingWindows.Single();windows.Add(declinedDialog);await Click(declinedDialog,Find<Button>(declinedDialog,"LanPairingReject"));
            Check(!await declinedTask&&lanPairingWindows.Count==0,"The native Cancel action refuses pairing and cleans its timer and dialog registration.");
            using(var cancellation=new CancellationTokenSource())
            {
                var canceledTask=ConfirmLanPairingAsync(new(){Device=peer,VerificationCode=codes[0],ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(1)},cancellation.Token);
                await Until(()=>lanPairingWindows.Count==1,"A cancellable native pairing dialog has an active lifetime.");cancellation.Cancel();
                Check(!await canceledTask&&lanPairingWindows.Count==0,"Canceling a pending pairing token closes its native dialog without granting trust.");
            }

            var seed=Path.Combine(output,"authored-lan-ui.bin");await File.WriteAllBytesAsync(seed,RandomNumberGenerator.GetBytes(16*1024));
            var torrent=Path.Combine(output,"authored-lan-ui.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(seed),torrent);
            var magnet="magnet:?xt=urn:btih:"+Torrent.Load(torrent).InfoHashes.V1!.ToHex()+"&dn=authored-lan-ui.bin";
            var movie=new MediaItem(73902,"Фильм на ноутбук в гостиной","Фильмы","драма",2026,"8.1","7.7","#526B69"){PageUrl="https://w6.zona.plus/movies/authored-lan-ui"};
            var release=new SourceEntry("lan-ui-authored","Authored LAN UI 1080p WEB-DL","Локальный тест","https://example.org/authored-lan-release",magnet,null,16*1024,1);
            prefs.Light=false;ApplyTheme();section="Фильмы";current=movie;
            var action=LanDownloadAction(release);Body.Children.Clear();Body.Children.Add(action);await Settle(this);
            var remote=FindVisual<Button>(action,button=>AutomationProperties.GetName(button)=="Скачать на другом устройстве: "+release.Title)??throw new Exception("Remote release action is missing.");
            remote.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Until(()=>lanDownloadWindow?.IsVisible==true,"The actual release action opens the native target chooser.");
            var sending=lanDownloadWindow!;windows.Add(sending);await Settle(sending);
            var choice=Find<Button>(sending,"LanDownloadDevice_"+receiver.Id.ToString("N"));await Click(sending,choice);
            await Click(sending,Find<Button>(sending,"LanDownloadConfirm"));
            await Until(()=>receivingDownloads.Items.Count==1&&remote.IsEnabled,"Production remote-download action receives acknowledgement after the actual native receiver queue is saved.");
            var received=receivingDownloads.Items.Single();
            Check(receives==1&&receivedOnDispatcher&&received.DisplayName==movie.Title&&received.Folder==receivingFolder&&received.RemoteSenderId==sender.Id.ToString("N")&&received.RemoteSenderFingerprint==sender.Fingerprint,"A real paired LAN request enters the receiver's MonoTorrent queue on the GUI dispatcher with the authenticated laptop identity.");
            Check(downloads.Items.Select(item=>item.Id).SequenceEqual(localQueueIds)&&Status.Text.StartsWith("Загрузка отправлена на «"+receiver.Name),"Production remote send adds no local download and reports the receiving laptop only after acknowledgement.");
            var retry=await sender.SendDownloadAsync(sender.Devices.Single(device=>device.Id==receiver.Id),receivedRequest!);
            Check(retry.Accepted&&retry.DownloadId==received.Id&&receives==1&&receivingDownloads.Items.Count==1,"A real encrypted retry returns the persisted receipt without duplicating native queue work.");
            section="Настройки";current=null;Render();await Settle(this);
            var nameField=FindVisual<TextBox>(Body,box=>box.Name=="SettingsLanDeviceName")!;nameField.Text="Ноутбук у телевизора";
            var saveName=FindVisual<Button>(Body,button=>button.Name=="SaveLanDeviceName")!;saveName.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Until(()=>prefs.LanDeviceName=="Ноутбук у телевизора"&&sender.Name==prefs.LanDeviceName,"The native name editor saves a custom laptop name to preferences and the real service identity.");
            var toggle=FindVisual<CheckBox>(Body,box=>box.Name=="SettingsLanEnabled")!;toggle.IsChecked=false;toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Until(()=>!prefs.LanEnabled&&lanService==null&&!sender.IsRunning,"The native LAN switch stops reception and releases the service before persisting disabled state.");
            Check(!Preferences.Load().LanEnabled&&Preferences.Load().LanDeviceName=="Ноутбук у телевизора","LAN opt-in and the custom device name persist independently through the native settings controls.");
            await Until(()=>toggle.IsEnabled,"The LAN settings switch becomes available after stopping the previous service.");
            await Click(this,toggle);
            await Until(()=>prefs.LanEnabled&&lanService?.IsRunning==true&&toggle.IsEnabled,"The native settings action starts the production LAN listener with the saved preference name.");
            defaultService=lanService!;var storedInstallation=defaultService.Id;var storedFingerprint=defaultService.Fingerprint;
            Check(defaultService.Name=="Ноутбук у телевизора"&&lanPairingWindows.Count==0,"Starting the production LAN listener restores its name without creating pairing prompts.");
            await Click(this,toggle);
            await Until(()=>!prefs.LanEnabled&&lanService==null&&toggle.IsEnabled&&!defaultService.IsRunning,"The production listener stops cleanly before a disabled-device rename.");
            nameField.Text="Ноутбук после перезапуска";saveName.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Until(()=>prefs.LanDeviceName=="Ноутбук после перезапуска"&&saveName.IsEnabled,"The native name editor accepts and saves a new name while LAN reception is disabled.");
            Check(!Preferences.Load().LanEnabled&&Preferences.Load().LanDeviceName=="Ноутбук после перезапуска"&&lanService==null,"A disabled-device rename persists without silently starting the listener.");
            await Click(this,toggle);
            await Until(()=>prefs.LanEnabled&&lanService?.IsRunning==true&&toggle.IsEnabled,"Re-enabling LAN reception reloads the persisted installation state.");
            defaultService=lanService!;
            Check(defaultService.Name=="Ноутбук после перезапуска"&&defaultService.Id==storedInstallation&&defaultService.Fingerprint==storedFingerprint&&lanPairingWindows.Count==0,"Production startup applies the renamed preference to the restored service identity without changing its certificate or opening pairing dialogs.");
            var localAddresses=LanLocalAddresses(defaultService.Port);
            if(localAddresses.Length>0)
            {
                await using var probe=new LanService(Options("default-name-probe","Проверка имени"));await probe.StartAsync();
                var advertised=await probe.FindManualAsync(localAddresses[0]);
                Check(advertised.Name=="Ноутбук после перезапуска"&&advertised.Id==storedInstallation&&advertised.Fingerprint==storedFingerprint,"The actual TLS hello on the native private interface advertises the renamed production laptop identity.");
            }
            await Click(this,toggle);
            await Until(()=>!prefs.LanEnabled&&lanService==null&&!defaultService.IsRunning&&toggle.IsEnabled,"The renamed production listener can be disabled again without leaking a dialog or socket lifetime.");
            Check(lanPairingWindows.Count==0&&lanDownloadWindow==null,"Restarting and renaming the production listener leaves no pairing or send windows behind.");
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,Receives=receives,ReceivedOnDispatcher=receivedOnDispatcher,LocalQueueUnchanged=true,ActualTlsPairing=true,WindowsDialogs=true},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error)
        {
            var report=error.ToString();
            try
            {
                var log=Path.Combine(output,"lan-network-errors.log");
                if(File.Exists(log))
                {
                    using var stream=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    var bytes=new byte[(int)Math.Min(stream.Length,32768)];stream.Seek(-bytes.Length,SeekOrigin.End);stream.ReadExactly(bytes);
                    var tail=string.Join(Environment.NewLine,Encoding.UTF8.GetString(bytes).Split('\n').Select(line=>line.TrimEnd('\r')).Where(line=>line.Length>0).TakeLast(16));
                    if(tail.Length>0)
                    {
                        var diagnostics="LAN server diagnostics (last 16 lines):"+Environment.NewLine+tail;
                        report+=Environment.NewLine+Environment.NewLine+diagnostics;Console.WriteLine(diagnostics);
                    }
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
            File.WriteAllText(Path.Combine(output,"error.txt"),report);throw;
        }
        finally
        {
            foreach(var window in windows)if(window.IsVisible)window.Close();CloseLanDialogs();
            var activeService=lanService;lanService=originalService;if(activeService!=null&&!ReferenceEquals(activeService,originalService))await activeService.DisposeAsync();
            if(defaultService!=null)await defaultService.DisposeAsync();if(sender!=null)await sender.DisposeAsync();if(receiver!=null)await receiver.DisposeAsync();if(receivingDownloads!=null)await receivingDownloads.Close();
            Environment.SetEnvironmentVariable("KACHALKA_DATA",originalData);prefs.LanEnabled=originalEnabled;prefs.LanDeviceName=originalName;prefs.Light=originalLight;prefs.Save();section=originalSection;current=originalCurrent;ApplyTheme();
        }
        Close();
    }
}

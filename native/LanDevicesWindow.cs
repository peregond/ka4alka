using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Kachalka.Lan;

namespace Kachalka;

public partial class MainWindow
{
    Window? lanDevicesWindow;
    readonly HashSet<Window> lanPairingWindows=[];

    void RenderLanSettings(StackPanel panel)
    {
        var enable=new CheckBox{Name="SettingsLanEnabled",Style=(Style)FindResource("SettingsSwitch"),Content=SwitchTitle("Загрузки на другом компьютере"),IsChecked=prefs.LanEnabled,Margin=new(0,0,0,8)};
        AutomationProperties.SetName(enable,"Разрешить загрузки на связанных устройствах в локальной сети");panel.Children.Add(enable);
        var description=Hint("Включи на обоих компьютерах и свяжи их один раз. Файл скачает выбранный компьютер в свою папку Ka4alka; этот компьютер только передаст задачу.");panel.Children.Add(description);
        var nameLabel=Hint("Имя устройства");nameLabel.Margin=new(0,8,0,6);panel.Children.Add(nameLabel);
        var name=new TextBox{Name="SettingsLanDeviceName",Text=LanDeviceDisplayName,MaxLength=64,Margin=new(0,0,0,8),MinWidth=0,MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Left};
        name.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)){Source=panel});
        AutomationProperties.SetName(name,"Имя этого устройства в локальной сети");panel.Children.Add(name);
        panel.Children.Add(Hint("По умолчанию — модель компьютера. Можно указать своё имя."));
        var actions=new WrapPanel();panel.Children.Add(actions);
        var save=Button("Сохранить имя",()=>{});save.Name="SaveLanDeviceName";save.Margin=new(0,0,8,6);AutomationProperties.SetName(save,"Сохранить имя устройства");actions.Children.Add(save);
        var manage=ActionButton("Устройства","IconSeries",ShowLanDevices);manage.Name="ManageLanDevices";manage.IsEnabled=prefs.LanEnabled;manage.Margin=new(0,0,0,6);AutomationProperties.SetName(manage,"Связать компьютеры и управлять устройствами в сети");actions.Children.Add(manage);
        var status=Text(prefs.LanEnabled?"Связанные устройства могут отправлять загрузки. Для новых устройств всегда потребуется подтверждение на обоих компьютерах.":"Выключено. Входящие сетевые задачи не принимаются.",12,true);status.Name="SettingsLanStatus";status.Margin=new(0,6,0,0);panel.Children.Add(status);
        enable.Click+=async(_,_)=>
        {
            enable.IsEnabled=false;manage.IsEnabled=false;
            try
            {
                await SetLanEnabledAsync(enable.IsChecked==true);
                status.Text=prefs.LanEnabled?"Включено. Открой «Устройства» на обоих компьютерах для первого соединения.":"Выключено. Входящие сетевые задачи не принимаются.";
                status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            }
            catch(Exception error){status.Text="Не удалось изменить настройку: "+error.Message;status.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
            finally{enable.IsChecked=prefs.LanEnabled;enable.IsEnabled=true;manage.IsEnabled=prefs.LanEnabled;}
        };
        save.Click+=async(_,_)=>
        {
            save.IsEnabled=false;
            try{await RenameLanDeviceAsync(name.Text);name.Text=LanDeviceDisplayName;status.Text="Имя устройства сохранено.";status.SetResourceReference(TextBlock.ForegroundProperty,"Accent");}
            catch(Exception error){status.Text="Не удалось сохранить имя: "+error.Message;status.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
            finally{save.IsEnabled=true;}
        };
    }

    Window LanWindow(string title,double width=580,double height=650)
    {
        var area=SystemParameters.WorkArea;
        var window=new Window{Owner=this,Title=title,Width=Math.Min(width,Math.Max(320,area.Width-32)),Height=Math.Min(height,Math.Max(360,area.Height-32)),MinWidth=320,MinHeight=300,WindowStartupLocation=WindowStartupLocation.CenterOwner,UseLayoutRounding=true,SnapsToDevicePixels=true,FontFamily=FontFamily,FontSize=13};
        window.Resources.MergedDictionaries.Add(Resources);
        window.SetResourceReference(Control.BackgroundProperty,"Bg");window.SetResourceReference(Control.ForegroundProperty,"Text");
        window.SourceInitialized+=(_,_)=>SystemWindowTheme.Apply(window,prefs.Light);
        return window;
    }

    static bool LanRecentlySeen(LanDevice device)=>DateTimeOffset.UtcNow-device.LastSeenUtc<TimeSpan.FromSeconds(60);

    static string LanDeviceAddress(LanDevice device)=>$"{device.Address}:{device.Port}";

    void ShowLanDevices()
    {
        if(lanDevicesWindow?.IsVisible==true){lanDevicesWindow.Activate();return;}
        var window=CreateLanDevicesWindow();lanDevicesWindow=window;
        window.Closed+=(_,_)=>{if(ReferenceEquals(lanDevicesWindow,window))lanDevicesWindow=null;};
        window.ShowDialog();
    }

    internal Window CreateLanDevicesWindow()
    {
        var window=LanWindow("Устройства в сети");
        var root=new DockPanel{Margin=new(18)};window.Content=root;
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var notice=Text("",12,true);notice.Name="LanDevicesNotice";notice.Margin=new(0,10,0,8);footer.Children.Add(notice);
        var close=Button("Готово",window.Close);close.IsCancel=true;close.Style=(Style)FindResource("QuietButton");close.HorizontalAlignment=HorizontalAlignment.Right;close.Margin=new(0);footer.Children.Add(close);
        var scroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        var heading=Text("Другой компьютер — та же Качалка",21);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        body.Children.Add(Text("Открой это окно на обоих компьютерах, нажми «Связать» и сравни код. После подтверждения можно отправлять загрузки без повторного запроса.",12,true));
        var pairing=Text("",12,true);pairing.Name="LanPairingAvailability";pairing.Margin=new(0,4,0,8);body.Children.Add(pairing);
        var tools=new WrapPanel{Margin=new(0,0,0,8)};body.Children.Add(tools);
        var refresh=ActionButton("Обновить список","IconRefresh",()=>{});refresh.Name="LanRefreshDevices";refresh.Margin=new(0,0,8,6);AutomationProperties.SetName(refresh,"Найти устройства в локальной сети");tools.Children.Add(refresh);
        var allow=Button("Разрешить связывание на 2 минуты",()=>{});allow.Name="LanAllowPairing";allow.Margin=new(0,0,0,6);AutomationProperties.SetName(allow,"Открыть двухминутное окно связывания новых устройств");tools.Children.Add(allow);
        var list=new StackPanel{Name="LanDevicesList"};body.Children.Add(list);
        var manualToggle=Button("Не видно компьютер? Добавить по адресу",()=>{});manualToggle.Name="LanManualToggle";manualToggle.Style=(Style)FindResource("QuietButton");manualToggle.HorizontalAlignment=HorizontalAlignment.Left;manualToggle.HorizontalContentAlignment=HorizontalAlignment.Left;manualToggle.Content=Text("Не видно компьютер? Добавить по адресу",12);manualToggle.Margin=new(0,8,0,8);body.Children.Add(manualToggle);
        var manual=new StackPanel{Visibility=Visibility.Collapsed};body.Children.Add(manual);
        manual.Children.Add(Text("Введи IPv4-адрес и порт, которые показывает окно «Устройства» на другом компьютере.",12,true));
        var address=new TextBox{Name="LanManualAddress",MaxLength=32,Margin=new(0,0,0,8),ToolTip="Например: 192.168.1.10:45832"};AutomationProperties.SetName(address,"Адрес другого компьютера: IPv4 и порт");manual.Children.Add(address);
        var add=Button("Найти по адресу",()=>{});add.Name="LanAddAddress";add.HorizontalAlignment=HorizontalAlignment.Left;add.Margin=new(0,0,0,8);AutomationProperties.SetName(add,"Найти компьютер по введённому адресу");manual.Children.Add(add);
        var self=Text("",12,true);self.Name="LanLocalAddresses";self.Margin=new(0,12,0,8);body.Children.Add(self);
        body.Children.Add(Text("Оба компьютера должны быть в одной частной сети Wi-Fi или Ethernet. Гостевой Wi-Fi может запрещать связь между устройствами. Разрешение брандмауэра действует только в частных сетях.",12,true));
        var firewall=Button("Разрешить в брандмауэре",()=>{});firewall.Name="LanAllowFirewall";firewall.HorizontalAlignment=HorizontalAlignment.Left;firewall.Margin=new(0);body.Children.Add(firewall);
        var service=lanService;var cancellation=new CancellationTokenSource();var token=cancellation.Token;
        bool finished=false,busy=false;int refreshQueued=0;var busyDevices=new HashSet<Guid>();
        void Feedback(string value,bool error=false){if(finished)return;notice.Text=value;notice.SetResourceReference(TextBlock.ForegroundProperty,error?"Danger":"Muted");}
        void AllowPairing(TimeSpan duration)
        {
            if(service?.IsRunning!=true)return;
            try{service.AllowPairingFor(duration);}
            catch(LanException error){Feedback(error.Message,true);}
        }
        void PairingStatus()
        {
            if(service==null||!service.IsRunning){pairing.Text="Сетевая функция выключена. Включи её в настройках на обоих компьютерах.";allow.IsEnabled=false;return;}
            var remaining=Math.Max(0,(int)Math.Ceiling((service.PairingExpiresUtc-DateTimeOffset.UtcNow).TotalSeconds));
            pairing.Text=service.IsPairingAllowed?$"Доступно связывание новых устройств · {remaining/60}:{remaining%60:00}":"Приём новых устройств закрыт. Связанные устройства продолжают работать.";
            allow.IsEnabled=!service.IsPairingAllowed;allow.Visibility=service.IsPairingAllowed?Visibility.Collapsed:Visibility.Visible;
        }
        async Task Pair(LanDevice device)
        {
            if(service==null||!busyDevices.Add(device.Id))return;
            RefreshList();Feedback("Ждём подтверждения на обоих компьютерах…");
            try{await service.PairAsync(device,token);Feedback("Устройства связаны. Теперь можно отправить загрузку на «"+device.Name+"». ");}
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){Feedback("Не удалось связать устройства: "+error.Message,true);}
            finally{busyDevices.Remove(device.Id);if(!finished)RefreshList();}
        }
        void RefreshList()
        {
            if(finished)return;
            PairingStatus();list.Children.Clear();
            var devices=service?.Devices.OrderByDescending(device=>device.Paired).ThenByDescending(LanRecentlySeen).ThenBy(device=>device.Name,StringComparer.CurrentCultureIgnoreCase).ToArray()??[];
            if(devices.Length==0){var empty=Text(service?.IsRunning==true?"Ищем компьютеры рядом… Открой «Устройства» во второй Качалке.":"Включи сетевую функцию в настройках для поиска устройств.",13,true);empty.Margin=new(0,14,0,14);list.Children.Add(empty);return;}
            bool? previousPaired=null;
            foreach(var device in devices)
            {
                if(previousPaired!=device.Paired){var label=Text(device.Paired?"Связанные устройства":"Найдены рядом",12,true);label.FontWeight=FontWeights.Medium;label.Margin=new(0,previousPaired==null?8:14,0,8);list.Children.Add(label);previousPaired=device.Paired;}
                var row=new StackPanel();var name=Text(device.Name,14);name.FontWeight=FontWeights.SemiBold;name.Margin=new(0,0,0,4);row.Children.Add(name);
                var recent=LanRecentlySeen(device);var detail=Text((recent?"В сети":"Давно не было связи")+" · "+LanDeviceAddress(device),11,true);detail.Margin=new(0,0,0,8);row.Children.Add(detail);
                var rowActions=new WrapPanel();row.Children.Add(rowActions);
                if(device.Paired)
                {
                    var forget=Button("Забыть устройство",()=>{});forget.Name="LanForgetDevice_"+device.Id.ToString("N");forget.Style=(Style)FindResource("QuietButton");forget.Padding=new(0,5,8,5);forget.Margin=new(0);forget.IsEnabled=!busyDevices.Contains(device.Id);AutomationProperties.SetName(forget,"Забыть связанное устройство "+device.Name);rowActions.Children.Add(forget);
                    forget.Click+=(_,_)=>ShowLanForgetConfirmation(window,device,async()=>{if(service==null||!busyDevices.Add(device.Id))return;RefreshList();try{await service.ForgetAsync(device.Id,token);Feedback("Устройство забыто. Для отправки загрузок потребуется снова подтвердить связь.");}catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception error){Feedback("Не удалось забыть устройство: "+error.Message,true);}finally{busyDevices.Remove(device.Id);if(!finished)RefreshList();}});
                }
                else
                {
                    var pair=Button(busyDevices.Contains(device.Id)?"Подтверждение…":"Связать",()=>{});pair.Name="LanPairDevice_"+device.Id.ToString("N");pair.Style=(Style)FindResource("PrimaryButton");pair.Padding=new(12,6,12,6);pair.Margin=new(0);pair.IsEnabled=recent&&!busyDevices.Contains(device.Id);AutomationProperties.SetName(pair,"Связать устройство "+device.Name);pair.Click+=async(_,_)=>await Pair(device);rowActions.Children.Add(pair);
                }
                var frame=new Border{Child=row,CornerRadius=new(10),Padding=new(12),BorderThickness=new(1),Margin=new(0,0,0,8)};frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");list.Children.Add(frame);
            }
        }
        void Changed(object? sender,EventArgs args)
        {
            if(finished||Interlocked.Exchange(ref refreshQueued,1)!=0)return;
            _=window.Dispatcher.BeginInvoke(()=>{Interlocked.Exchange(ref refreshQueued,0);if(!finished)RefreshList();},DispatcherPriority.Background);
        }
        async Task Discover()
        {
            if(service==null||busy||finished)return;busy=true;refresh.IsEnabled=false;Feedback("Ищем компьютеры в локальной сети…");
            try{await service.DiscoverAsync(token);if(!finished){RefreshList();Feedback("Список обновлён. Если компьютер не появился, добавь его по адресу.");}}
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){Feedback("Не удалось обновить список: "+error.Message,true);}
            finally{busy=false;if(!finished)refresh.IsEnabled=service.IsRunning;}
        }
        var timer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>PairingStatus();
        if(service!=null)service.Changed+=Changed;
        refresh.Click+=async(_,_)=>await Discover();
        allow.Click+=(_,_)=>{AllowPairing(TimeSpan.FromMinutes(2));PairingStatus();};
        manualToggle.Click+=(_,_)=>{var opening=manual.Visibility!=Visibility.Visible;manual.Visibility=opening?Visibility.Visible:Visibility.Collapsed;manualToggle.Content=Text(opening?"Скрыть ввод адреса":"Не видно компьютер? Добавить по адресу",12);if(opening)address.Focus();};
        add.Click+=async(_,_)=>
        {
            if(service==null||finished)return;add.IsEnabled=false;
            try{Feedback("Подключаемся по адресу…");var device=await service.FindManualAsync(address.Text.Trim(),token);if(finished)return;RefreshList();if(device.Paired)Feedback("Устройство «"+device.Name+"» уже связано.");else await Pair(device);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){Feedback("Не удалось найти компьютер: "+error.Message,true);}
            finally{if(!finished)add.IsEnabled=true;}
        };
        firewall.Click+=async(_,_)=>
        {
            firewall.IsEnabled=false;
            try{Feedback(await WindowsIntegration.AllowFirewallAsync()?"Качалка разрешена в частных сетях. Обнови список устройств.":"Запрос отменён. Настройки брандмауэра не изменены.");}
            catch(Exception error){Feedback("Не удалось изменить брандмауэр: "+error.Message,true);}
            finally{if(!finished)firewall.IsEnabled=true;}
        };
        window.Loaded+=async(_,_)=>
        {
            AllowPairing(TimeSpan.FromMinutes(2));RefreshList();timer.Start();
            var local=Task.Run(()=>LanLocalAddresses(service?.Port??45832),token);
            await Discover();
            try{var addresses=await local;if(!finished)self.Text=addresses.Length==0?"Адрес этого компьютера недоступен. Проверь подключение к частной сети.":"Этот компьютер: "+string.Join(" · ",addresses);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception){if(!finished)self.Text="Не удалось определить адрес этого компьютера.";}
        };
        window.Closed+=(_,_)=>{finished=true;timer.Stop();if(service!=null)service.Changed-=Changed;AllowPairing(TimeSpan.Zero);cancellation.Cancel();cancellation.Dispose();};
        refresh.IsEnabled=service?.IsRunning==true;add.IsEnabled=refresh.IsEnabled;RefreshList();return window;
    }

    static string[] LanLocalAddresses(int port)
    {
        static bool Private(IPAddress address)
        {
            if(address.AddressFamily!=AddressFamily.InterNetwork)return false;var bytes=address.GetAddressBytes();
            return bytes[0]==10||bytes[0]==172&&bytes[1] is >=16 and <=31||bytes[0]==192&&bytes[1]==168||bytes[0]==169&&bytes[1]==254;
        }
        return NetworkInterface.GetAllNetworkInterfaces().Where(network=>network.OperationalStatus==OperationalStatus.Up&&network.NetworkInterfaceType!=NetworkInterfaceType.Loopback).SelectMany(network=>network.GetIPProperties().UnicastAddresses).Select(info=>info.Address).Where(Private).Select(address=>$"{address}:{port}").Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    void ShowLanForgetConfirmation(Window owner,LanDevice device,Func<Task> forget)
    {
        var window=LanWindow("Забыть устройство",420,320);window.Owner=owner;
        var body=new StackPanel{Margin=new(18)};window.Content=body;
        var heading=Text("Забыть «"+device.Name+"»?",18);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        body.Children.Add(Text("Этот компьютер перестанет принимать от него загрузки. Уже добавленные задачи останутся в очереди. Для нового соединения потребуется снова сравнить код.",12,true));
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,12,0,0)};body.Children.Add(actions);
        var cancel=Button("Отмена",window.Close);cancel.IsCancel=true;actions.Children.Add(cancel);
        var confirm=Button("Забыть",()=>{window.Close();_=forget();});confirm.Name="LanForgetConfirm";confirm.IsDefault=true;AutomationProperties.SetName(confirm,"Подтвердить удаление связи с "+device.Name);actions.Children.Add(confirm);
        window.ShowDialog();
    }

    Task<bool> ConfirmLanPairingAsync(LanPairingRequest request,CancellationToken token)
    {
        if(token.IsCancellationRequested)return Task.FromResult(false);
        var result=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window=LanWindow("Подтвердить связь",440,410);
        if(lanDevicesWindow?.IsVisible==true)window.Owner=lanDevicesWindow;
        var body=new StackPanel{Margin=new(18)};window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var heading=Text(request.Incoming?"«"+request.Device.Name+"» хочет подключиться":"Связать с «"+request.Device.Name+"»?",20);heading.FontWeight=FontWeights.Bold;body.Children.Add(heading);
        body.Children.Add(Hint(request.Incoming?"Этот компьютер хочет отправлять тебе загрузки.":"Ты сможешь отправлять загрузки на этот компьютер."));
        body.Children.Add(Text("Сверь код: на другом компьютере должен быть такой же. Нажимай «Код совпадает» только если он одинаковый на обоих экранах.",13));
        // The full code stays available to assistive tools; the visible form is six cells of two characters.
        var code=Text(request.VerificationCode,27);code.Name="LanPairingCode";code.Visibility=Visibility.Collapsed;AutomationProperties.SetName(code,"Код проверки связи "+request.VerificationCode);body.Children.Add(code);
        var digits=new string(request.VerificationCode.Where(Uri.IsHexDigit).ToArray());
        var cells=new UniformGrid{Columns=digits.Length==12?6:1,Margin=new(0,6,0,12)};
        var chunks=digits.Length==12?Enumerable.Range(0,6).Select(index=>digits.Substring(index*2,2)).ToArray():[request.VerificationCode];
        foreach(var chunk in chunks)
        {
            var text=new TextBlock{Text=chunk,FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=24,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"Text");
            var cell=new Border{Child=text,CornerRadius=new(12),BorderThickness=new(1),MinHeight=60,Margin=new(3,0,3,0)};cell.SetResourceReference(Border.BackgroundProperty,"PanelAlt");cell.SetResourceReference(Border.BorderBrushProperty,"Edge");cells.Children.Add(cell);
        }
        var frame=new Border{Name="LanPairingCodeFrame",Child=cells,Margin=new(-3,0,-3,0)};body.Children.Add(frame);
        var notice=Text("",12,true);notice.Name="LanPairingRemaining";body.Children.Add(notice);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,10,0,0)};body.Children.Add(actions);
        var reject=Button("Отклонить",()=>{result.TrySetResult(false);window.Close();});reject.Name="LanPairingReject";reject.IsCancel=true;actions.Children.Add(reject);
        var accept=ActionButton("Код совпадает","IconCheck",()=>{result.TrySetResult(true);window.Close();},"PrimaryButton");accept.Name="LanPairingConfirm";accept.IsDefault=true;AutomationProperties.SetName(accept,"Подтвердить совпадение кода на обоих компьютерах");actions.Children.Add(accept);
        var timer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromSeconds(1)};
        void UpdateTime(){var seconds=Math.Max(0,(int)Math.Ceiling((request.ExpiresUtc-DateTimeOffset.UtcNow).TotalSeconds));notice.Text=$"Ожидаем подтверждение · {seconds/60}:{seconds%60:00}";if(seconds==0){result.TrySetResult(false);window.Close();}}
        timer.Tick+=(_,_)=>UpdateTime();
        var registration=token.Register(()=>{_=window.Dispatcher.BeginInvoke(()=>{result.TrySetResult(false);window.Close();});});
        lanPairingWindows.Add(window);
        window.Closed+=(_,_)=>{timer.Stop();registration.Dispose();lanPairingWindows.Remove(window);result.TrySetResult(false);};
        window.Loaded+=(_,_)=>{UpdateTime();if(window.IsVisible)timer.Start();};
        window.Show();return result.Task;
    }
}

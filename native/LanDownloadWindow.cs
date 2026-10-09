using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Kachalka.Lan;

namespace Kachalka;

public partial class MainWindow
{
    Window? lanDownloadWindow;

    async Task<LanDevice?> ChooseLanDownloadDeviceAsync(CancellationToken token)
    {
        if(token.IsCancellationRequested)return null;
        if(lanDownloadWindow?.IsVisible==true){lanDownloadWindow.Activate();return null;}
        var result=new TaskCompletionSource<LanDevice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window=CreateLanDownloadWindow(device=>result.TrySetResult(device));lanDownloadWindow=window;
        var registration=token.Register(()=>{_=window.Dispatcher.BeginInvoke(()=>{result.TrySetResult(null);window.Close();});});
        window.Closed+=(_,_)=>{registration.Dispose();if(ReferenceEquals(lanDownloadWindow,window))lanDownloadWindow=null;result.TrySetResult(null);};
        window.Show();return await result.Task;
    }

    internal Window CreateLanDownloadWindow(Action<LanDevice?> complete)
    {
        var window=LanWindow("Скачать на другом компьютере",510,510);
        var root=new DockPanel{Margin=new(18)};window.Content=root;
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var notice=Text("",12,true);notice.Name="LanDownloadNotice";notice.Margin=new(0,10,0,10);footer.Children.Add(notice);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
        var cancel=Button("Отмена",window.Close);cancel.IsCancel=true;cancel.Margin=new(0,0,8,0);actions.Children.Add(cancel);
        var confirm=Button("Отправить загрузку",()=>{});confirm.Name="LanDownloadConfirm";confirm.Style=(Style)FindResource("PrimaryButton");confirm.IsDefault=true;confirm.Margin=new(0);confirm.IsEnabled=false;AutomationProperties.SetName(confirm,"Отправить загрузку на выбранный компьютер");actions.Children.Add(confirm);
        var scroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        var heading=Text("Где скачать?",22);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        body.Children.Add(Text("Выбранный компьютер скачает файл в свою папку Ka4alka. Здесь новая загрузка не появится.",12,true));
        var tools=new WrapPanel{Margin=new(0,4,0,10)};body.Children.Add(tools);
        var refresh=ActionButton("Обновить","IconRefresh",()=>{});refresh.Name="LanDownloadRefresh";refresh.Margin=new(0,0,8,6);AutomationProperties.SetName(refresh,"Проверить доступность связанных компьютеров");tools.Children.Add(refresh);
        var manage=ActionButton("Связать устройство","IconPlus",ShowLanDevices);manage.Name="LanDownloadManageDevices";manage.Margin=new(0,0,0,6);tools.Children.Add(manage);
        var list=new StackPanel{Name="LanDownloadDevices"};body.Children.Add(list);
        var service=lanService;var cancellation=new CancellationTokenSource();var token=cancellation.Token;
        Guid? selectedId=null;LanDevice? selected=null;bool finished=false,busy=false,completed=false;int refreshQueued=0;
        void Complete(LanDevice? device){if(completed)return;completed=true;complete(device);}
        void Feedback(string message,bool error=false){if(finished)return;notice.Text=message;notice.SetResourceReference(TextBlock.ForegroundProperty,error?"Danger":"Muted");}
        void RefreshList()
        {
            if(finished)return;list.Children.Clear();
            var devices=service?.Devices.Where(device=>device.Paired).OrderByDescending(LanRecentlySeen).ThenBy(device=>device.Name,StringComparer.CurrentCultureIgnoreCase).ToArray()??[];
            selected=devices.FirstOrDefault(device=>device.Id==selectedId);confirm.IsEnabled=selected!=null&&service?.IsRunning==true;
            if(devices.Length==0)
            {
                var empty=Text(service?.IsRunning==true?"Пока нет связанных устройств. Открой «Устройства» на обоих компьютерах и сравни код.":"Включи «Загрузки на другом компьютере» в настройках на обоих устройствах.",13,true);empty.Margin=new(0,10,0,12);list.Children.Add(empty);return;
            }
            foreach(var device in devices)
            {
                var active=device.Id==selectedId;var recent=LanRecentlySeen(device);
                var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var details=new StackPanel();content.Children.Add(details);
                var name=Text(device.Name,14);name.FontWeight=FontWeights.SemiBold;name.Margin=new(0,0,0,4);details.Children.Add(name);
                var caption=Text((recent?"В сети":"Доступность не подтверждена")+" · "+LanDeviceAddress(device),11,true);caption.Margin=new(0);details.Children.Add(caption);
                var check=Text(active?"✓":"",18);check.SetResourceReference(TextBlock.ForegroundProperty,"Accent");check.Margin=new(12,0,0,0);check.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(check,1);content.Children.Add(check);
                var choice=Button("",()=>{selectedId=device.Id;RefreshList();Feedback(LanRecentlySeen(device)?"Файл скачает «"+device.Name+"». ":"Компьютер давно не был в сети. При отправке проверим подключение; если он выключен, задача не будет добавлена.");});
                choice.Name="LanDownloadDevice_"+device.Id.ToString("N");choice.Content=content;choice.HorizontalContentAlignment=HorizontalAlignment.Stretch;choice.Padding=new(12);choice.Margin=new(0,0,0,8);
                choice.SetResourceReference(Control.BackgroundProperty,active?"Selected":"Panel");choice.SetResourceReference(Control.BorderBrushProperty,active?"Accent":"EdgeSoft");
                AutomationProperties.SetName(choice,"Скачать на "+device.Name+(recent?", в сети":", доступность не подтверждена"));AutomationProperties.SetHelpText(choice,LanDeviceAddress(device));list.Children.Add(choice);
            }
        }
        void Changed(object? sender,EventArgs args)
        {
            if(finished||Interlocked.Exchange(ref refreshQueued,1)!=0)return;
            _=window.Dispatcher.BeginInvoke(()=>{Interlocked.Exchange(ref refreshQueued,0);if(!finished)RefreshList();},DispatcherPriority.Background);
        }
        async Task Discover()
        {
            if(service==null||busy||finished)return;busy=true;refresh.IsEnabled=false;Feedback("Проверяем устройства…");
            try{await service.DiscoverAsync(token);if(!finished){RefreshList();Feedback("Выбери компьютер, который скачает файл.");}}
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){Feedback("Не удалось проверить устройства: "+error.Message,true);}
            finally{busy=false;if(!finished)refresh.IsEnabled=service.IsRunning;}
        }
        if(service!=null)service.Changed+=Changed;
        confirm.Click+=(_,_)=>{if(selected==null||service?.IsRunning!=true)return;Complete(selected);window.Close();};
        refresh.Click+=async(_,_)=>await Discover();
        window.Loaded+=async(_,_)=>await Discover();
        window.Closed+=(_,_)=>{finished=true;if(service!=null)service.Changed-=Changed;cancellation.Cancel();cancellation.Dispose();Complete(null);};
        refresh.IsEnabled=service?.IsRunning==true;manage.IsEnabled=refresh.IsEnabled;RefreshList();return window;
    }

    void CloseLanDialogs()
    {
        foreach(var window in lanPairingWindows.ToArray())window.Close();
        lanDownloadWindow?.Close();lanDevicesWindow?.Close();
    }
}

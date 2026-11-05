using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Text.Json;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kachalka.Lan;

namespace Kachalka;

public partial class MainWindow
{
    PhoneRemoteServer? phoneRemote;
    PhoneRemoteCatalog? phoneCatalog;
    readonly SemaphoreSlim phoneGate=new(1,1);

    void RenderPhoneSettings(StackPanel panel)
    {
        panel.Children.Add(Hint("С телефона через браузер"));
        panel.Children.Add(Hint("Подключись к той же домашней сети Wi-Fi и отсканируй QR-код камерой телефона. Подключение произойдёт автоматически. Файлы скачает этот компьютер в выбранную папку."));
        var actions=new WrapPanel{Margin=new(0,8,0,0)};panel.Children.Add(actions);
        var start=Button("Подключить телефон",()=>{});start.Name="PhoneRemoteStart";start.Margin=new(0,0,8,6);actions.Children.Add(start);
        var stop=Button("Отключить все телефоны",()=>{});stop.Name="PhoneRemoteStop";stop.Margin=new(0,0,8,6);actions.Children.Add(stop);
        var info=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new(0),Background=System.Windows.Media.Brushes.Transparent,Margin=new(0,6,0,0)};
        AutomationProperties.SetName(info,AppLanguage.T("Адрес и код подключения телефона"));panel.Children.Add(info);
        var connect=new StackPanel{Margin=new(0,8,0,8),Visibility=Visibility.Collapsed};panel.Children.Add(connect);
        connect.Children.Add(Hint("Отсканируй камерой телефона. QR-код действует 2 минуты и только для одного подключения."));
        var qr=new Image{Width=216,Height=216,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,8)};
        RenderOptions.SetBitmapScalingMode(qr,BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetName(qr,AppLanguage.T("QR-код подключения телефона"));connect.Children.Add(qr);
        var addressBox=new ComboBox{MinWidth=180,MaxWidth=460,HorizontalAlignment=HorizontalAlignment.Left};
        AutomationProperties.SetName(addressBox,AppLanguage.T("Адрес для подключения"));connect.Children.Add(addressBox);
        var copy=Button("Копировать ссылку",()=>{if(addressBox.SelectedItem is string address)Clipboard.SetText(address);});copy.HorizontalAlignment=HorizontalAlignment.Left;copy.Margin=new(0,6,0,0);connect.Children.Add(copy);
        string? pairingCode=null,qrLink=null;
        void UpdateQr()
        {
            var link=pairingCode!=null&&addressBox.SelectedItem is string address?PhonePairingQr.Link(address,pairingCode):null;
            if(link==qrLink)return;
            qrLink=link;qr.Source=null;
            if(link==null)return;
            using var stream=new MemoryStream(PhonePairingQr.Png(link));
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();qr.Source=bitmap;
        }
        addressBox.SelectionChanged+=(_,_)=>UpdateQr();
        void Refresh()
        {
            stop.IsEnabled=phoneRemote!=null;
            if(phoneRemote==null){info.Text=AppLanguage.T("Доступ с телефона выключен.");connect.Visibility=Visibility.Collapsed;pairingCode=null;UpdateQr();return;}
            using var state=JsonDocument.Parse(JsonSerializer.Serialize(phoneRemote.State()));
            var root=state.RootElement;var code=root.GetProperty("code").GetString();
            var addresses=root.GetProperty("urls").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            pairingCode=code;
            if(addressBox.ItemsSource is not string[] current||!current.SequenceEqual(addresses))
            {
                var selected=addressBox.SelectedItem as string;addressBox.ItemsSource=addresses;
                addressBox.SelectedItem=selected!=null&&addresses.Contains(selected)?selected:addresses.FirstOrDefault();
            }
            connect.Visibility=addresses.Length>0&&code!=null?Visibility.Visible:Visibility.Collapsed;UpdateQr();
            info.Text=(addresses.Length>0?string.Join("\n",addresses):AppLanguage.T("Подключи компьютер к домашней сети Wi-Fi."))+"\n"+
                (code==null?AppLanguage.T("Для нового телефона получи новый код кнопкой выше."):AppLanguage.T("Код на 2 минуты: ")+code);
        }
        var refreshTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        refreshTimer.Tick+=(_,_)=>{if(phoneRemote!=null)Refresh();};
        info.Loaded+=(_,_)=>refreshTimer.Start();info.Unloaded+=(_,_)=>refreshTimer.Stop();
        start.Click+=async(_,_)=>
        {
            start.IsEnabled=false;
            try
            {
                await phoneGate.WaitAsync();
                try
                {
                    if(closing||closed)throw new OperationCanceledException();
                    if(phoneRemote==null)
                    {
                        var catalog=new PhoneRemoteCatalog(downloads,prefs);
                        var server=new PhoneRemoteServer((method,args,token)=>Dispatcher.InvokeAsync(async()=>
                        {
                            token.ThrowIfCancellationRequested();if(closing||closed)throw new OperationCanceledException(token);
                            var result=await catalog.Handle(method,args,token);
                            if(method is "add" or "control"){SyncTimer();if(section=="Загрузки")Render();}return result;
                        }).Task.Unwrap());
                        try{server.Start();phoneCatalog=catalog;phoneRemote=server;}
                        catch{await server.DisposeAsync();catalog.Dispose();throw;}
                    }
                    phoneRemote.OpenPairing();Refresh();
                }
                finally{phoneGate.Release();}
            }
            catch(Exception error){info.Text=AppLanguage.T("Не удалось включить доступ с телефона: ")+error.Message;}
            finally{start.IsEnabled=true;}
        };
        stop.Click+=async(_,_)=>{stop.IsEnabled=false;await StopPhoneRemoteAsync();Refresh();};
        Refresh();
    }
    async Task StopPhoneRemoteAsync()
    {
        await phoneGate.WaitAsync();
        try
        {
            var server=phoneRemote;phoneRemote=null;
            if(server!=null)await server.DisposeAsync();phoneCatalog?.Dispose();phoneCatalog=null;
        }
        finally{phoneGate.Release();}
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
namespace Kachalka;
public partial class MainWindow
{
    IReadOnlyList<Broadcast> broadcasts=[];
    string broadcastSection="",broadcastError="";
    bool broadcastLoading;
    CancellationTokenSource? broadcastRequest;
    void RenderBroadcast()
    {
        if(broadcastSection!=section){broadcastSection=section;_ = FetchBroadcast(section);}
        PageHeader.Children.Add(Text(section,25));PageHeader.Children.Add(Text(broadcastLoading?"Загружаем список каналов…":broadcastError.Length>0?broadcastError:section=="Радио"?"Радиостанции · Radio Browser":"Публичные трансляции · IPTV-org",12,true));
        var actions=new WrapPanel();actions.Children.Add(Button("Обновить",()=>{broadcastSection="";Render();}));PageHeader.Children.Add(actions);
        var filtered=broadcasts.Where(x=>x.Name.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).ToArray();
        var list=new ListBox{ItemsSource=filtered,ItemTemplate=(DataTemplate)FindResource("BroadcastRow")};Body.Children.Add(list);
        if(filtered.Length==0&&!broadcastLoading)PageHeader.Children.Add(Text("Каналы не найдены. Попробуй другой запрос или обнови список.",13,true));
    }
    async Task FetchBroadcast(string category)
    {
        broadcastRequest?.Cancel();broadcastRequest?.Dispose();broadcastRequest=new();var token=broadcastRequest.Token;broadcastLoading=true;broadcastError="";broadcasts=[];
        try{var catalog=new BroadcastCatalog(sourceClient);var result=category=="Радио"?await catalog.Radio(token):await catalog.Television(category=="Спорт",token);if(broadcastSection==category)broadcasts=result;}
        catch(OperationCanceledException){if(broadcastSection==category)broadcastError="Источник не ответил вовремя. Нажми «Обновить».";}
        catch(Exception){if(broadcastSection==category)broadcastError="Список каналов временно недоступен. Нажми «Обновить».";}
        finally{if(broadcastSection==category){broadcastLoading=false;if(!closed&&section==category)Render();}}
    }
    void OpenBroadcast(object sender,RoutedEventArgs e)
    {
        var item=(Broadcast)((Button)sender).Tag;
        try{
            // A small playlist lets the user's installed player handle HLS and codecs.
            var folder=Path.Combine(Preferences.DataDir,"streams");Directory.CreateDirectory(folder);
            var safe=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(item.Stream)));
            var path=Path.Combine(folder,safe+".m3u");File.WriteAllText(path,"#EXTM3U\n#EXTINF:-1,"+item.Name.Replace('\n',' ').Replace('\r',' ')+"\n"+item.Stream+"\n",Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});Status.Text="Открываем поток в установленном плеере: "+item.Name;
        }catch{Status.Text="Нет плеера для этого потока. Установи плеер с поддержкой M3U/HLS или открой сайт канала.";}
    }
    void OpenBroadcastSite(object sender,RoutedEventArgs e)
    {
        var item=(Broadcast)((Button)sender).Tag;if(!Uri.TryCreate(item.Website,UriKind.Absolute,out var url)||url.Scheme is not ("http" or "https"))return;
        try{Process.Start(new ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});}catch{Status.Text="Не удалось открыть сайт канала.";}
    }
}

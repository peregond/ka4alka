using System.Windows;
using System.Windows.Controls;
namespace Kachalka;
public partial class MainWindow
{
    IReadOnlyList<SourceEntry> archiveItems=[];
    string archiveKey="",archiveError="";
    int archivePage=1;
    bool archiveLoading;
    CancellationTokenSource? archiveRequest;
    void RenderArchive()
    {
        var key=section+"|"+Search.Text+"|"+archivePage;
        if(archiveKey!=key){archiveKey=key;_ = FetchArchive(key,section,Search.Text,archivePage);}
        PageHeader.Children.Add(Text(section,25));
        PageHeader.Children.Add(Text(archiveLoading?"Загружаем каталог…":archiveError.Length>0?archiveError:"Записи Internet Archive · торрент доступен, когда он опубликован для записи",12,true));
        var actions=new WrapPanel();actions.Children.Add(Button("Обновить",()=>{archiveKey="";Render();}));if(archivePage>1)actions.Children.Add(Button("← Назад",()=>{archivePage--;Render();}));if(archiveItems.Count==24)actions.Children.Add(Button("Далее →",()=>{archivePage++;Render();}));PageHeader.Children.Add(actions);
        Body.Children.Add(new ListBox{ItemsSource=archiveItems,ItemTemplate=(DataTemplate)FindResource("SourceRow")});
    }
    async Task FetchArchive(string key,string category,string query,int page)
    {
        archiveRequest?.Cancel();archiveRequest?.Dispose();archiveRequest=new();var token=archiveRequest.Token;archiveLoading=true;archiveError="";archiveItems=[];
        try{var items=await sourceClient.SearchArchive(query,category,page,token);if(archiveKey==key){archiveItems=items;if(items.Count==0)archiveError="Ничего не найдено. Измени поиск.";}}
        catch(OperationCanceledException){if(archiveKey==key)archiveError="Источник не ответил вовремя. Нажми «Обновить».";}
        catch(Exception){if(archiveKey==key)archiveError="Источник временно недоступен. Нажми «Обновить».";}
        finally{if(archiveKey==key){archiveLoading=false;if(!closed&&section==category)Render();}}
    }
}

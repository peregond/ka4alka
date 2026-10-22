using System.Windows;
using System.Windows.Controls;
namespace Kachalka;
public partial class MainWindow
{
    string detailFranchiseKey="";bool detailFranchiseExpanded;
    StackPanel? detailFranchiseHost;
    Task<MovieFranchise?>? detailFranchiseTask;
    CancellationTokenSource? detailFranchiseRequest;
    void ResetDetailFranchiseIfLeaving()
    {
        if(activePerson==null&&current?.Section=="Фильмы"&&detailFranchiseKey==current.Id+"|"+prefs.UiLanguage)return;
        detailFranchiseRequest?.Cancel();detailFranchiseRequest?.Dispose();detailFranchiseRequest=null;detailFranchiseTask=null;detailFranchiseHost=null;detailFranchiseKey="";detailFranchiseExpanded=false;
    }
    void RenderDetailFranchise(StackPanel page,MediaItem item)
    {
        var key=item.Id+"|"+prefs.UiLanguage;
        if(key!=detailFranchiseKey){detailFranchiseRequest?.Cancel();detailFranchiseRequest?.Dispose();detailFranchiseRequest=null;detailFranchiseTask=null;detailFranchiseExpanded=false;detailFranchiseKey=key;}
        detailFranchiseHost=new StackPanel{Name="DetailFranchise",Visibility=detailFranchiseExpanded?Visibility.Visible:Visibility.Collapsed};
        page.Children.Insert(Math.Min(1,page.Children.Count),detailFranchiseHost);
        if(detailFranchiseExpanded)_=LoadDetailFranchise(item,key);
    }
    void ToggleDetailFranchise(MediaItem item)
    {
        if(detailFranchiseHost==null)return;
        detailFranchiseExpanded=!detailFranchiseExpanded;detailFranchiseHost.Visibility=detailFranchiseExpanded?Visibility.Visible:Visibility.Collapsed;
        if(detailFranchiseExpanded)_=LoadDetailFranchise(item,detailFranchiseKey);
    }
    async Task LoadDetailFranchise(MediaItem item,string key)
    {
        if(detailFranchiseHost==null||key!=detailFranchiseKey)return;
        var host=detailFranchiseHost;host.Children.Clear();
        var gallery=PersonGallery("Другие части","Фильмы в порядке выхода.","FranchiseFilms");host.Children.Add(gallery.Frame);gallery.Loading(true);
        try
        {
            detailFranchiseRequest??=CancellationTokenSource.CreateLinkedTokenSource(metadataLifetime.Token);
            detailFranchiseTask??=MovieFranchiseCatalog.Load(item,onlineIndex,prefs.UiLanguage,detailFranchiseRequest.Token);
            var franchise=await detailFranchiseTask;
            if(key!=detailFranchiseKey||current?.Id!=item.Id||!ReferenceEquals(host,detailFranchiseHost))return;
            gallery.Loading(false);
            if(franchise!=null)gallery.SetTitle(prefs.UiLanguage=="en"?franchise.EnglishName??franchise.Name:franchise.Name);
            var parts=MovieFranchiseCatalog.OtherParts(franchise,item,prefs.UiLanguage).Where(PersonallyVisible).ToArray();
            gallery.Update(parts);
            if(parts.Length==0){host.Children.Clear();host.Children.Add(Text("Другие части пока не найдены.",14,true));}
        }
        catch(OperationCanceledException){}
        catch
        {
            if(key!=detailFranchiseKey||!ReferenceEquals(host,detailFranchiseHost))return;
            detailFranchiseTask=null;host.Children.Clear();host.Children.Add(Text("Не удалось загрузить другие части.",14,true));
            var retry=ActionButton("Повторить загрузку","IconRefresh",()=>{_=LoadDetailFranchise(item,key);});retry.HorizontalAlignment=HorizontalAlignment.Left;host.Children.Add(retry);
        }
    }
}

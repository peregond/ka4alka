using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kachalka;
public partial class MainWindow
{
    string submittedQuery="",searchCategory="";
    bool SearchActive=>submittedQuery.Length>0;
    internal Func<string,string,CancellationToken,Task<IReadOnlyList<MediaItem>>>? searchProvider;
    void SubmitSearch(object sender,RoutedEventArgs e)
    {
        searchDelay.Stop();submittedQuery=Search.Text.Trim();searchCategory="";current=null;favoritesOnly=false;livePage=1;ResetCatalogFilters();liveKey="";Render();
    }
    void SearchKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){SubmitSearch(sender,e);e.Handled=true;}}
    void SearchTabs(Panel panel)
    {
        foreach(var choice in new[]{("","Все"),("Фильмы","Фильмы"),("Сериалы","Сериалы")})
        {
            var count=UnifiedSearch.Filter(liveItems,choice.Item1).Length;
            var button=Button(choice.Item2+(liveLoading?"":" · "+count),()=>{searchCategory=choice.Item1;livePage=1;catalogLastPage=null;Render();});
            button.Style=(Style)FindResource("PillButton");button.SetResourceReference(Control.BackgroundProperty,searchCategory==choice.Item1?"Selected":"Panel");
            System.Windows.Automation.AutomationProperties.SetName(button,"Результаты: "+choice.Item2);panel.Children.Add(button);
        }
    }
    void RefreshLoadingIndicator()
    {
        var details=current!=null&&releaseViews.TryGetValue(current.Id,out var view)&&view.Checking;
        var loading=current!=null?details:section is "Фильмы" or "Сериалы"?liveLoading&&!favoritesOnly:section=="Источники"?searching:section is "Музыка" or "Игры" or "Программы"?archiveLoading:section is "ТВ-каналы" or "Радио" or "Спорт"?broadcastLoading:false;
        LoadingIndicator.Visibility=loading?Visibility.Visible:Visibility.Collapsed;
        LoadingLabel.Text=details?"Ищем раздачи…":SearchActive?"Ищем фильмы и сериалы…":"Загружаем страницу…";
    }
}

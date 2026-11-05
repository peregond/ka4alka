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
        if(OpenShareLink(Search.Text))return;
        featuredReturn=null;ResetCatalogNavigationPosition();
        searchDelay.Stop();
        if(section is not ("Фильмы" or "Сериалы" or "ТВ Шоу"))section=current?.Section is "Фильмы" or "Сериалы"?current.Section:lastCatalogSection;
        CancelPeopleSearch(true);personSearchReturn=null;activePerson=null;returnPerson=null;
        lastCatalogSection=section;submittedQuery=Search.Text.Trim();searchCategory="";current=null;favoritesOnly=false;livePage=1;ResetCatalogFilters();liveKey="";Render();
    }
    void SearchKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){SubmitSearch(sender,e);e.Handled=true;}}
    void SearchTabs(Panel panel)
    {
        foreach(var choice in new[]{("","Все"),("Фильмы","Фильмы"),("Сериалы","Сериалы"),("Игры","Игры"),("Люди","Люди")})
        {
            var count=choice.Item1=="Люди"?peopleSearchItems.Length:UnifiedSearch.Filter(liveItems,choice.Item1).Length+(choice.Item1==""?peopleSearchItems.Length:0);
            var loading=choice.Item1=="Люди"?PeopleSearchLoading:choice.Item1==""?liveLoading||PeopleSearchLoading:liveLoading;
            var button=Button(choice.Item2+(loading?"":" · "+count),()=>{searchCategory=choice.Item1;livePage=1;catalogLastPage=null;Render();});
            button.Style=(Style)FindResource("PillButton");button.SetResourceReference(Control.BackgroundProperty,searchCategory==choice.Item1?"Selected":"Panel");
            System.Windows.Automation.AutomationProperties.SetName(button,AppLanguage.T("Результаты: "+choice.Item2));panel.Children.Add(button);
        }
    }
    void RefreshLoadingIndicator()
    {
        RefreshReleaseLoadingIndicator();
        // Release searches have their own indicator beside the release results.
        var details=current?.IsLive==true&&DetailMetadataLoading(current.Id)&&!MediaMetadata.HasDescription(current);
        // A film page shows breathing placeholders in place of its synopsis and releases instead of this bar.
        var loading=activePerson!=null||current!=null?false:section is "Фильмы" or "Сериалы" or "ТВ Шоу"?liveLoading&&!favoritesOnly:section=="Источники"?searching:section=="Игры"?gameLoading||gameShelfLoading.Count>0:section is "Музыка" or "Программы"?archiveLoading:section is "ТВ-каналы" or "Радио" or "Спорт"?broadcastLoading:false;
        LoadingIndicator.Visibility=loading?Visibility.Visible:Visibility.Collapsed;
        LoadingLabel.Text=AppLanguage.T(details?"Загружаем информацию…":SearchActive&&(section is "Фильмы" or "Сериалы" or "ТВ Шоу")?"Ищем фильмы, сериалы и игры…":"Загружаем страницу…");
    }
}

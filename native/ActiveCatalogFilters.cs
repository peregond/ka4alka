using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Kachalka;

public partial class MainWindow
{
    sealed record ActiveCatalogFilter(string Key,string Label,Action Clear);
    ScrollViewer? activeCatalogFilterScroll;
    ActiveCatalogFilter[] ActiveCatalogFilters()
    {
        var filters=new List<ActiveCatalogFilter>();
        string Label(CatalogChoice[] choices,string key)=>choices.FirstOrDefault(choice=>choice.Key==key)?.Label??key;
        if(catalogGenre.Length>0)filters.Add(new("genre","Жанр · "+Label(catalogGenres,catalogGenre),()=>catalogGenre=""));
        if(catalogCountry.Length>0)filters.Add(new("country","Страна · "+Label(catalogCountries,catalogCountry),()=>catalogCountry=""));
        if(catalogYear is {} year)filters.Add(new("year","Год · "+year,()=>catalogYear=null));
        if(catalogRating>0)filters.Add(new("rating","Рейтинг · от "+catalogRating,()=>catalogRating=0));
        if(catalogRegion.Length>0)filters.Add(new("region",catalogRegion=="native"?"Отечественные":"Иностранные",()=>catalogRegion=""));
        else if(catalogCollection!="all")filters.Add(new("collection",catalogCollection=="popular"?"Популярное":"Высокий рейтинг",()=>catalogCollection="all"));
        if(catalogOrder!="Сначала новые")filters.Add(new("order",catalogOrder,()=>catalogOrder="Сначала новые"));
        if(prefs.CatalogQualityHeight>0)filters.Add(new("quality",prefs.CatalogQualityHeight switch{2160=>"Качество · 4K",1080=>"Качество · Full HD",_=>"Качество · HD Ready"},()=>{prefs.CatalogQualityHeight=0;prefs.Save();}));
        return filters.ToArray();
    }
    void ResetAllCatalogFilters()=>ChangeCatalogFilter(()=>{favoritesOnly=false;searchCategory="";ResetCatalogFilters();prefs.CatalogQualityHeight=0;prefs.Save();});
    Button AddCatalogResetButton()
    {
        var reset=ActionButton("Сбросить всё","IconReset",ResetAllCatalogFilters,"PillButton");
        reset.Name="ResetActiveCatalogFilters";reset.ToolTip=AppLanguage.T("Сбросить фильтры, подборку и сортировку. Поиск и скрытие плохого качества сохранятся.");
        AutomationProperties.SetName(reset,AppLanguage.T("Сбросить выбранные фильтры"));return reset;
    }
    void RenderActiveCatalogFilters()
    {
        activeCatalogFilterScroll=null;
        if(PeopleOnlySearch)return;
        var filters=ActiveCatalogFilters();if(filters.Length==0)return;
        var row=new WrapPanel{Name="ActiveCatalogFilters"};
        AutomationProperties.SetName(row,AppLanguage.T("Выбранные фильтры"));
        foreach(var filter in filters)
        {
            var button=Button(filter.Label+" ×",()=>ChangeCatalogFilter(filter.Clear));
            button.Name="ActiveFilter_"+filter.Key;button.Style=(Style)FindResource("PillButton");
            var label=new Grid();label.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});label.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var caption=new TextBlock{Text=filter.Label,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};label.Children.Add(caption);
            var close=new TextBlock{Text="×",Margin=new(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(close,1);label.Children.Add(close);button.Content=label;button.MaxWidth=190;
            button.SetResourceReference(Control.BackgroundProperty,"AccentSoft");
            button.Margin=new(0,0,6,4);button.Padding=new(10,5,10,5);button.MinHeight=28;button.FontSize=11;
            button.ToolTip=AppLanguage.T("Снять фильтр: "+filter.Label);
            AutomationProperties.SetName(button,AppLanguage.T("Снять фильтр: "+filter.Label));row.Children.Add(button);
        }
        activeCatalogFilterScroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=row,Margin=new(0,7,0,0),MaxHeight=compactHeight?68:double.PositiveInfinity,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        FilterControls.Children.Add(activeCatalogFilterScroll);
    }
}

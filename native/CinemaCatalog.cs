using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    DockPanel? catalogToolbar;
    Button? catalogRefreshButton;
    StackPanel? catalogRailPreview;
    Grid? discoveryHero;
    ContentControl? discoveryShelf;
    readonly Dictionary<DiscoveryShelfKind,DiscoveryShelfView> discoveryShelves=[];
    readonly HashSet<string> featuredRequests=[];
    readonly HashSet<string> featuredFallback=[];
    CancellationTokenSource? featuredRequest;
    string? featuredCategory;
    int featuredGeneration;
    bool DiscoveryCatalog=>section is ("Фильмы" or "Сериалы")&&!SearchActive&&!favoritesOnly&&activePerson==null&&current==null&&livePage==1&&CatalogSelection.IsDefault&&catalogRegion.Length==0;

    void AddDiscovery(StackPanel content,MediaItem[] cards,UIElement? filterRow=null)
    {
        discoveryShelves.Clear();
        if(!DiscoveryCatalog)return;
        var popular=DiscoveryPopularItems(section);
        var banners=popular.Length>0?popular:cards;
        if(banners.Length>0)
        {
            discoveryHero=new Grid{Margin=new(0,0,0,28)};
            discoveryHero.Children.Add(BuildFeatureCarousel(banners));
            content.Children.Add(discoveryHero);
        }
        else if(liveLoading||featuredRequests.Contains(section))content.Children.Add(BannerSkeleton());
        if(filterRow!=null)content.Children.Add(filterRow);
        var regions=DiscoveryRegionalItems(section,prefs.HomeCountry,cards);
        var order=section=="Фильмы"?new[]{DiscoveryShelfKind.Popular,DiscoveryShelfKind.New,DiscoveryShelfKind.Foreign,DiscoveryShelfKind.Native}:new[]{DiscoveryShelfKind.Popular,DiscoveryShelfKind.Foreign,DiscoveryShelfKind.New,DiscoveryShelfKind.Native};
        foreach(var kind in order)
        {
            var view=AddDiscoveryShelf(content,kind);discoveryShelves[kind]=view;
            switch(kind)
            {
                case DiscoveryShelfKind.Popular:view.Set(popular,featuredRequests.Contains(section)&&!featuredFallback.Contains(section));discoveryShelf=view.Row;break;
                case DiscoveryShelfKind.New:view.Set(cards,liveLoading);break;
                case DiscoveryShelfKind.Foreign:view.Set(regions.Foreign,regions.Loading);break;
                case DiscoveryShelfKind.Native:view.Set(regions.Native,regions.Loading);break;
            }
        }
        var all=Text(section=="Фильмы"?"Все фильмы":"Все сериалы",22);all.Name="CatalogAllHeading";all.FontWeight=FontWeights.Bold;all.Margin=new(0,4,0,16);content.Children.Add(all);
        UpdateDiscoveryLayout();
        StartRegionalDiscovery(section,cards);
        if(!catalogPages.ContainsKey(section+"||1")&&!featuredRequests.Contains(section)&&!featuredFallback.Contains(section)){featuredRequests.Add(section);_=LoadFeatured(section);}
    }

    static double CatalogScore(MediaItem item)
    {
        var kp=CatalogPaging.Rating(item);
        return kp>=0?kp:double.TryParse(item.Imdb.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var imdb)?imdb:-1;
    }
    MediaItem[] DiscoveryPopularItems(string category)=>catalogPages.TryGetValue(category+"||1",out var page)&&!featuredFallback.Contains(category)?page.Items.Where(item=>!NoDownloads(item)&&CatalogQualityMatches(item)).DistinctBy(item=>item.Id).Take(40).ToArray():[];
    void SyncDiscoveryContext()
    {
        SyncRegionalDiscoveryContext();
        if(!DiscoveryCatalog||featuredCategory!=null&&featuredCategory!=section)
        {
            featuredGeneration++;featuredRequest?.Cancel();featuredRequest?.Dispose();featuredRequest=null;
            if(featuredCategory!=null)featuredRequests.Remove(featuredCategory);
            featuredCategory=null;
        }
        discoveryShelves.Clear();
    }
    void ResetDiscoveryData()
    {
        ResetRegionalDiscovery();
        featuredGeneration++;featuredRequest?.Cancel();featuredRequest?.Dispose();featuredRequest=null;featuredCategory=null;
        featuredRequests.Clear();featuredFallback.Clear();discoveryRegions.Clear();discoveryShelves.Clear();
    }
    async Task LoadFeatured(string category)
    {
        featuredRequest?.Cancel();featuredRequest?.Dispose();var request=featuredRequest=new CancellationTokenSource(TimeSpan.FromSeconds(12));featuredCategory=category;var generation=++featuredGeneration;
        if(discoveryShelves.TryGetValue(DiscoveryShelfKind.Popular,out var pending))pending.Set(DiscoveryPopularItems(category),true);
        try
        {
            var page=await new LiveCatalog(sourceClient).BrowsePage(category,1,new(Collection:"popular"),request.Token);
            if(request.IsCancellationRequested||generation!=featuredGeneration||closed)return;
            if(page.Items.Length>0){catalogPages[category+"||1"]=page;featuredFallback.Remove(category);}
            else featuredFallback.Add(category);
            if(DiscoveryCatalog&&section==category)StartDiscoveryQualityCheck();
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested&&generation!=featuredGeneration){return;}
        catch{if(generation==featuredGeneration&&!closed)featuredFallback.Add(category);}
        finally
        {
            if(generation==featuredGeneration)
            {
                featuredRequest=null;featuredCategory=null;featuredRequests.Remove(category);
                if(!closed&&DiscoveryCatalog&&section==category&&discoveryShelves.TryGetValue(DiscoveryShelfKind.Popular,out var shelf))shelf.Set(DiscoveryPopularItems(category),false);
            }
            request.Dispose();
        }
    }
    void UpdateDiscoveryLayout()
    {
        if(discoveryHero!=null)ArrangeFeatureCarousel(Body.ActualWidth);
        foreach(var shelf in discoveryShelves.Values)shelf.Resize(Math.Max(1,catalogColumns));
    }
    void AddRailPreview(MediaItem[] cards)
    {
        catalogRailPreview=new StackPanel{Margin=new(0,14,6,0)};
        var divider=new Border{Height=1,Margin=new(0,0,0,16)};divider.SetResourceReference(Border.BackgroundProperty,"EdgeSoft");catalogRailPreview.Children.Add(divider);
        var heading=Text("Новые в библиотеке",13);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,0,0,14);catalogRailPreview.Children.Add(heading);
        foreach(var item in cards.Take(3))
        {
            var button=Button("",()=>{current=item;Render();});button.Style=(Style)FindResource("PosterButton");button.Padding=new(4);button.Margin=new(0,0,0,10);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;System.Windows.Automation.AutomationProperties.SetName(button,"Открыть "+item.Title);
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(42)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});button.Content=row;
            var poster=new Border{Width=42,Height=63,CornerRadius=new(6),ClipToBounds=true,Background=item.Cover};poster.SizeChanged+=(_,_)=>ClipPoster(poster);var image=new Image{DataContext=item,Stretch=Stretch.UniformToFill};image.Loaded+=SourceCover;poster.Child=image;row.Children.Add(poster);
            var info=new StackPanel{Margin=new(10,5,0,0)};Grid.SetColumn(info,1);row.Children.Add(info);
            var title=Text(item.Title,12);title.MaxHeight=34;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new(0,0,0,6);info.Children.Add(title);info.Children.Add(Text(item.Year>0?item.Year.ToString():"",11,true));catalogRailPreview.Children.Add(button);
        }
    }
}

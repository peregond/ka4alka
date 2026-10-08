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

    void AddDiscovery(StackPanel content,MediaItem[] cards)
    {
        discoveryShelves.Clear();
        if(!DiscoveryCatalog)return;
        var popular=DiscoveryPopularItems(section);
        var banners=popular.Length>0?popular:cards;
        if(banners.Length>0)
        {
            discoveryHero=new Grid{Margin=new(0,0,8,20)};
            discoveryHero.ColumnDefinitions.Add(new(){Width=new GridLength(1.8,GridUnitType.Star)});
            discoveryHero.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            discoveryHero.Children.Add(FeatureBanner(banners[0],true));
            if(banners.Length>1){var second=FeatureBanner(banners[1],false);Grid.SetColumn(second,1);second.Margin=new(12,0,0,0);discoveryHero.Children.Add(second);}
            content.Children.Add(discoveryHero);
        }
        var regions=DiscoveryRegionalItems(section,prefs.HomeCountry,cards);
        var order=section=="Фильмы"?new[]{DiscoveryShelfKind.Popular,DiscoveryShelfKind.New,DiscoveryShelfKind.Foreign,DiscoveryShelfKind.Native}:new[]{DiscoveryShelfKind.Popular,DiscoveryShelfKind.Foreign,DiscoveryShelfKind.New,DiscoveryShelfKind.Native};
        foreach(var kind in order)
        {
            var view=AddDiscoveryShelf(content,kind);discoveryShelves[kind]=view;
            switch(kind)
            {
                case DiscoveryShelfKind.Popular:view.Set(popular,featuredRequests.Contains(section)&&!featuredFallback.Contains(section));discoveryShelf=view.Row;break;
                case DiscoveryShelfKind.New:view.Set(cards,false);break;
                case DiscoveryShelfKind.Foreign:view.Set(regions.Foreign,regions.Loading);break;
                case DiscoveryShelfKind.Native:view.Set(regions.Native,regions.Loading);break;
            }
        }
        var all=Text(section=="Фильмы"?"Все фильмы":"Все сериалы",24);all.Name="CatalogAllHeading";all.FontWeight=FontWeights.SemiBold;all.Margin=new(0,8,8,12);content.Children.Add(all);
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
    Button FeatureBanner(MediaItem item,bool primary)
    {
        var frame=new Border{Name="FeatureFrame",CornerRadius=new(16),BorderThickness=new(1)};
        frame.SetResourceReference(Border.BackgroundProperty,"PanelAlt");frame.SetResourceReference(Border.BorderBrushProperty,"Edge");
        var grid=new Grid{Name="FeatureArtwork"};frame.Child=grid;
        // Clip the artwork to the inside of the border, keeping its rounded stroke intact.
        grid.SizeChanged+=(_,e)=>
        {
            if(e.NewSize.Width<=0||e.NewSize.Height<=0)return;
            var clip=new RectangleGeometry(new Rect(e.NewSize),15,15);clip.Freeze();grid.Clip=clip;
        };
        var image=new Image{DataContext=item,Width=0,Height=0,Opacity=0,Tag="FeaturePoster"};image.Loaded+=SourceCover;image.DataContextChanged+=SourceCoverChanged;image.Loaded+=async(_,_)=>await ImproveFeaturePoster(image,item);grid.Children.Add(image);
        var picture=new ImageBrush{Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Top};
        BindingOperations.SetBinding(picture,ImageBrush.ImageSourceProperty,new Binding("Source"){Source=image});
        var backdrop=new Border{Background=picture};grid.Children.Add(backdrop);
        grid.Children.Add(new Border{Background=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(230,7,12,29),0),new(Color.FromArgb(75,7,12,29),.6),new(Color.FromArgb(20,7,12,29),1)},0)});
        grid.Children.Add(new Border{Background=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(0,7,12,29),0),new(Color.FromArgb(230,7,12,29),1)},90)});
        var content=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(20,18,20,18)};grid.Children.Add(content);
        var eyebrow=new TextBlock{Text=primary?"В ЦЕНТРЕ ВНИМАНИЯ":"СТОИТ ПОСМОТРЕТЬ",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(185,200,255)),Margin=new(0,0,0,8)};content.Children.Add(eyebrow);
        var title=new TextBlock{Text=item.Title,FontSize=primary?25:22,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=65,ToolTip=item.Title,Margin=new(0,0,0,6)};content.Children.Add(title);
        content.Children.Add(new TextBlock{Text=string.Join(" · ",new[]{item.Year>0?item.Year.ToString():"",item.CardGenre}.Where(x=>x.Length>0)),Foreground=new SolidColorBrush(Color.FromRgb(202,212,237)),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,0,12)});
        var action=new Border{CornerRadius=new(9),Padding=new(12,9,12,9),HorizontalAlignment=HorizontalAlignment.Left,BorderThickness=new(1)};
        action.SetResourceReference(Border.BackgroundProperty,primary?"PrimaryFill":"AccentSoft");action.SetResourceReference(Border.BorderBrushProperty,primary?"Primary":"Edge");
        var actionLabel=IconLabel("Подробнее","IconChevron");actionLabel.IsHitTestVisible=false;
        foreach(var label in VisualElements<TextBlock>(actionLabel))label.Foreground=Brushes.White;
        foreach(var glyph in VisualElements<System.Windows.Shapes.Path>(actionLabel))glyph.Stroke=Brushes.White;
        action.Child=actionLabel;content.Children.Add(action);
        var button=new Button{Content=frame,Tag=item,Style=(Style)FindResource("FeatureBannerButton"),Padding=new(0),Margin=new(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ToolTip="Открыть «"+item.Title+"»"};
        System.Windows.Automation.AutomationProperties.SetName(button,"Открыть "+item.Title);
        button.Click+=OpenCard;return button;
    }
    void UpdateDiscoveryLayout()
    {
        if(discoveryHero!=null)
        {
            var wide=Body.ActualWidth>=760;
            discoveryHero.Height=Body.ActualWidth>=1050?260:wide?235:205;
            discoveryHero.ColumnDefinitions[1].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);
            if(discoveryHero.Children.Count>1)discoveryHero.Children[1].Visibility=wide?Visibility.Visible:Visibility.Collapsed;
        }
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

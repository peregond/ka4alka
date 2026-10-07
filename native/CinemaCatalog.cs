using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    DockPanel? catalogToolbar;
    StackPanel? catalogRailPreview;
    Grid? discoveryHero;
    ContentControl? discoveryShelf;
    int discoveryShelfColumns;
    MediaItem[] discoveryShelfItems=[];
    readonly HashSet<string> featuredRequests=[];
    readonly HashSet<string> featuredFallback=[];
    bool DiscoveryCatalog=>!SearchActive&&!favoritesOnly&&livePage==1&&CatalogSelection.IsDefault;

    void AddDiscovery(StackPanel content,MediaItem[] cards)
    {
        if(cards.Length==0||!DiscoveryCatalog)return;
        var featureKey=section+"||1";
        var popular=catalogPages.TryGetValue(featureKey,out var page)&&page.Items.Length>0&&!featuredFallback.Contains(section);
        var picks=(popular?page!.Items.AsEnumerable():cards.OrderByDescending(CatalogScore)).Where(x=>!NoDownloads(x)).ToArray();
        foreach(var item in picks)ApplyKnownQuality(item);
        if(prefs.HidePoorQuality)picks=picks.Where(x=>!x.OnlyPoorQuality).ToArray();
        if(picks.Length==0)picks=cards;
        discoveryHero=new Grid{Margin=new(0,0,8,20)};
        discoveryHero.ColumnDefinitions.Add(new(){Width=new GridLength(1.8,GridUnitType.Star)});
        discoveryHero.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        discoveryHero.Children.Add(FeatureBanner(picks[0],true));
        if(picks.Length>1){var second=FeatureBanner(picks[1],false);Grid.SetColumn(second,1);second.Margin=new(12,0,0,0);discoveryHero.Children.Add(second);}
        content.Children.Add(discoveryHero);
        AddShelfHeading(content,popular?"Популярное":"Рекомендуем",()=>ChangeCatalogFilter(()=>catalogCollection="popular"));
        discoveryShelfItems=picks.Take(8).ToArray();
        discoveryShelf=new ContentControl{ContentTemplate=(DataTemplate)FindResource("MediaRow"),Margin=new(0,0,0,8)};
        discoveryShelfColumns=0;
        content.Children.Add(discoveryShelf);
        AddShelfHeading(content,section=="Фильмы"?"Новые фильмы":"Новые сериалы",()=>{ResetCatalogFilters();GoCatalogPage(2);});
        UpdateDiscoveryLayout();
        if(!popular&&!featuredRequests.Contains(section)){featuredRequests.Add(section);_ = LoadFeatured(section);}
    }
    static double CatalogScore(MediaItem item)
    {
        var kp=CatalogPaging.Rating(item);
        return kp>=0?kp:double.TryParse(item.Imdb.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var imdb)?imdb:-1;
    }
    async Task LoadFeatured(string category)
    {
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var page=await new LiveCatalog(sourceClient).BrowsePage(category,1,new(Collection:"popular"),timeout.Token);
            if(closed)return;
            if(page.Items.Length>0){catalogPages[category+"||1"]=page;featuredFallback.Remove(category);}
            else featuredFallback.Add(category);
        }
        catch{if(!closed)featuredFallback.Add(category);}
        if(!closed&&section==category&&current==null&&DiscoveryCatalog&&catalogPages.ContainsKey(category+"||1")&&
           !VisualElements<Button>(RootGrid).Any(x=>x.ContextMenu?.IsOpen==true))RenderCatalogKeepingPosition();
    }
    void AddShelfHeading(Panel target,string label,Action all)
    {
        var row=new DockPanel{Margin=new(0,0,8,10)};
        var more=ActionButton("Все","IconChevron",all);more.FontSize=11;more.Padding=new(5);more.Margin=new(0);more.MinHeight=26;more.SetResourceReference(Control.ForegroundProperty,"Accent");DockPanel.SetDock(more,Dock.Right);row.Children.Add(more);
        var heading=Text(label,22);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0);row.Children.Add(heading);target.Children.Add(row);
    }
    Border FeatureBanner(MediaItem item,bool primary)
    {
        var frame=new Border{CornerRadius=new(16),BorderThickness=new(1),ClipToBounds=true};
        frame.SetResourceReference(Border.BorderBrushProperty,"Edge");frame.SizeChanged+=(_,_)=>ClipPoster(frame);
        var grid=new Grid();frame.Child=grid;
        var image=new Image{DataContext=item,Width=0,Height=0,Opacity=0};image.Loaded+=SourceCover;grid.Children.Add(image);
        var picture=new ImageBrush{Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Top};
        BindingOperations.SetBinding(picture,ImageBrush.ImageSourceProperty,new Binding("Source"){Source=image});
        var backdrop=new Border{Background=picture};grid.Children.Add(backdrop);
        grid.Children.Add(new Border{Background=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(230,7,12,29),0),new(Color.FromArgb(75,7,12,29),.6),new(Color.FromArgb(20,7,12,29),1)},0)});
        grid.Children.Add(new Border{Background=new LinearGradientBrush(new GradientStopCollection{new(Colors.Transparent,0),new(Color.FromArgb(230,7,12,29),1)},90)});
        var content=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(20,18,20,18)};grid.Children.Add(content);
        var eyebrow=new TextBlock{Text=primary?"В ЦЕНТРЕ ВНИМАНИЯ":"СТОИТ ПОСМОТРЕТЬ",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(185,200,255)),Margin=new(0,0,0,8)};content.Children.Add(eyebrow);
        var title=new TextBlock{Text=item.Title,FontSize=primary?25:22,FontWeight=FontWeights.Bold,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=65,ToolTip=item.Title,Margin=new(0,0,0,6)};content.Children.Add(title);
        content.Children.Add(new TextBlock{Text=string.Join(" · ",new[]{item.Year>0?item.Year.ToString():"",item.Genre}.Where(x=>x.Length>0)),Foreground=new SolidColorBrush(Color.FromRgb(202,212,237)),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,0,12)});
        var actions=new WrapPanel();content.Children.Add(actions);
        void Open(){current=item;Render();}
        var details=ActionButton(primary?"Выбрать раздачу":"Подробнее",primary?"IconDownload":"IconChevron",Open,primary?"PrimaryButton":"QuietButton");details.FontSize=12;details.Margin=new(0,0,8,0);details.Padding=new(12,9,12,9);details.MinHeight=36;
        if(!primary){details.Foreground=Brushes.White;details.Background=new SolidColorBrush(Color.FromArgb(140,35,46,85));details.BorderBrush=new SolidColorBrush(Color.FromArgb(160,120,139,204));}
        actions.Children.Add(details);
        return frame;
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
        if(discoveryShelf!=null&&discoveryShelfColumns!=catalogColumns)
        {
            discoveryShelfColumns=catalogColumns;
            discoveryShelf.Content=new CatalogRow(discoveryShelfItems.Take(catalogColumns).ToArray(),Math.Max(1,catalogColumns));
        }
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

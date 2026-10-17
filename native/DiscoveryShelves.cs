using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    enum DiscoveryShelfKind { Popular,New,Foreign,Native }
    sealed record DiscoveryRegionalPage(MediaItem[] Foreign,MediaItem[] Native,bool Loading);
    readonly Dictionary<string,DiscoveryRegionalPage> discoveryRegions=[];
    sealed class DiscoveryShelfView(ContentControl row,TextBlock state,Button all,FrameworkElement skeleton)
    {
        MediaItem[] items=[];
        int columns=1;
        public ContentControl Row=>row;
        public MediaItem[] Items=>items;
        public Action<MediaItem[]>? Changed {get;init;}
        // Poster shelves show one row; the wider game capsules fill two.
        public int Rows {get;init;}=1;
        // A shelf with its own layout (a story with a list, a ranking) draws itself from the items and the column count.
        public Action<MediaItem[],int>? Custom {get;set;}
        public UIElement? Host {get;set;}
        public void Set(MediaItem[] source,bool loading)
        {
            var appearing=items.Length==0;
            items=source.DistinctBy(item=>item.Id).Take(40).ToArray();
            appearing&=items.Length>0;
            state.Text=AppLanguage.T(loading?"Загружаем подборку…":"Подборка пока недоступна.");
            // While a shelf is still loading it shows grey poster shapes instead of a sentence.
            skeleton.Visibility=items.Length==0&&loading?Visibility.Visible:Visibility.Collapsed;
            state.Visibility=items.Length==0&&!loading?Visibility.Visible:Visibility.Collapsed;
            row.Visibility=items.Length>0&&Custom==null?Visibility.Visible:Visibility.Collapsed;
            all.IsEnabled=items.Length>0;
            Render();
            // A shelf that just filled up replaces its placeholders with a fade, not a jump.
            if(appearing)Reveal(Host??row);
        }
        public void Resize(int count)
        {
            count=Math.Clamp(count,1,8);if(columns==count)return;columns=count;Render();
        }
        void Render()
        {
            if(Custom!=null){Custom(items,columns);return;}
            var next=items.Take(columns*Rows).ToArray();
            if(row.Content is CatalogRow prior&&prior.Columns==columns)
            {
                if(prior.Update(next))Changed?.Invoke(next);
                return;
            }
            row.Content=new CatalogRow(next,columns);Changed?.Invoke(next);
        }
    }
    string DiscoveryShelfTitle(DiscoveryShelfKind kind)=>kind switch
    {
        DiscoveryShelfKind.Popular=>"Популярное",
        DiscoveryShelfKind.New=>section=="Фильмы"?"Новые фильмы":"Новые сериалы",
        DiscoveryShelfKind.Foreign=>section=="Фильмы"?"Иностранные фильмы":"Иностранные сериалы",
        _=>section=="Фильмы"?"Отечественные фильмы":"Отечественные сериалы"
    };
    DiscoveryShelfView AddDiscoveryShelf(Panel parent,DiscoveryShelfKind kind)=>AddShelf(parent,kind.ToString(),DiscoveryShelfTitle(kind),()=>OpenDiscoveryShelf(kind),"MediaRow",1);
    // A titled shelf with its "Ещё" button; the film home and the games home both build theirs here. Game capsules fill two rows, posters one.
    DiscoveryShelfView AddShelf(Panel parent,string id,string title,Action open,string template,int rows,string? kicker=null)
    {
        var sectionPanel=new StackPanel{Name="Discovery"+id+"Section",Margin=new(0,0,0,12)};parent.Children.Add(sectionPanel);
        // The mouse usually rests on the left (sidebar), so "Ещё" sits right next to the shelf title.
        var header=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,12)};sectionPanel.Children.Add(header);
        // A news-style rubric in small accent capitals before the heading.
        if(kicker!=null){var rubric=TextRaw(kicker.ToUpperInvariant(),11);rubric.FontWeight=FontWeights.Bold;rubric.Margin=new(0,0,12,0);rubric.VerticalAlignment=VerticalAlignment.Center;rubric.SetResourceReference(TextBlock.ForegroundProperty,"Accent");header.Children.Add(rubric);}
        var heading=Text(title,22);heading.Name="Discovery"+id+"Heading";heading.FontWeight=FontWeights.Bold;heading.Margin=new(0);heading.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(heading);
        var all=new Button{Style=(Style)FindResource("QuietButton"),MinHeight=32,Height=32,Margin=new(14,0,0,0),Padding=new(10,0,6,0),FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,ToolTip=AppLanguage.T("Показать: "+title)};
        all.SetResourceReference(Control.ForegroundProperty,"Accent");
        var allRow=new StackPanel{Orientation=Orientation.Horizontal};allRow.Children.Add(new TextBlock{Text=AppLanguage.T("Ещё"),VerticalAlignment=VerticalAlignment.Center});
        var chevron=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconChevron"),Width=13,Height=13,Stretch=Stretch.Uniform,StrokeThickness=1.9,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(4,1,0,0),VerticalAlignment=VerticalAlignment.Center};
        chevron.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=all});allRow.Children.Add(chevron);all.Content=allRow;
        all.Click+=(_,_)=>open();AutomationProperties.SetName(all,AppLanguage.T("Все: "+title));header.Children.Add(all);
        var state=Text("Загружаем подборку…",13,true);state.Margin=new(0,0,0,16);sectionPanel.Children.Add(state);
        var skeleton=PosterSkeleton(rows,"Discovery"+id+"Skeleton");skeleton.Visibility=Visibility.Collapsed;sectionPanel.Children.Add(skeleton);
        var row=new ContentControl{Name="Discovery"+id+"Row",ContentTemplate=(DataTemplate)FindResource(template),Margin=new(0,0,0,0)};sectionPanel.Children.Add(row);
        return new(row,state,all,skeleton){Changed=ApplyPosterDownloads,Rows=rows};
    }
    void OpenDiscoveryShelf(DiscoveryShelfKind kind)
    {
        if(kind==DiscoveryShelfKind.New)
        {
            CancelCatalogPositionRestoreForUserJump();
            FindVisual<TextBlock>(Body,element=>element.Name=="CatalogAllHeading")?.BringIntoView();return;
        }
        ChangeCatalogFilter(()=>
        {
            ResetCatalogFilters();
            if(kind==DiscoveryShelfKind.Popular){catalogCollection="popular";catalogOrder="По популярности";}
            else catalogRegion=kind==DiscoveryShelfKind.Native?"native":"foreign";
        });
    }
    DiscoveryRegionalPage DiscoveryRegionalItems(string category,string homeCountry,MediaItem[] cards)
    {
        discoveryRegions.TryGetValue(category+"|"+homeCountry,out var saved);
        var foreign=(saved?.Foreign??[]).Concat(cards).Where(item=>CatalogRegions.IsForeign(item,homeCountry)&&!NoDownloads(item)&&RecommendableTitle(item)&&PersonallyVisible(item)&&CatalogQualityMatches(item)).DistinctBy(item=>item.Id).Take(40).ToArray();
        var native=(saved?.Native??[]).Concat(cards).Where(item=>CatalogRegions.IsNative(item,homeCountry)&&!NoDownloads(item)&&RecommendableTitle(item)&&PersonallyVisible(item)&&CatalogQualityMatches(item)).DistinctBy(item=>item.Id).Take(40).ToArray();
        return new(foreign,native,(saved?.Loading??false)||CatalogQualityChecking);
    }
    void SetDiscoveryRegionalItems(string category,string homeCountry,MediaItem[] foreign,MediaItem[] native,bool loading=false)
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>SetDiscoveryRegionalItems(category,homeCountry,foreign,native,loading)));return;}
        var key=category+"|"+homeCountry;
        if(!discoveryRegions.ContainsKey(key)&&discoveryRegions.Count>=4)discoveryRegions.Remove(discoveryRegions.Keys.First());
        discoveryRegions[key]=new(foreign.Where(item=>CatalogRegions.IsForeign(item,homeCountry)).DistinctBy(item=>item.Id).Take(40).ToArray(),native.Where(item=>CatalogRegions.IsNative(item,homeCountry)).DistinctBy(item=>item.Id).Take(40).ToArray(),loading);
        if(closed||!DiscoveryCatalog||section!=category||prefs.HomeCountry!=homeCountry)return;
        StartDiscoveryQualityCheck();
        var page=DiscoveryRegionalItems(category,homeCountry,liveItems.ToArray());
        if(discoveryShelves.TryGetValue(DiscoveryShelfKind.Foreign,out var foreignShelf))foreignShelf.Set(page.Foreign,page.Loading);
        if(discoveryShelves.TryGetValue(DiscoveryShelfKind.Native,out var nativeShelf))nativeShelf.Set(page.Native,page.Loading);
        UpdateDiscoveryLayout();
    }
}

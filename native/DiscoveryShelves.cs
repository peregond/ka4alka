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
    sealed class DiscoveryShelfView(ContentControl row,TextBlock state,Button all)
    {
        MediaItem[] items=[];
        int columns=1;
        public ContentControl Row=>row;
        public MediaItem[] Items=>items;
        public Action<MediaItem[]>? Changed {get;init;}
        public void Set(MediaItem[] source,bool loading)
        {
            items=source.DistinctBy(item=>item.Id).Take(40).ToArray();
            state.Text=loading?"Загружаем подборку…":"Подборка пока недоступна.";
            state.Visibility=items.Length==0?Visibility.Visible:Visibility.Collapsed;
            row.Visibility=items.Length>0?Visibility.Visible:Visibility.Collapsed;
            all.IsEnabled=items.Length>0;
            Render();
        }
        public void Resize(int count)
        {
            count=Math.Clamp(count,1,8);if(columns==count)return;columns=count;Render();
        }
        void Render()
        {
            var next=items.Take(columns).ToArray();
            if(row.Content is CatalogRow prior&&prior.Columns==columns&&prior.Items.SequenceEqual(next))return;
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
    DiscoveryShelfView AddDiscoveryShelf(Panel parent,DiscoveryShelfKind kind)
    {
        var sectionPanel=new StackPanel{Name="Discovery"+kind+"Section",Margin=new(0,0,0,12)};parent.Children.Add(sectionPanel);
        var header=new DockPanel{Margin=new(0,0,0,16)};sectionPanel.Children.Add(header);
        var title=DiscoveryShelfTitle(kind);
        var all=new Button{Style=(Style)FindResource(typeof(Button)),MinHeight=36,Height=36,Margin=new(0),Padding=new(14,0,10,0),FontSize=13,FontWeight=FontWeights.SemiBold,ToolTip="Показать: "+title};
        all.SetResourceReference(Control.BackgroundProperty,"Panel");all.SetResourceReference(Control.BorderBrushProperty,"EdgeSoft");all.SetResourceReference(Control.ForegroundProperty,"TextSoft");
        var allRow=new StackPanel{Orientation=Orientation.Horizontal};allRow.Children.Add(new TextBlock{Text="Смотреть все",VerticalAlignment=VerticalAlignment.Center});
        var chevron=new System.Windows.Shapes.Path{Data=(Geometry)FindResource("IconChevron"),Width=14,Height=14,Stretch=Stretch.Uniform,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Margin=new(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        chevron.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=all});allRow.Children.Add(chevron);all.Content=allRow;
        all.Click+=(_,_)=>OpenDiscoveryShelf(kind);AutomationProperties.SetName(all,"Все: "+title);DockPanel.SetDock(all,Dock.Right);header.Children.Add(all);
        var heading=Text(title,22);heading.Name="Discovery"+kind+"Heading";heading.FontWeight=FontWeights.Bold;heading.Margin=new(0);heading.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(heading);
        var state=Text("Загружаем подборку…",13,true);state.Margin=new(0,0,0,16);sectionPanel.Children.Add(state);
        var row=new ContentControl{Name="Discovery"+kind+"Row",ContentTemplate=(DataTemplate)FindResource("MediaRow"),Margin=new(0,0,0,0)};sectionPanel.Children.Add(row);
        return new(row,state,all){Changed=ApplyPosterDownloads};
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
        var foreign=(saved?.Foreign??[]).Concat(cards).Where(item=>CatalogRegions.IsForeign(item,homeCountry)&&!NoDownloads(item)&&CatalogQualityMatches(item)).DistinctBy(item=>item.Id).Take(40).ToArray();
        var native=(saved?.Native??[]).Concat(cards).Where(item=>CatalogRegions.IsNative(item,homeCountry)&&!NoDownloads(item)&&CatalogQualityMatches(item)).DistinctBy(item=>item.Id).Take(40).ToArray();
        return new(foreign,native,saved?.Loading??false);
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

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    // Explicit production-country fixtures keep screenshot checks independent
    // of a remote source while exercising each shelf's real poster template.
    void PrepareDiscoverySmokeRegions(string category,IEnumerable<MediaItem> cards)
    {
        var home=CatalogRegions.HomeCountry(prefs.HomeCountry);var away=home=="ssha"?"rossiia":"ssha";
        var items=cards.Take(16).Select((item,index)=>item with{CountryKeys=[index%2==0?home:away],CountryKeysComplete=true}).ToArray();
        SetDiscoveryRegionalItems(category,home,items.Where(item=>CatalogRegions.IsForeign(item,home)).ToArray(),items.Where(item=>CatalogRegions.IsNative(item,home)).ToArray());
    }

    void CheckDiscoveryShelves(Action<bool,string> check,string stage)
    {
        check(DiscoveryCatalog&&discoveryShelves.Count==4,stage+": default catalog has four independent discovery shelves");
        var expected=section=="Фильмы"?new[]{DiscoveryShelfKind.Popular,DiscoveryShelfKind.New,DiscoveryShelfKind.Foreign,DiscoveryShelfKind.Native}:new[]{DiscoveryShelfKind.Popular,DiscoveryShelfKind.Foreign,DiscoveryShelfKind.New,DiscoveryShelfKind.Native};
        var sections=expected.Select(kind=>FindVisual<StackPanel>(Body,item=>item.Name=="Discovery"+kind+"Section")??throw new Exception(stage+": missing "+kind+" shelf.")).ToArray();
        var host=sections[0].Parent as StackPanel??throw new Exception(stage+": discovery shelves have no shared catalog container.");
        check(sections.All(item=>ReferenceEquals(item.Parent,host))&&sections.Select(host.Children.IndexOf).SequenceEqual(sections.Select(host.Children.IndexOf).Order()),stage+": discovery shelves follow the requested movie or series order");
        foreach(var kind in expected)
        {
            var row=discoveryShelves[kind].Row;
            var data=row.Content as CatalogRow??throw new Exception(stage+": "+kind+" has no poster row.");
            check(data.Columns==Math.Clamp(catalogColumns,1,8)&&data.Items.Length<=data.Columns,stage+": "+kind+" is one responsive row of at most eight posters");
            check(ReferenceEquals(row.ContentTemplate,FindResource("MediaRow")),stage+": "+kind+" uses the catalog's poster-card template");
            if(kind is DiscoveryShelfKind.Popular or DiscoveryShelfKind.New)check(data.Items.Length>0,stage+": "+kind+" contains real fixture cards");
            if(kind==DiscoveryShelfKind.Foreign)check(data.Items.All(item=>CatalogRegions.IsForeign(item,prefs.HomeCountry)),stage+": foreign shelf excludes unknown countries and home-country co-productions");
            if(kind==DiscoveryShelfKind.Native)check(data.Items.All(item=>CatalogRegions.IsNative(item,prefs.HomeCountry)),stage+": native shelf contains confirmed home-country productions");
            if(data.Items.Length==0)continue;
            var buttons=VisualElements<Button>(row).Where(button=>button.Tag is MediaItem).ToArray();
            check(buttons.Length==data.Items.Length&&buttons.All(button=>button.ActualWidth>0&&button.Tag is MediaItem item&&AutomationProperties.GetName(button)=="Открыть "+item.Title),stage+": "+kind+" poster cards have named navigation actions");
            var bounds=buttons.Select(button=>button.TransformToAncestor(row).TransformBounds(new Rect(new Point(),button.RenderSize))).ToArray();
            check(bounds.Max(rect=>rect.Top)-bounds.Min(rect=>rect.Top)<=1&&bounds.All(rect=>rect.Left>=-.5&&rect.Right<=row.ActualWidth+.5),stage+": "+kind+" posters align in one row without overflowing its width");
            var posters=VisualElements<Border>(row).Where(border=>border.Name=="CardPoster").ToArray();
            check(posters.Length==buttons.Length&&posters.All(poster=>poster.Clip is RectangleGeometry clip&&clip.RadiusX>0&&clip.RadiusY>0&&Math.Abs(poster.ActualHeight-poster.ActualWidth*1.5)<=1),stage+": "+kind+" poster artwork keeps its rounded corners and proportions");
        }
        var all=FindVisual<TextBlock>(Body,item=>item.Name=="CatalogAllHeading")??throw new Exception(stage+": missing complete catalog heading.");
        check(ReferenceEquals(all.Parent,host)&&host.Children.IndexOf(all)>sections.Max(host.Children.IndexOf)&&catalogList!=null&&host.Children.IndexOf(catalogList)>host.Children.IndexOf(all),stage+": complete paged catalog appears after all four shelves");
    }

    async Task CheckDiscoveryShelfActions(MediaItem[] fixtures,Func<Task> settle,Action<bool,string> check)
    {
        var category=section;var home=CatalogRegions.HomeCountry(prefs.HomeCountry);
        foreach(var region in new[]{"native","foreign"})
        {
            var selection=new CatalogSelection(Region:region,HomeCountry:home);var filtered=fixtures.Where(selection.Matches).ToArray();
            for(var page=1;page<=Math.Max(1,(filtered.Length+39)/40);page++)
                catalogPages[category+"|"+selection.Filter+"|"+page+"|region:"+region+"|home:"+home]=new(filtered.Skip((page-1)*40).Take(40).ToArray(),page*40<filtered.Length,CatalogChoices.Genres,CatalogChoices.Countries);
        }
        Button All(DiscoveryShelfKind kind)
        {
            var panel=FindVisual<StackPanel>(Body,item=>item.Name=="Discovery"+kind+"Section")??throw new Exception("Missing discovery navigation shelf: "+kind);
            return FindVisual<Button>(panel,button=>AutomationProperties.GetName(button)=="Все: "+DiscoveryShelfTitle(kind))??throw new Exception("Missing discovery all action: "+kind);
        }
        async Task Default(){ChangeCatalogFilter(ResetCatalogFilters);await settle();}
        var firstPage=liveItems.Select(item=>item.Id).ToArray();var originalKey=liveKey;
        All(DiscoveryShelfKind.New).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await settle();
        check(livePage==1&&liveKey==originalKey&&CatalogSelection.IsDefault&&liveItems.Select(item=>item.Id).SequenceEqual(firstPage),"New shelf opens the complete newest first page without skipping to page 2");
        check(FindVisual<ScrollViewer>(Body,_=>true) is {VerticalOffset:>0},"New shelf's All action scrolls to the complete catalog below the shelves");
        await Default();All(DiscoveryShelfKind.Popular).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await settle();
        check(livePage==1&&catalogCollection=="popular"&&CatalogSelection.Filter==""&&liveItems.Select(item=>item.Id).SequenceEqual(catalogPages[category+"||1"].Items.Select(item=>item.Id)),"Popular shelf opens the public source's popularity order on page 1");
        await Default();All(DiscoveryShelfKind.Native).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await settle();
        check(livePage==1&&catalogRegion=="native"&&CatalogSelection.Filter.Contains("country-"+home)&&liveItems.Count>0&&liveItems.All(item=>CatalogRegions.IsNative(item,home)),"Native shelf opens the confirmed home-country catalog filter");
        await Default();All(DiscoveryShelfKind.Foreign).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await settle();
        var foreignFirst=liveItems.Select(item=>item.Id).ToArray();
        check(livePage==1&&catalogRegion=="foreign"&&liveItems.Count>0&&liveItems.All(item=>CatalogRegions.IsForeign(item,home)),"Foreign shelf opens its confirmed country filter instead of the unfiltered list");
        if(catalogHasNext)
        {
            var next=FindVisual<Button>(Body,button=>AutomationProperties.GetName(button)=="Страница 2")??throw new Exception("Foreign discovery filter has no second page action.");
            next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await settle();
            check(livePage==2&&catalogRegion=="foreign"&&liveItems.Count>0&&liveItems.All(item=>CatalogRegions.IsForeign(item,home))&&!liveItems.Select(item=>item.Id).Intersect(foreignFirst).Any(),"Foreign discovery pagination keeps the regional selection without repeating page 1");
        }
        await Default();
    }
}

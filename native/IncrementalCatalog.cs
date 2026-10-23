using System.Windows;
namespace Kachalka;

public partial class MainWindow
{
    // Release checks only change evidence and membership. Keep the ScrollViewer, filter controls,
    // poster containers and their decoded images alive throughout those background results.
    internal Action<double>? catalogRefreshMeasured;
    bool RefreshCatalogInPlace()
    {
        if(closed||current!=null||activePerson!=null||section is not ("Фильмы" or "Сериалы")||catalogList==null||catalogRows==null||catalogEmptyHost==null)return false;
        var refreshStart=catalogRefreshMeasured==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
        var rawPage=CatalogPageItems();
        var cards=rawPage.Where(item=>(!DiscoveryCatalog||RecommendableTitle(item))&&CatalogQualityMatches(item)).ToArray();
        var membershipChanged=!catalogDisplay.Select(item=>item.Id).SequenceEqual(cards.Select(item=>item.Id));
        var position=membershipChanged?CaptureCatalogReflow():null;
        catalogDisplay=cards;
        var chunks=cards.Chunk(Math.Max(1,catalogColumns)).ToArray();
        for(var index=0;index<chunks.Length;index++)
        {
            if(index<catalogRows.Count)catalogRows[index].Update(chunks[index]);
            else catalogRows.Add(new(chunks[index],catalogColumns));
        }
        while(catalogRows.Count>chunks.Length)catalogRows.RemoveAt(catalogRows.Count-1);
        UpdateCatalogEmptyState(cards);
        if(DiscoveryCatalog)
        {
            var popular=DiscoveryPopularItems(section);var regions=DiscoveryRegionalItems(section,prefs.HomeCountry,cards);
            if(discoveryShelves.TryGetValue(DiscoveryShelfKind.Popular,out var top))top.Set(popular,CatalogQualityChecking||featuredRequests.Contains(section)&&!featuredFallback.Contains(section));
            if(discoveryShelves.TryGetValue(DiscoveryShelfKind.New,out var latest))latest.Set(cards,liveLoading||CatalogQualityChecking);
            if(discoveryShelves.TryGetValue(DiscoveryShelfKind.Foreign,out var foreign))foreign.Set(regions.Foreign,regions.Loading);
            if(discoveryShelves.TryGetValue(DiscoveryShelfKind.Native,out var native))native.Set(regions.Native,regions.Loading);
            var banners=popular.Length>0?popular:cards.Where(item=>RecommendableTitle(item)&&PersonallyVisible(item)).ToArray();
            RefreshDiscoveryHero(banners);
        }
        ApplyPosterDownloads(cards);RefreshLoadingIndicator();UpdateDiscoveryLayout();
        RestoreCatalogReflow(position);catalogRefreshMeasured?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(refreshStart).TotalMilliseconds);return true;
    }

    void RefreshDiscoveryHero(MediaItem[] banners)
    {
        var slides=banners.Take(5).ToArray();
        if(discoveryHero==null)
        {
            if(slides.Length==0||catalogList?.Parent is not System.Windows.Controls.Panel content)return;
            discoveryHero=new System.Windows.Controls.Grid();content.Children.Insert(0,discoveryHero);
        }
        var waiting=liveLoading||CatalogQualityChecking||featuredRequests.Contains(section);
        if(carousel==null&&slides.Length==0&&waiting&&discoveryHero.Children.Count>0)return;
        if(carousel!=null&&carousel.Slides.Select(item=>item.Id).SequenceEqual(slides.Select(item=>item.Id)))return;
        var position=CaptureCatalogReflow();var selected=carousel?.Bound?.Id;
        if(carousel!=null)DetachFeatureWatch(carousel);
        carousel=null;discoveryHero.Children.Clear();
        if(slides.Length>0)
        {
            discoveryHero.Margin=new(0,0,0,28);discoveryHero.Children.Add(BuildFeatureCarousel(slides));
            var index=Array.FindIndex(slides,item=>item.Id==selected);
            if(index>0&&carousel!=null)ShowFeatureSlide(carousel,index);
        }
        else if(waiting)
        {discoveryHero.Margin=new(0);discoveryHero.Children.Add(BannerSkeleton());}
        if(slides.Length==0&&!waiting)discoveryHero.Margin=new(0);
        UpdateHeroPageLayout();
        RestoreCatalogReflow(position);
    }
}

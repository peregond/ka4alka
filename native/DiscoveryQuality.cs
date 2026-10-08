namespace Kachalka;

public partial class MainWindow
{
    MediaItem[] DiscoveryQualityCandidates(MediaItem[] page)
    {
        if(!DiscoveryCatalog)return page;
        var groups=new List<MediaItem[]>{page};
        if(catalogPages.TryGetValue(section+"||1",out var popular)&&!featuredFallback.Contains(section))groups.Add(popular.Items);
        if(discoveryRegions.TryGetValue(section+"|"+prefs.HomeCountry,out var regions)){groups.Add(regions.Foreign);groups.Add(regions.Native);}
        // Interleave the four source rows so one slow page cannot consume the
        // entire deadline before shelf-only titles receive a quality lookup.
        return Enumerable.Range(0,CatalogPaging.Size).SelectMany(index=>groups.Where(items=>index<items.Length).Select(items=>items[index])).Where(item=>item.IsLive).DistinctBy(item=>item.Id).Take(CatalogPaging.Size*4).ToArray();
    }
    void StartDiscoveryQualityCheck()
    {
        if(DiscoveryCatalog&&prefs.CatalogQualityHeight!=0)StartCatalogQualityCheck(liveItems.ToArray());
    }
}

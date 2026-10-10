using MonoTorrent.Client;
namespace Kachalka;

public static class MagnetDiscovery
{
    // HTTPS endpoints verified with a real announce for the reported TPB hash.
    // Additional UDP endpoints come from the maintained public trackers list.
    public static IReadOnlyList<string> PublicTrackers {get;}=Array.AsReadOnly(new[] {
        "https://tracker.foreverpirates.co/announce",
        "https://open.ftorrent.com/announce",
        "https://tr.nyacat.pw/announce",
        "udp://open.stealth.si:80/announce",
        "udp://exodus.desync.com:6969/announce",
        // Large public trackers that answer outside Russia; each gets its own tier, so a
        // timeout there never holds up the others.
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.demonii.com:1337/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://explodie.org:6969/announce"
    });
    // RuTracker's own announce addresses for magnet links (t-ru.org is not in the
    // Russian registry) and the local retracker many Russian providers run. A
    // RuTracker magnet from Knaben carries only opentrackr, which does not answer
    // from Russia, while the public HTTPS and UDP trackers above do.
    public static IReadOnlyList<string> RuTrackerTrackers {get;}=Array.AsReadOnly(new[] {
        "http://bt.t-ru.org/ann?magnet",
        "http://bt2.t-ru.org/ann?magnet",
        "http://bt3.t-ru.org/ann?magnet",
        "http://bt4.t-ru.org/ann?magnet",
        "http://retracker.local/announce"
    });
    public static IReadOnlyList<string>? Fallbacks(string? source,IReadOnlyList<string> publicTrackers)=>
        source=="RuTracker"?[..RuTrackerTrackers,..publicTrackers]:PublicSource(source)?publicTrackers:null;
    public static bool PublicSource(string? source)=>source is "The Pirate Bay" or "RuTor" or "NNM-Club" or "MegaPeer" or "BigFanGroup" or "Nyaa" or "EZTV" or "Internet Archive";
    public static async Task ConfigureAsync(TorrentManager manager,IEnumerable<string> fallbacks)
    {
        // Private torrents retain their original tracker policy. Manual links are
        // excluded by the caller.
        if(manager.TrackerManager.Private)return;
        var trackers=manager.TrackerManager;
        // MonoTorrent puts every magnet tr= in one sequential tier. Give each
        // tracker its own tier, so a UDP timeout cannot hold up HTTPS discovery.
        foreach(var tier in trackers.Tiers.ToArray())
            foreach(var tracker in tier.Trackers.Skip(1).ToArray())
            {
                await trackers.RemoveTrackerAsync(tracker);
                await trackers.AddTrackerAsync(tracker);
            }
        var existing=trackers.Tiers.SelectMany(t=>t.Trackers).Select(t=>t.Uri).ToHashSet();
        foreach(var address in fallbacks)
        {
            var uri=new Uri(address);
            if(existing.Add(uri))await trackers.AddTrackerAsync(uri);
        }
    }
}

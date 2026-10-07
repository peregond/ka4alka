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
        "udp://exodus.desync.com:6969/announce"
    });
    public static async Task ConfigureAsync(TorrentManager manager,IEnumerable<string> fallbacks)
    {
        // .torrent files and private torrents retain their original tracker policy.
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

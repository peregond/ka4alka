import { relayTorrent } from "../web-index/lib/torrent-relay";
import { searchReleases } from "../web-index/lib/release-search";

// The site's hosting refuses visitors from Russia and Belarus. This Worker runs
// the same release search and torrent relay on a host that answers there; the
// application finds its address in distribution/relays.json.
export default {
  async fetch(request:Request):Promise<Response> {
    const path=new URL(request.url).pathname;
    if(request.method!=="GET")return new Response(null,{status:405,headers:{"Allow":"GET"}});
    if(path==="/api/torrent")return relayTorrent(request);
    if(path==="/api/search")return searchReleases(request);
    if(path==="/")return Response.json({service:"ka4alka-relay"},{headers:{"Cache-Control":"no-store"}});
    return new Response(null,{status:404});
  }
};

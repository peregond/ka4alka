import { relayTorrent } from "../web-index/lib/torrent-relay";
import { searchReleases } from "../web-index/lib/release-search";

// The site's hosting refuses visitors from Russia and Belarus. This Worker runs
// the same release search and torrent relay on a host that answers there; the
// application finds its address in distribution/relays.json.

// Best-effort per-address limit. Each isolate counts on its own, which is enough to stop one client
// from using the relay to hammer the release sources.
const LIMIT=40,WINDOW=60_000,MAX_TRACKED=2048;
const hits=new Map<string,{since:number;count:number}>();
function allowed(request:Request):boolean {
  const now=Date.now(),key=request.headers.get("CF-Connecting-IP")??"unknown";
  if(hits.size>=MAX_TRACKED)for(const [id,entry] of hits)if(now-entry.since>WINDOW)hits.delete(id);
  const entry=hits.get(key);
  if(!entry||now-entry.since>WINDOW){if(hits.size>=MAX_TRACKED&&!entry)return false;hits.set(key,{since:now,count:1});return true;}
  entry.count++;
  return entry.count<=LIMIT;
}

function secured(response:Response):Response {
  const result=new Response(response.body,response);
  result.headers.set("X-Content-Type-Options","nosniff");
  result.headers.set("Referrer-Policy","no-referrer");
  result.headers.set("Content-Security-Policy","default-src 'none'; frame-ancestors 'none'");
  return result;
}

export default {
  async fetch(request:Request):Promise<Response> {
    const path=new URL(request.url).pathname;
    if(request.method!=="GET")return secured(new Response(null,{status:405,headers:{"Allow":"GET"}}));
    if(path==="/api/torrent"||path==="/api/search") {
      if(!allowed(request))return secured(new Response(null,{status:429,headers:{"Retry-After":"60"}}));
      return secured(await (path==="/api/torrent"?relayTorrent(request):searchReleases(request)));
    }
    if(path==="/")return secured(Response.json({service:"ka4alka-relay"},{headers:{"Cache-Control":"no-store"}}));
    return secured(new Response(null,{status:404}));
  }
};

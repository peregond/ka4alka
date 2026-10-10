// Torrent files of trackers that some networks block (RuTor, NNM-Club, MegaPeer,
// BigFanGroup, Nyaa and others are in the Russian registry) are fetched by the
// site and passed on unchanged. Only the exact download addresses that the
// release sources produce are accepted, so the route is not a general proxy.
const patterns:Record<string,RegExp>={
  "nnmclub.to":/^\/forum\/download\.php\?id=\d{1,12}$/,
  "megapeer.vip":/^\/download\/\d{1,12}$/,
  "bigfangroup.org":/^\/download\.php\?id=\d{1,12}$/,
  "nyaa.si":/^\/download\/\d{1,12}\.torrent$/
};
export const TORRENT_LIMIT=10*1024*1024;

export function relaySource(raw:string|null|undefined):URL|null {
  if(!raw||raw.length>300)return null;
  let url:URL;
  try{url=new URL(raw);}catch{return null;}
  const pattern=patterns[url.hostname];
  if(url.protocol!=="https:"||url.username||url.password||url.port||url.hash||!pattern)return null;
  return pattern.test(url.pathname+url.search)?url:null;
}

// A redirect may move to a subdomain of the tracker, never to another site.
export function sameTracker(source:URL,final:string) {
  try{const url=new URL(final);return url.protocol==="https:"&&(url.hostname===source.hostname||url.hostname.endsWith("."+source.hostname));}
  catch{return false;}
}

export function relayPath(raw:string|null|undefined) {
  const url=relaySource(raw);
  return url?`/api/torrent?url=${encodeURIComponent(url.href)}`:null;
}

// Walks the bencoded structure and requires a top-level dictionary with an
// "info" dictionary. A tracker's HTML error or an ISP block page fails here.
export function isTorrent(bytes:Uint8Array) {
  let position=0,depth=0,info=false;
  const digit=(byte:number)=>byte>=0x30&&byte<=0x39;
  function integer(end:number) {
    const start=position;
    if(bytes[position]===0x2d)position++;
    if(!digit(bytes[position]??-1))throw new Error("integer");
    while(digit(bytes[position]??-1))position++;
    if(bytes[position]!==end||position-start>20)throw new Error("integer");
    const value=Number(new TextDecoder().decode(bytes.subarray(start,position)));position++;return value;
  }
  function string() {
    const length=integer(0x3a);
    if(length<0||position+length>bytes.length)throw new Error("string");
    const value=bytes.subarray(position,position+length);position+=length;return value;
  }
  function value():"dict"|"other" {
    const byte=bytes[position];
    if(byte===0x69){position++;integer(0x65);return "other";}
    if(digit(byte??-1)){string();return "other";}
    if(byte!==0x6c&&byte!==0x64)throw new Error("value");
    if(++depth>64)throw new Error("depth");
    position++;
    const top=depth===1&&byte===0x64;
    while(bytes[position]!==0x65) {
      if(position>=bytes.length)throw new Error("end");
      if(byte===0x64) {
        const key=new TextDecoder().decode(string());
        const kind=value();
        if(top&&key==="info"&&kind==="dict")info=true;
      } else value();
    }
    position++;depth--;
    return byte===0x64?"dict":"other";
  }
  try{return value()==="dict"&&position===bytes.length&&info;}
  catch{return false;}
}

const failure=(status:number,error:string)=>Response.json({error},{status,headers:{"Cache-Control":"no-store"}});

// GET /api/torrent?url=<tracker download address>, served by the site and by the standalone relay.
export async function relayTorrent(request:Request):Promise<Response> {
  const source=relaySource(new URL(request.url).searchParams.get("url"));
  if(!source)return failure(400,"Этот torrent-файл нельзя получить через сайт.");
  let upstream:Response;
  try {
    upstream=await fetch(source.href,{headers:{"Accept":"application/x-bittorrent,*/*","User-Agent":"KachalkaIndex/0.4"},signal:AbortSignal.timeout(12_000),cache:"no-store"});
  } catch {return failure(502,"Трекер не ответил.");}
  if(!upstream.ok)return failure(502,`Трекер вернул ${upstream.status}.`);
  if(!sameTracker(source,upstream.url||source.href))return failure(502,"Трекер перенаправил на другой сайт.");
  if(Number(upstream.headers.get("content-length")??0)>TORRENT_LIMIT)return failure(413,"Torrent-файл слишком большой.");
  // Read in chunks: a missing or wrong Content-Length must not let a response grow past the limit.
  const reader=upstream.body?.getReader();
  if(!reader)return failure(502,"Трекер вернул не torrent-файл.");
  const chunks:Uint8Array[]=[];let size=0;
  try {
    for(;;){const next=await reader.read();if(next.done)break;size+=next.value.byteLength;if(size>TORRENT_LIMIT)return failure(413,"Torrent-файл слишком большой.");chunks.push(next.value);}
  } catch {return failure(502,"Трекер не ответил.");}
  finally {await reader.cancel().catch(()=>undefined);}
  const body=new Uint8Array(size);let offset=0;
  for(const chunk of chunks){body.set(chunk,offset);offset+=chunk.byteLength;}
  if(!isTorrent(body))return failure(502,"Трекер вернул не torrent-файл.");
  const name=`${source.hostname.split(".")[0]}-${/(\d{1,12})/.exec(source.pathname+source.search)?.[1]??"release"}.torrent`;
  return new Response(body,{headers:{"Content-Type":"application/x-bittorrent","Content-Disposition":`attachment; filename="${name}"`,"Cache-Control":"no-store","X-Content-Type-Options":"nosniff"}});
}

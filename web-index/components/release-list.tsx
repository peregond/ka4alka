"use client";

import { useMemo, useState } from "react";
import { Download, ExternalLink } from "lucide-react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { Release } from "@/lib/index-store";
import { relayPath } from "@/lib/torrent-relay";

const sizeLabel=(size:number|null)=>size&&size>0?size>=1073741824?`${(size/1073741824).toFixed(1).replace(".",",")} ГБ`:`${Math.round(size/1048576)} МБ`:"Размер не указан";
const seasonLabel=(season:number|null)=>season===null?"Сезон не указан":`${season} сезон`;

export function ReleaseList({items,error}:{items:Release[]|null;error:string|null}) {
  const [source,setSource]=useState("all"),[quality,setQuality]=useState("all"),[season,setSeason]=useState("all"),[count,setCount]=useState(40);
  const sources=useMemo(()=>[...new Set((items??[]).map(x=>x.source))].sort(),[items]);
  const qualities=useMemo(()=>[...new Set((items??[]).map(x=>x.quality).filter((x):x is string=>Boolean(x)))].sort().reverse(),[items]);
  const seasons=useMemo(()=>[...new Set((items??[]).map(x=>x.season).filter((x):x is number=>x!==null))].sort((a,b)=>a-b),[items]);
  const visible=useMemo(()=>(items??[]).filter(x=>(source==="all"||x.source===source)&&(quality==="all"||x.quality===quality)&&(season==="all"||String(x.season)===season)),[items,source,quality,season]);
  return <section className="release-section"><div className="release-heading"><div><p className="eyebrow">РАЗДАЧИ</p><h3>Варианты загрузки</h3></div><span>{items?.length??0}</span></div>
    {items===null?<div className="release-placeholder">Ищем доступные варианты в источниках…</div>:error?<div className="release-placeholder">{error}</div>:items.length===0?<div className="release-placeholder">Подходящих раздач пока нет. Повтори поиск позже.</div>:<>
      <div className="release-filters">
        <label>Источник<Select value={source} onValueChange={setSource}><SelectTrigger aria-label="Источник"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Все источники</SelectItem>{sources.map(x=><SelectItem key={x} value={x}>{x}</SelectItem>)}</SelectContent></Select></label>
        <label>Качество<Select value={quality} onValueChange={setQuality}><SelectTrigger aria-label="Качество"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Любое</SelectItem>{qualities.map(x=><SelectItem key={x} value={x}>{x}</SelectItem>)}</SelectContent></Select></label>
        {seasons.length>0&&<label>Сезон<Select value={season} onValueChange={setSeason}><SelectTrigger aria-label="Сезон"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Все сезоны</SelectItem>{seasons.map(x=><SelectItem key={x} value={String(x)}>{x} сезон</SelectItem>)}</SelectContent></Select></label>}
      </div>
      <p className="release-count">Показано {Math.min(visible.length,count)} из {visible.length}. Сиды обновляются при новом поиске.</p>
      <div className="release-list">{visible.slice(0,count).map(item=><article className="release-card" key={item.id}><div className="release-main"><p className="release-name">{item.title}</p><div className="release-tags"><span>{item.source}</span>{item.via&&<span>через {item.via}</span>}{item.quality&&<span>{item.quality}</span>}{item.season!==null&&<span>{seasonLabel(item.season)}{item.episode!==null?` · ${item.episode} серия`:""}</span>}</div><p className="release-meta">{sizeLabel(item.size)} · {item.seeds===null?"Сиды неизвестны":`${item.seeds} сидов`}</p></div>{item.torrentUrl?<a className="release-action" href={relayPath(item.torrentUrl)??item.torrentUrl} target="_blank" rel="noreferrer" aria-label={`Открыть раздачу ${item.title}`}><Download size={18}/></a>:item.pageUrl?<a className="release-action" href={item.pageUrl} target="_blank" rel="noreferrer" aria-label={`Открыть источник ${item.title}`}><ExternalLink size={18}/></a>:null}</article>)}</div>
      {visible.length>count&&<button className="more-button" onClick={()=>setCount(x=>x+40)}>Показать ещё</button>}
    </>}
  </section>;
}

"use client";

import { useMemo, useState } from "react";
import { useSiteLanguage } from "@/components/site-language";
import { downloadSize } from "@/lib/site-language";
import { Download, ExternalLink } from "lucide-react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { Release } from "@/lib/index-store";
import { relayPath } from "@/lib/torrent-relay";

export function ReleaseList({items,error}:{items:Release[]|null;error:string|null}) {
  const { language, t } = useSiteLanguage();
  const [source,setSource]=useState("all"),[quality,setQuality]=useState("all"),[season,setSeason]=useState("all"),[count,setCount]=useState(40);
  const sources=useMemo(()=>[...new Set((items??[]).map(x=>x.source))].sort(),[items]);
  const qualities=useMemo(()=>[...new Set((items??[]).map(x=>x.quality).filter((x):x is string=>Boolean(x)))].sort().reverse(),[items]);
  const seasons=useMemo(()=>[...new Set((items??[]).map(x=>x.season).filter((x):x is number=>x!==null))].sort((a,b)=>a-b),[items]);
  const visible=useMemo(()=>(items??[]).filter(x=>(source==="all"||x.source===source)&&(quality==="all"||x.quality===quality)&&(season==="all"||String(x.season)===season)),[items,source,quality,season]);
  return <section className="release-section"><div className="release-heading"><div><p className="eyebrow">{t('РАЗДАЧИ')}</p><h3>{t('Варианты загрузки')}</h3></div><span>{items?.length??0}</span></div>
    {items===null?<div className="release-placeholder">{t('Ищем доступные варианты в источниках…')}</div>:error?<div className="release-placeholder">{t(error)}</div>:items.length===0?<div className="release-placeholder">{t('Подходящих раздач пока нет. Повтори поиск позже.')}</div>:<>
      <div className="release-filters">
        <label>{t('Источник')}<Select value={source} onValueChange={setSource}><SelectTrigger aria-label={t('Источник')}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">{t('Все источники')}</SelectItem>{sources.map(x=><SelectItem key={x} value={x}>{x}</SelectItem>)}</SelectContent></Select></label>
        <label>{t('Качество')}<Select value={quality} onValueChange={setQuality}><SelectTrigger aria-label={t('Качество')}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">{t('Любое')}</SelectItem>{qualities.map(x=><SelectItem key={x} value={x}>{x}</SelectItem>)}</SelectContent></Select></label>
        {seasons.length>0&&<label>{t('Сезон')}<Select value={season} onValueChange={setSeason}><SelectTrigger aria-label={t('Сезон')}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">{t('Все сезоны')}</SelectItem>{seasons.map(x=><SelectItem key={x} value={String(x)}>{t("{season} сезон",{season:x})}</SelectItem>)}</SelectContent></Select></label>}
      </div>
      <p className="release-count">{t("Показано {shown} из {total}. Число отдающих обновляется при новом поиске.",{shown:Math.min(visible.length,count),total:visible.length})}</p>
      <div className="release-list">{visible.slice(0,count).map(item=><article className="release-card" key={item.id}><div className="release-main"><p className="release-name">{item.title}</p><div className="release-tags"><span>{item.source}</span>{item.via&&<span>{t("через {source}",{source:item.via})}</span>}{item.quality&&<span>{item.quality}</span>}{item.season!==null&&<span>{t("{season} сезон",{season:item.season})}{item.episode!==null?` · ${t("{episode} серия",{episode:item.episode})}`:""}</span>}</div><p className="release-meta">{downloadSize(language,item.size)} · {item.seeds===null?t("Сколько отдают — неизвестно"):t("отдают: {count}",{count:item.seeds})}</p></div>{item.torrentUrl?<a className="release-action" href={relayPath(item.torrentUrl)??item.torrentUrl} target="_blank" rel="noreferrer" aria-label={t("Открыть раздачу {title}",{title:item.title})}><Download size={18}/></a>:item.pageUrl?<a className="release-action" href={item.pageUrl} target="_blank" rel="noreferrer" aria-label={t("Открыть источник {title}",{title:item.title})}><ExternalLink size={18}/></a>:null}</article>)}</div>
      {visible.length>count&&<button className="more-button" onClick={()=>setCount(x=>x+40)}>{t('Показать ещё')}</button>}
    </>}
  </section>;
}

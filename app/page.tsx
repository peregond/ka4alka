"use client";

import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { ArrowUpRight, Clapperboard, Film, Search, Tv } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Sidebar, SidebarContent, SidebarHeader, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ReleaseList } from "@/components/release-list";
import { normalize, type Media, type Section } from "@/lib/catalog-source";
import type { Release } from "@/lib/index-store";
import seed from "./data/seed.json";

const initial = seed as Media[];

function PosterImage({src,className,lazy=false}:{src:string|null|undefined;className:string;lazy?:boolean}) {
  const [failed,setFailed]=useState(false);
  return <span className={className}>
    {src&&!failed?<img src={src} alt="" loading={lazy?"lazy":"eager"} referrerPolicy="no-referrer" onError={()=>setFailed(true)}/>:<span className="poster-fallback">Постер недоступен</span>}
  </span>;
}

export default function Home() {
  const [section,setSection] = useState<Section>("movies");
  const [query,setQuery] = useState("");
  const [selected,setSelected] = useState<Media|null>(null);
  const [pool,setPool] = useState<Media[]>(initial);
  const [page,setPage] = useState(1);
  const [hasMore,setHasMore] = useState(true);
  const [catalogBusy,setCatalogBusy] = useState(false);
  const [catalogState,setCatalogState] = useState("Сохранённая подборка");
  const [releases,setReleases] = useState<Release[]|null>(null);
  const [releaseError,setReleaseError] = useState<string|null>(null);
  const items = useMemo(() => {
    const words=normalize(query).split(" ").filter(Boolean);
    return pool.filter(item=>item.section===section&&words.every(word=>normalize(`${item.title} ${item.originalTitle??""} ${item.year}`).split(" ").some(part=>part.startsWith(word))));
  },[section,query,pool]);

  useEffect(()=>{
    const controller=new AbortController();
    const timer=setTimeout(async()=>{
      setCatalogBusy(true);
      try {
        const response=await fetch(`/api/catalog?section=${section}&q=${encodeURIComponent(query)}&page=${page}`,{signal:controller.signal});
        if(!response.ok)throw new Error("Индекс временно недоступен");
        const data=await response.json() as {items:Media[];sourceStatus:string};
        if(!controller.signal.aborted){
          const incoming=Array.isArray(data.items)?data.items:[];
          setPool(previous=>{const merged=new Map(previous.map(item=>[item.id,item]));for(const item of incoming)merged.set(item.id,{...merged.get(item.id),...item});return [...merged.values()]});
          setHasMore(incoming.length>=40);
          setCatalogState(data.sourceStatus==="updated"?"Каталог обновлён":"Сохранённая подборка");
        }
      } catch {if(!controller.signal.aborted)setCatalogState("Сохранённая подборка");}
      finally {if(!controller.signal.aborted)setCatalogBusy(false);}
    },query?420:0);
    return ()=>{controller.abort();clearTimeout(timer)};
  },[section,query,page]);

  const selectedId=selected?.id;
  useEffect(()=>{
    if(!selectedId)return;
    const id=selectedId,controller=new AbortController();
    void (async()=>{
      try {const response=await fetch(`/api/media?id=${encodeURIComponent(id)}`,{signal:controller.signal});if(response.ok){const data=await response.json() as {item:Media};if(!controller.signal.aborted)setSelected(current=>current?.id===id?{...current,...data.item}:current);}} catch {}
    })();
    void (async()=>{
      try {const response=await fetch(`/api/releases?id=${encodeURIComponent(id)}`,{signal:controller.signal});if(!response.ok)throw new Error();const data=await response.json() as {items:Release[]};if(!controller.signal.aborted)setReleases(data.items??[]);}
      catch {if(!controller.signal.aborted){setReleases([]);setReleaseError("Не удалось обновить варианты. Попробуй открыть карточку позже.");}}
    })();
    return ()=>controller.abort();
  },[selectedId]);

  const openMedia=(item:Media)=>{setReleases(null);setReleaseError(null);setSelected(item)};

  return <SidebarProvider style={{"--sidebar-width":"14.5rem"} as CSSProperties}>
    <Sidebar collapsible="offcanvas" className="index-sidebar">
      <SidebarHeader className="brand-header"><div className="brand-mark"><Clapperboard size={21}/></div><div><strong>Ка4алка</strong><span>Онл@йн</span></div></SidebarHeader>
      <SidebarContent className="nav-content"><p className="nav-caption">КАТАЛОГ</p><SidebarMenu>
        <SidebarMenuItem><SidebarMenuButton isActive={section==="movies"} onClick={()=>{setSection("movies");setPage(1);setSelected(null)}}><Film size={19}/><span>Фильмы</span></SidebarMenuButton></SidebarMenuItem>
        <SidebarMenuItem><SidebarMenuButton isActive={section==="series"} onClick={()=>{setSection("series");setPage(1);setSelected(null)}}><Tv size={19}/><span>Сериалы</span></SidebarMenuButton></SidebarMenuItem>
      </SidebarMenu><div className="nav-note">Новые названия и варианты загрузки собираются в одном месте.</div></SidebarContent>
    </Sidebar>
    <SidebarInset className="index-main">
      <header className="topbar"><SidebarTrigger className="mobile-menu" aria-label="Открыть разделы"/><div className="topbar-name">Ка4алка Онл@йн <span>/</span> {section==="movies"?"Фильмы":"Сериалы"}</div><div className="topbar-state"><span className="state-dot"/> {catalogBusy?"Обновляем каталог…":catalogState}</div></header>
      <div className="workspace"><div className="workspace-head"><div><p className="eyebrow">КИНОТЕКА</p><h1>{section==="movies"?"Фильмы":"Сериалы"}</h1><p className="lead">Выбирай по постеру. Подробности и варианты — в карточке.</p></div><div className="result-count">{items.length} {section==="movies"?"фильмов":"сериалов"}</div></div>
        <div className="search-wrap"><Search size={20} aria-hidden="true"/><Input value={query} onChange={event=>{setQuery(event.target.value);setPage(1)}} placeholder={section==="movies"?"Название фильма, год…":"Название сериала, год…"} aria-label="Поиск по каталогу"/></div>
        <div className="content-label"><span>{query?"Результаты поиска":"Недавно добавлены"}</span><span className="hairline"/></div>
        {items.length?<div className="poster-grid">{items.map(item=><button type="button" className="poster-card" key={item.id} onClick={()=>openMedia(item)} aria-label={`Открыть ${item.title}`}><PosterImage key={item.poster??item.id} src={item.poster} className="poster-frame" lazy/><span className="poster-title">{item.title}</span><span className="poster-meta">{item.year||"Год неизвестен"}{item.kinopoisk&&item.kinopoisk!=="—"?` · КП ${item.kinopoisk}`:""}</span></button>)}</div>:<div className="empty-state"><Search size={28}/><h2>Ничего не нашлось</h2><p>Попробуй другое название или убери год из запроса.</p></div>}
        {!query&&hasMore&&<button className="more-button catalog-more" onClick={()=>setPage(value=>value+1)} disabled={catalogBusy}>Показать ещё</button>}
      </div>
    </SidebarInset>
    <Sheet open={selected!==null} onOpenChange={open=>{if(!open)setSelected(null)}}><SheetContent className="detail-sheet" side="right">{selected&&<div className="detail-scroll"><SheetHeader className="detail-header"><p className="eyebrow">{selected.section==="movies"?"ФИЛЬМ":"СЕРИАЛ"} · {selected.year}</p><SheetTitle>{selected.title}</SheetTitle><SheetDescription>{selected.originalTitle&&selected.originalTitle!==selected.title?selected.originalTitle:"Подробности"}</SheetDescription></SheetHeader><div className="detail-top"><PosterImage key={selected.poster??selected.id} src={selected.poster} className="detail-poster"/><div className="detail-facts"><div className="rating">Кинопоиск <strong>{selected.kinopoisk||"—"}</strong></div><div className="rating">IMDb <strong>{selected.imdb||"—"}</strong></div></div></div><p className="detail-description">{selected.description||"Описание скоро появится."}</p><a className="source-link" href={selected.pageUrl} target="_blank" rel="noreferrer">Страница в каталоге <ArrowUpRight size={17}/></a><ReleaseList key={selected.id} items={releases} error={releaseError}/></div>}</SheetContent></Sheet>
  </SidebarProvider>;
}

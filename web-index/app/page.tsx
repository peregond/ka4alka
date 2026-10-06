"use client";

import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { ArrowUpRight, Clapperboard, Film, PanelLeft, Search, Tv } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Sidebar, SidebarContent, SidebarHeader, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider } from "@/components/ui/sidebar";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ReleaseList } from "@/components/release-list";
import { normalize, type Media, type Section } from "@/lib/catalog-source";
import type { Release } from "@/lib/index-store";
import seed from "./data/seed.json";

const initial = seed as Media[];
const savedPosters = new Map(initial.filter(item=>item.poster?.startsWith("/posters/")).map(item=>[item.id,item.poster!]));
const posterSrc = (item:Media) => savedPosters.get(item.id) ?? (item.poster?.startsWith("https://")?`/api/poster?src=${encodeURIComponent(item.poster)}`:null);
const countLabel = (count:number, section:Section) => {
  const forms = section === "movies" ? ["фильм", "фильма", "фильмов"] : ["сериал", "сериала", "сериалов"];
  const form = count % 100 >= 11 && count % 100 <= 14 ? 2 : count % 10 === 1 ? 0 : count % 10 >= 2 && count % 10 <= 4 ? 1 : 2;
  return `${count} ${forms[form]}`;
};

function PosterImage({item,className,lazy=false,rating=false}:{item:Media;className:string;lazy?:boolean;rating?:boolean}) {
  const [attempt,setAttempt]=useState(0);
  const primary=posterSrc(item);
  const secondary=item.poster?.startsWith("https://")?item.poster:null;
  const src=attempt===0?primary:attempt===1&&secondary!==primary?secondary:null;
  return <span className={className}>
    {src?<img src={src} alt="" loading={lazy?"lazy":"eager"} referrerPolicy="no-referrer" onError={()=>setAttempt(value=>value+1)}/>:<span className="poster-fallback" aria-hidden="true"><Clapperboard size={24}/><span>Постер недоступен</span></span>}
    {rating&&item.kinopoisk&&item.kinopoisk!=="—"?<span className="poster-rating">★ {item.kinopoisk}</span>:null}
  </span>;
}

export default function Home() {
  const [section,setSection] = useState<Section>("movies");
  const [query,setQuery] = useState("");
  const [selected,setSelected] = useState<Media|null>(null);
  const [pool,setPool] = useState<Media[]>(initial.filter(x=>x.section==="movies").slice(0,40));
  const [page,setPage] = useState(1);
  const [hasMore,setHasMore] = useState(true);
  const [catalogBusy,setCatalogBusy] = useState(false);
  const [catalogState,setCatalogState] = useState("Сохранённая подборка");
  const [releases,setReleases] = useState<Release[]|null>(null);
  const [releaseError,setReleaseError] = useState<string|null>(null);
  const [mobileNavOpen,setMobileNavOpen] = useState(false);
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
        const data=await response.json() as {items:Media[];sourceStatus:string;hasMore:boolean};
        if(!controller.signal.aborted){
          const incoming=Array.isArray(data.items)?data.items:[];
          setPool(previous=>{const merged=new Map(previous.map(item=>[item.id,item]));for(const item of incoming)merged.set(item.id,{...merged.get(item.id),...item});return [...merged.values()]});
          setHasMore(data.hasMore);
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
  const goHome=()=>{setSection("movies");setQuery("");setPage(1);setSelected(null);setMobileNavOpen(false);window.scrollTo({top:0,behavior:"smooth"})};
  const switchSection=(next:Section)=>{setSection(next);setPage(1);setSelected(null);setMobileNavOpen(false);window.scrollTo({top:0,behavior:"smooth"})};

  return <SidebarProvider style={{"--sidebar-width":"14.5rem"} as CSSProperties}>
    <Sidebar collapsible="offcanvas" className="index-sidebar">
      <SidebarHeader className="brand-header"><button type="button" className="brand-home" onClick={goHome} aria-label="На главную — Ка4алка Онл@йн"><span className="brand-mark"><Clapperboard size={22}/></span><span className="brand-copy"><strong>Ка4алка</strong><span>Онл@йн</span></span></button></SidebarHeader>
      <SidebarContent className="nav-content"><p className="nav-caption">КАТАЛОГ</p><SidebarMenu>
        <SidebarMenuItem><SidebarMenuButton isActive={section==="movies"} onClick={()=>switchSection("movies")}><Film size={19}/><span>Фильмы</span></SidebarMenuButton></SidebarMenuItem>
        <SidebarMenuItem><SidebarMenuButton isActive={section==="series"} onClick={()=>switchSection("series")}><Tv size={19}/><span>Сериалы</span></SidebarMenuButton></SidebarMenuItem>
      </SidebarMenu></SidebarContent>
    </Sidebar>
    <SidebarInset className="index-main">
      <header className="topbar"><button type="button" className="mobile-menu" aria-label="Открыть разделы" aria-expanded={mobileNavOpen} aria-controls="mobile-navigation" onClick={()=>setMobileNavOpen(true)}><PanelLeft size={21}/></button><div className="topbar-name">Каталог <span>/</span> {section==="movies"?"Фильмы":"Сериалы"}</div><div className="topbar-state">{catalogBusy?"Обновляем каталог…":catalogState}</div></header>
      <div className="workspace"><div className="workspace-head"><div><p className="eyebrow">КИНОТЕКА</p><h1>{section==="movies"?"Фильмы":"Сериалы"}</h1><p className="lead">Найди, что посмотреть сегодня</p></div><div className="result-count">{countLabel(items.length,section)}</div></div>
        <div className="search-wrap"><Search size={20} aria-hidden="true"/><Input value={query} onChange={event=>{setQuery(event.target.value);setPage(1)}} placeholder={section==="movies"?"Название фильма, год…":"Название сериала, год…"} aria-label="Поиск по каталогу"/></div>
        <div className="content-label"><span>{query?"Результаты поиска":"Недавно добавлены"}</span><span className="hairline"/></div>
        {items.length?<div className="poster-grid">{items.map(item=><button type="button" className="poster-card" key={item.id} onClick={()=>openMedia(item)} aria-label={`Открыть ${item.title}`}><PosterImage key={item.poster??item.id} item={item} className="poster-frame" lazy rating/><span className="poster-title">{item.title}</span><span className="poster-meta">{item.year||"Год неизвестен"}</span></button>)}</div>:<div className="empty-state"><Search size={28}/><h2>Ничего не нашлось</h2><p>Попробуй другое название или убери год из запроса.</p></div>}
        {!query&&hasMore&&<button className="more-button catalog-more" onClick={()=>setPage(value=>value+1)} disabled={catalogBusy}>Показать ещё</button>}
      </div>
    </SidebarInset>
    <Sheet open={mobileNavOpen} onOpenChange={setMobileNavOpen}><SheetContent id="mobile-navigation" className="mobile-nav-sheet" side="left"><SheetHeader><SheetTitle>Ка4алка Онл@йн</SheetTitle><SheetDescription>Каталог фильмов и сериалов</SheetDescription></SheetHeader><nav className="mobile-nav-list" aria-label="Разделы каталога"><button type="button" aria-current={section==="movies"?"page":undefined} onClick={()=>switchSection("movies")}><Film size={20}/>Фильмы</button><button type="button" aria-current={section==="series"?"page":undefined} onClick={()=>switchSection("series")}><Tv size={20}/>Сериалы</button></nav></SheetContent></Sheet>
    <Sheet open={selected!==null} onOpenChange={open=>{if(!open)setSelected(null)}}><SheetContent className="detail-sheet" side="right">{selected&&<div className="detail-scroll"><SheetHeader className="detail-header"><p className="eyebrow">{selected.section==="movies"?"ФИЛЬМ":"СЕРИАЛ"} · {selected.year}</p><SheetTitle>{selected.title}</SheetTitle><SheetDescription>{selected.originalTitle&&selected.originalTitle!==selected.title?selected.originalTitle:"Подробности"}</SheetDescription></SheetHeader><div className="detail-top"><PosterImage key={selected.poster??selected.id} item={selected} className="detail-poster"/><div className="detail-facts"><div className="rating">Кинопоиск <strong>{selected.kinopoisk||"—"}</strong></div><div className="rating">IMDb <strong>{selected.imdb||"—"}</strong></div></div></div><p className="detail-description">{selected.description||"Описание скоро появится."}</p><a className="source-link" href={selected.pageUrl} target="_blank" rel="noreferrer">Страница в каталоге <ArrowUpRight size={17}/></a><ReleaseList key={selected.id} items={releases} error={releaseError}/></div>}</SheetContent></Sheet>
  </SidebarProvider>;
}

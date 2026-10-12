import type { Release } from "./release";

// Share concurrent searches and briefly reuse useful replies. Empty/failed searches retry immediately.
export function createReleaseLookup(now=()=>Date.now(),ttl=30_000,limit=64) {
  const saved=new Map<string,{expires:number,items:Release[]}>();
  const pending=new Map<string,Promise<Release[]>>();
  return function lookup(key:string,load:()=>Promise<Release[]>):Promise<Release[]> {
    const cached=saved.get(key);
    if(cached&&cached.expires>now())return Promise.resolve(cached.items);
    saved.delete(key);
    const active=pending.get(key);if(active)return active;
    const task=Promise.resolve().then(load).then(items=>{
      if(items.length) {
        for(const [name,entry] of saved)if(entry.expires<=now())saved.delete(name);
        while(saved.size>=limit)saved.delete(saved.keys().next().value!);
        saved.set(key,{expires:now()+ttl,items});
      }
      return items;
    });
    if(pending.size<limit)pending.set(key,task);
    const clear=()=>{if(pending.get(key)===task)pending.delete(key);};
    task.then(clear,clear);return task;
  };
}

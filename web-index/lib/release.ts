// Kept apart from the D1 store so the standalone relay can share release code.
export type Release = { id:string; mediaId:string; title:string; source:string; via?:string|null; pageUrl:string|null; torrentUrl:string|null; size:number|null; seeds:number|null; quality:string|null; season:number|null; episode:number|null; indexedAt?:number };

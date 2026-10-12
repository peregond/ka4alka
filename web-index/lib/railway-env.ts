import { railwayDb } from "./railway-db.mjs";

export const env = {
  get DB(): D1Database { return railwayDb() as unknown as D1Database; },
  get TMDB_READ_TOKEN() { return process.env.TMDB_READ_TOKEN; },
  get TVDB_API_KEY() { return process.env.TVDB_API_KEY; },
};

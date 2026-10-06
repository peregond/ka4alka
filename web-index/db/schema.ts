import { index, integer, sqliteTable, text } from "drizzle-orm/sqlite-core";

export const media = sqliteTable("media", {
  id: text("id").primaryKey(),
  section: text("section").notNull(),
  title: text("title").notNull(),
  titleSearch: text("title_search").notNull(),
  originalTitle: text("original_title"),
  originalSearch: text("original_search").notNull().default(""),
  year: integer("year").notNull().default(0),
  poster: text("poster"),
  pageUrl: text("page_url").notNull(),
  description: text("description"),
  kinopoisk: text("kinopoisk"),
  imdb: text("imdb"),
  catalogRank: integer("catalog_rank"),
  indexedAt: integer("indexed_at").notNull(),
}, table => [
  index("idx_media_section_rank").on(table.section, table.catalogRank),
  index("idx_media_section_title").on(table.section, table.titleSearch),
]);

export const releases = sqliteTable("releases", {
  id: text("id").primaryKey(),
  mediaId: text("media_id").notNull(),
  title: text("title").notNull(),
  source: text("source").notNull(),
  pageUrl: text("page_url"),
  torrentUrl: text("torrent_url"),
  size: integer("size"),
  seeds: integer("seeds"),
  quality: text("quality"),
  season: integer("season"),
  episode: integer("episode"),
  indexedAt: integer("indexed_at").notNull(),
}, table => [index("idx_releases_media_indexed").on(table.mediaId, table.indexedAt)]);

export const syncState = sqliteTable("sync_state", {
  key: text("key").primaryKey(),
  refreshedAt: integer("refreshed_at").notNull(),
});

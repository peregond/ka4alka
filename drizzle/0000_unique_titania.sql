CREATE TABLE `media` (
	`id` text PRIMARY KEY NOT NULL,
	`section` text NOT NULL,
	`title` text NOT NULL,
	`title_search` text NOT NULL,
	`original_title` text,
	`original_search` text DEFAULT '' NOT NULL,
	`year` integer DEFAULT 0 NOT NULL,
	`poster` text,
	`page_url` text NOT NULL,
	`description` text,
	`kinopoisk` text,
	`imdb` text,
	`catalog_rank` integer,
	`indexed_at` integer NOT NULL
);
--> statement-breakpoint
CREATE INDEX `idx_media_section_rank` ON `media` (`section`,`catalog_rank`);--> statement-breakpoint
CREATE INDEX `idx_media_section_title` ON `media` (`section`,`title_search`);--> statement-breakpoint
CREATE TABLE `releases` (
	`id` text PRIMARY KEY NOT NULL,
	`media_id` text NOT NULL,
	`title` text NOT NULL,
	`source` text NOT NULL,
	`page_url` text,
	`torrent_url` text,
	`size` integer,
	`seeds` integer,
	`quality` text,
	`season` integer,
	`episode` integer,
	`indexed_at` integer NOT NULL
);
--> statement-breakpoint
CREATE INDEX `idx_releases_media_indexed` ON `releases` (`media_id`,`indexed_at`);--> statement-breakpoint
CREATE TABLE `sync_state` (
	`key` text PRIMARY KEY NOT NULL,
	`refreshed_at` integer NOT NULL
);

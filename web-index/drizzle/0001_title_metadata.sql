CREATE TABLE `title_metadata` (
	`section` text NOT NULL,
	`imdb_id` text NOT NULL,
	`payload` text NOT NULL,
	`updated_at` integer NOT NULL,
	PRIMARY KEY(`section`, `imdb_id`)
);

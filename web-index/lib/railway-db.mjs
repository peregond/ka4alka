import { DatabaseSync } from "node:sqlite";
import { mkdirSync, readFileSync, readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

let instance;
// Only the D1 statement operations used by this application's index store.
// This is a separate Railway SQLite database, never the hosted Sites D1.
export function railwayDb() {
  if (instance) return instance;
  const path = process.env.DATABASE_PATH || resolve("data/catalog.sqlite");
  mkdirSync(dirname(path), { recursive: true });
  const db = new DatabaseSync(path);
  db.exec("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
  db.exec("CREATE TABLE IF NOT EXISTS railway_migrations (name TEXT PRIMARY KEY NOT NULL)");
  try {
    for (const name of readdirSync("drizzle").filter(name => name.endsWith(".sql")).sort()) {
      if (db.prepare("SELECT name FROM railway_migrations WHERE name=?").get(name)) continue;
      db.exec("BEGIN IMMEDIATE");
      try {
        db.exec(readFileSync(resolve("drizzle", name), "utf8"));
        db.prepare("INSERT INTO railway_migrations(name) VALUES(?)").run(name);
        db.exec("COMMIT");
      } catch (error) { db.exec("ROLLBACK"); throw error; }
    }
  } catch (error) { db.close(); throw error; }
  class Statement {
    constructor(sql, args = []) { this.sql = sql; this.args = args; }
    bind(...args) { return new Statement(this.sql, args); }
    execute() {
      const result = db.prepare(this.sql).run(...this.args);
      return { success: true, results: [], meta: { changes: Number(result.changes), last_row_id: Number(result.lastInsertRowid) } };
    }
    async run() { return this.execute(); }
    async first(column) {
      const row = db.prepare(this.sql).get(...this.args);
      return column ? row?.[column] ?? null : row ?? null;
    }
    async all() { return { success: true, results: db.prepare(this.sql).all(...this.args), meta: {} }; }
    async raw() {
      const query = db.prepare(this.sql); query.setReturnArrays(true);
      return query.all(...this.args);
    }
  }
  instance = {
    prepare: sql => new Statement(sql),
    async batch(statements) {
      db.exec("BEGIN IMMEDIATE");
      try { const results = statements.map(statement => statement.execute()); db.exec("COMMIT"); return results; }
      catch (error) { db.exec("ROLLBACK"); throw error; }
    },
  };
  return instance;
}

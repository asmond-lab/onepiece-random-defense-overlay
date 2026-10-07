CREATE TABLE IF NOT EXISTS gameplay_v3_learning_job (
  singleton INTEGER PRIMARY KEY CHECK(singleton=1), cursor INTEGER NOT NULL DEFAULT 0,
  lease_token TEXT, lease_until INTEGER NOT NULL DEFAULT 0
);
INSERT OR IGNORE INTO gameplay_v3_learning_job(singleton) VALUES(1);
CREATE TABLE IF NOT EXISTS gameplay_v3_learning_sessions (
  match_id TEXT PRIMARY KEY, map_hash TEXT NOT NULL, difficulty TEXT NOT NULL,
  map_version TEXT NOT NULL, goal TEXT NOT NULL, clear INTEGER NOT NULL,
  adherence REAL, expires_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS gameplay_v3_learning_cohort
  ON gameplay_v3_learning_sessions(map_hash,difficulty,expires_at);
CREATE INDEX IF NOT EXISTS gameplay_v3_learning_expiry ON gameplay_v3_learning_sessions(expires_at);
CREATE TABLE IF NOT EXISTS gameplay_v3_learning_units (
  match_id TEXT NOT NULL, unit TEXT NOT NULL, PRIMARY KEY(match_id,unit)
);
CREATE TABLE IF NOT EXISTS gameplay_v3_dirty_cohorts (
  map_hash TEXT NOT NULL, difficulty TEXT NOT NULL, PRIMARY KEY(map_hash,difficulty)
);

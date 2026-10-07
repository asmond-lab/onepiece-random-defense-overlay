CREATE TABLE IF NOT EXISTS clear_rollups (
  bucket_start TEXT NOT NULL,
  difficulty TEXT NOT NULL,
  goal_tier_family TEXT NOT NULL,
  clear_count INTEGER NOT NULL CHECK(clear_count > 0),
  PRIMARY KEY (bucket_start, difficulty, goal_tier_family)
);

CREATE INDEX IF NOT EXISTS clear_rollups_bucket_start
  ON clear_rollups(bucket_start);

CREATE TABLE IF NOT EXISTS dashboard_state (
  singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
  recent_clear_at TEXT NOT NULL
);

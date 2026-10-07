-- Generic cohorts and their primary keys remain unchanged.
ALTER TABLE gameplay_v3_learning_sessions ADD COLUMN bullet_guide INTEGER NOT NULL DEFAULT 0 CHECK(bullet_guide IN (0,1));
CREATE INDEX gameplay_v3_bullet_cohort ON gameplay_v3_learning_sessions(map_hash,difficulty,bullet_guide,expires_at);
CREATE TABLE gameplay_v3_profile_stats (
  map_script_sha256 TEXT NOT NULL, difficulty TEXT NOT NULL,
  profile TEXT NOT NULL CHECK(profile='bullet-guide-1'),
  stats TEXT NOT NULL, updated_at INTEGER NOT NULL,
  PRIMARY KEY(map_script_sha256,difficulty,profile)
);
-- Backfill retained whole matches with the existing bounded job. Old generic
-- contributions cannot prove historical mode/guide eligibility.
-- The NEW worker resets under its lease. An old cron invocation between migration
-- and deployment cannot accidentally advance past the required profile backfill.
ALTER TABLE gameplay_v3_learning_job ADD COLUMN bullet_backfill INTEGER NOT NULL DEFAULT 1 CHECK(bullet_backfill IN (0,1));

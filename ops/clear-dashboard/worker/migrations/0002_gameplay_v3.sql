-- Additive only. Identity/hash tombstones outlive raw payloads, preventing replay
-- after the 30-day raw retention window. No player or installation identity.
CREATE TABLE IF NOT EXISTS gameplay_v3_matches (
  match_id TEXT PRIMARY KEY,
  metadata_hash TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS gameplay_v3_packets (
  packet_id TEXT PRIMARY KEY,
  match_id TEXT NOT NULL REFERENCES gameplay_v3_matches(match_id),
  chunk_index INTEGER NOT NULL CHECK(chunk_index >= 0),
  content_hash TEXT NOT NULL,
  received_at INTEGER NOT NULL,
  raw_expired INTEGER NOT NULL DEFAULT 0 CHECK(raw_expired IN (0, 1)),
  UNIQUE(match_id, chunk_index)
);
CREATE INDEX IF NOT EXISTS gameplay_v3_packets_expiry ON gameplay_v3_packets(raw_expired, received_at);
CREATE TABLE IF NOT EXISTS gameplay_v3_events (
  match_id TEXT NOT NULL,
  sequence INTEGER NOT NULL CHECK(sequence > 0),
  packet_id TEXT NOT NULL REFERENCES gameplay_v3_packets(packet_id),
  content_hash TEXT NOT NULL,
  PRIMARY KEY(match_id, sequence)
);
CREATE TABLE IF NOT EXISTS gameplay_v3_raw (
  cursor INTEGER PRIMARY KEY AUTOINCREMENT,
  packet_id TEXT NOT NULL UNIQUE REFERENCES gameplay_v3_packets(packet_id),
  received_at INTEGER NOT NULL,
  payload TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS gameplay_v3_raw_expiry ON gameplay_v3_raw(received_at);
CREATE TABLE IF NOT EXISTS gameplay_v3_live_stats (
  map_script_sha256 TEXT NOT NULL,
  difficulty TEXT NOT NULL,
  stats TEXT NOT NULL,
  updated_at INTEGER NOT NULL,
  PRIMARY KEY(map_script_sha256, difficulty)
);

-- Guards run inside D1's atomic batch, including concurrent uploads. Read-then-write
-- checks alone would race. Identical retries do not update receipt time or payload.
CREATE TRIGGER IF NOT EXISTS gameplay_v3_match_conflict
BEFORE INSERT ON gameplay_v3_matches
WHEN EXISTS(SELECT 1 FROM gameplay_v3_matches WHERE match_id = NEW.match_id AND metadata_hash <> NEW.metadata_hash)
BEGIN SELECT RAISE(ABORT, 'v3_conflict'); END;
CREATE TRIGGER IF NOT EXISTS gameplay_v3_packet_conflict
BEFORE INSERT ON gameplay_v3_packets
WHEN EXISTS(SELECT 1 FROM gameplay_v3_packets WHERE
  (packet_id = NEW.packet_id AND content_hash <> NEW.content_hash) OR
  (match_id = NEW.match_id AND chunk_index = NEW.chunk_index AND packet_id <> NEW.packet_id))
BEGIN SELECT RAISE(ABORT, 'v3_conflict'); END;
CREATE TRIGGER IF NOT EXISTS gameplay_v3_event_conflict
BEFORE INSERT ON gameplay_v3_events
WHEN EXISTS(SELECT 1 FROM gameplay_v3_events WHERE match_id = NEW.match_id AND sequence = NEW.sequence
  AND (content_hash <> NEW.content_hash OR packet_id <> NEW.packet_id))
BEGIN SELECT RAISE(ABORT, 'v3_conflict'); END;

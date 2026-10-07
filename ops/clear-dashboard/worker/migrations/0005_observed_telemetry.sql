-- Observations are distinct from confirmed gameplay and never train v3 recommendations.
CREATE TABLE IF NOT EXISTS telemetry_v4_packets (
  packet_id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  app_version TEXT NOT NULL,
  source TEXT NOT NULL CHECK(source IN ('live', 'synthetic-validation')),
  received_at INTEGER NOT NULL,
  content_hash TEXT NOT NULL,
  payload TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS telemetry_v4_packets_expiry ON telemetry_v4_packets(received_at);
CREATE INDEX IF NOT EXISTS telemetry_v4_packets_source_receipt ON telemetry_v4_packets(source, received_at);
-- Runs inside the atomic D1 batch; concurrent conflicting retries cannot acknowledge.
CREATE TRIGGER IF NOT EXISTS telemetry_v4_packet_conflict
BEFORE INSERT ON telemetry_v4_packets
WHEN EXISTS(SELECT 1 FROM telemetry_v4_packets WHERE packet_id = NEW.packet_id AND content_hash <> NEW.content_hash)
BEGIN SELECT RAISE(ABORT, 'v4_packet_conflict'); END;

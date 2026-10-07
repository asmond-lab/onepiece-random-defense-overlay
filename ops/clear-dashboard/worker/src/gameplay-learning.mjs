import { BULLET_PROFILE, validatePacket, validateStats } from "./gameplay-validate.mjs";

// The Python offline collector is a parity oracle, not an operational dependency.
export function reconstructSession(packets) {
  if (!packets.length || packets.some(p => !validatePacket(p))) return null;
  const chunks = [...packets].sort((a, b) => a.chunkIndex - b.chunkIndex);
  const first = chunks[0];
  if (chunks.some((p, i) => p.chunkIndex !== i || p.matchId !== first.matchId ||
      p.mapScriptSha256 !== first.mapScriptSha256 || p.difficulty !== first.difficulty ||
      p.mapVersion !== first.mapVersion)) return null;
  const events = chunks.flatMap(p => p.events).sort((a, b) => a.sequence - b.sequence);
  if (events.some((e, i) => e.sequence !== i + 1 || i > 0 &&
      (e.elapsedMs < events[i - 1].elapsedMs || e.recognitionRevision < events[i - 1].recognitionRevision)) ||
      events[0].kind !== "observation" || events[0].round !== 1 || first.difficulty === "unknown") return null;
  const terminal = events.at(-1);
  if (terminal.kind !== "outcome" || events.filter(e => e.kind === "outcome").length !== 1) return null;
  const observedWipe = terminal.evidence === "observation-only" && terminal.outcome === "fail" &&
    terminal.outcomeSource === "unitWipe" && events.slice(0, -1).some(e =>
      e.kind === "observation" && e.evidence === "native-observed" && Object.values(e.inventory).some(n => n > 0));
  if (!["native-observed", "user-confirmed"].includes(terminal.evidence) && !observedWipe) return null;
  if (!(terminal.outcome === "clear" && terminal.outcomeSource === "mapSettlement") &&
      !(terminal.outcome === "fail" && ["unitWipe", "mapSettlement"].includes(terminal.outcomeSource))) return null;
  if (terminal.goalUnitIds.length !== 1 || terminal.goalUnitIds[0] === "unknown") return null;
  const goal = terminal.goalUnitIds[0];
  const recommendations = events.filter(e => e.kind === "recommendation");
  if (recommendations.some(e => e.goalUnitIds.length !== 1 || e.goalUnitIds[0] !== goal)) return null;
  let roster = terminal.inventory;
  if (!Object.values(roster).some(n => n > 0))
    roster = [...events].reverse().find(e => e.kind === "observation" && e.evidence !== "unknown" &&
      Object.values(e.inventory).some(n => n > 0))?.inventory ?? {};
  const owned = Object.keys(roster).filter(id => roster[id] > 0);
  const targets = [...new Set(recommendations.filter(e => e.evidence === "recommendation")
    .map(e => e.targetUnitId).filter(Boolean))];
  return { hash: first.mapScriptSha256, difficulty: first.difficulty, version: first.mapVersion,
    bulletGuide: goal === "rawcode:180h" && events.every(e => e.mode === "Guide" && e.guideNumber === 1),
    goal, clear: terminal.outcome === "clear", owned,
    adherence: targets.length ? targets.filter(id => owned.includes(id)).length / targets.length : null };
}

export function wilson(hits, trials) {
  const ratio = hits / trials, denominator = 1 + 1.96 ** 2 / trials;
  const center = (ratio + 1.96 ** 2 / (2 * trials)) / denominator;
  const margin = 1.96 / denominator * Math.sqrt((ratio * (1 - ratio) / trials + 1.96 ** 2 / (4 * trials ** 2)));
  return [center - margin, center + margin];
}

export function buildStats(goals, units, difficulty, generatedAt, profile = null) {
  const total = goals.reduce((n, g) => n + g.plays, 0);
  const stats = { schemaVersion: 1, generatedAt, totalRecords: total, labeledRecords: total,
    goals: {}, weights: {}, goalWeights: {}, difficulties: { [difficulty]: total } };
  for (const g of goals) {
    const weights = {};
    if (g.plays >= 30 && g.clears > 0 && g.plays > g.clears) {
      for (const u of units.filter(u => u.goal === g.goal && u.unit !== g.goal)) {
        const [cl, ch] = wilson(u.clears, g.clears);
        const [fl, fh] = wilson(u.fails, g.plays - g.clears);
        const gap = cl > fh ? cl - fh : fl > ch ? ch - fl : 0;
        if (gap) weights[u.unit] = Math.max(-0.1, Math.min(0.1, Number(gap.toFixed(3))));
      }
    }
    stats.goals[g.goal] = { plays: g.plays, labeled: g.plays, clears: g.clears,
      adherenceMean: g.adherence === null ? null : Number(g.adherence.toFixed(3)),
      failHeavyUnits: Object.keys(weights).filter(id => weights[id] < 0).sort() };
    if (Object.keys(weights).length) stats.goalWeights[g.goal] = weights;
  }
  if (!validateStats(stats, difficulty, profile)) throw new Error("Invalid derived gameplay cohort");
  return stats;
}

const RETENTION = 30 * 86400000;
const PAGE = 32;
const MAX_MATCH_BYTES = 8 * 1024 * 1024;
const MAX_MATCH_PACKETS = 2048;

// One invocation advances a durable cursor by at most 32 packets. Revisit the whole
// affected match so delayed/out-of-order chunks never become a partial training sample.
export async function aggregateGameplay(env) {
  const now = (env.CLOCK?.() ?? new Date()).getTime(), cutoff = now - RETENTION;
  const token = crypto.randomUUID();
  await env.DB.prepare(`UPDATE gameplay_v3_learning_job SET lease_token = ?, lease_until = ?
    WHERE singleton = 1 AND lease_until <= ?`).bind(token, now + 600000, now).run();
  const job = await env.DB.prepare("SELECT * FROM gameplay_v3_learning_job WHERE singleton = 1").first();
  if (job.lease_token !== token) return { busy: true };
  try {
    if (job.bullet_backfill === 1) {
      await env.DB.prepare("UPDATE gameplay_v3_learning_job SET cursor=0,bullet_backfill=0 WHERE singleton=1 AND lease_token=?")
        .bind(token).run();
      job.cursor = 0;
    }
    await env.DB.batch([
      env.DB.prepare(`INSERT OR IGNORE INTO gameplay_v3_dirty_cohorts
        SELECT map_hash, difficulty FROM gameplay_v3_learning_sessions WHERE expires_at <= ?`).bind(now),
      env.DB.prepare("DELETE FROM gameplay_v3_learning_units WHERE match_id IN (SELECT match_id FROM gameplay_v3_learning_sessions WHERE expires_at <= ?)").bind(now),
      env.DB.prepare("DELETE FROM gameplay_v3_learning_sessions WHERE expires_at <= ?").bind(now),
    ]);
    const page = await env.DB.prepare(`SELECT r.cursor, p.match_id FROM gameplay_v3_raw r
      JOIN gameplay_v3_packets p ON p.packet_id = r.packet_id
      WHERE r.cursor > ? AND r.received_at > ? ORDER BY r.cursor LIMIT ?`).bind(job.cursor, cutoff, PAGE).all();
    for (const match of new Set(page.results.map(r => r.match_id))) {
      const size = await env.DB.prepare(`SELECT count(*) AS n, sum(length(CAST(r.payload AS BLOB))) AS bytes,
        min(r.received_at) AS first_at FROM gameplay_v3_raw r JOIN gameplay_v3_packets p ON p.packet_id = r.packet_id
        WHERE p.match_id = ? AND r.received_at > ?`).bind(match, cutoff).first();
      let session = null;
      if (size.n <= MAX_MATCH_PACKETS && size.bytes <= MAX_MATCH_BYTES) {
        const raw = await env.DB.prepare(`SELECT r.payload FROM gameplay_v3_raw r
          JOIN gameplay_v3_packets p ON p.packet_id = r.packet_id
          WHERE p.match_id = ? AND r.received_at > ? ORDER BY p.chunk_index`).bind(match, cutoff).all();
        session = reconstructSession(raw.results.map(r => JSON.parse(r.payload)));
      }
      const statements = [
        env.DB.prepare(`INSERT OR IGNORE INTO gameplay_v3_dirty_cohorts
          SELECT map_hash, difficulty FROM gameplay_v3_learning_sessions WHERE match_id = ?`).bind(match),
        env.DB.prepare("DELETE FROM gameplay_v3_learning_units WHERE match_id = ?").bind(match),
        env.DB.prepare("DELETE FROM gameplay_v3_learning_sessions WHERE match_id = ?").bind(match),
      ];
      if (session) {
        statements.push(env.DB.prepare(`INSERT INTO gameplay_v3_learning_sessions
          (match_id,map_hash,difficulty,map_version,goal,clear,adherence,expires_at,bullet_guide) VALUES(?,?,?,?,?,?,?,?,?)`)
          .bind(match, session.hash, session.difficulty, session.version, session.goal,
            Number(session.clear), session.adherence, size.first_at + RETENTION, Number(session.bulletGuide)));
        statements.push(...session.owned.map(id => env.DB.prepare(
          "INSERT INTO gameplay_v3_learning_units(match_id,unit) VALUES(?,?)").bind(match, id)));
        statements.push(env.DB.prepare("INSERT OR IGNORE INTO gameplay_v3_dirty_cohorts VALUES(?,?)")
          .bind(session.hash, session.difficulty));
      }
      await env.DB.batch(statements);
    }
    if (page.results.length) await env.DB.prepare("UPDATE gameplay_v3_learning_job SET cursor = ? WHERE singleton = 1 AND lease_token = ?")
      .bind(page.results.at(-1).cursor, token).run();
    // Never publish while a capture backlog is only partly indexed.
    const more = await env.DB.prepare("SELECT cursor FROM gameplay_v3_raw WHERE cursor > ? AND received_at > ? LIMIT 1")
      .bind(page.results.at(-1)?.cursor ?? job.cursor, cutoff).first();
    if (more) return { processed: page.results.length, pending: true };
    const dirty = await env.DB.prepare("SELECT * FROM gameplay_v3_dirty_cohorts ORDER BY map_hash,difficulty LIMIT 8").all();
    for (const c of dirty.results) {
      // Both namespaces are projected at the same complete cursor boundary.
      const publication = [];
      for (const profile of [null, BULLET_PROFILE]) {
        const predicate = profile ? " AND bullet_guide=1" : "";
        const versions = await env.DB.prepare(`SELECT DISTINCT map_version FROM gameplay_v3_learning_sessions
          WHERE map_hash = ? AND difficulty = ? AND expires_at > ?${predicate} LIMIT 2`).bind(c.map_hash, c.difficulty, now).all();
        const goals = await env.DB.prepare(`SELECT goal,count(*) AS plays,sum(clear) AS clears,avg(adherence) AS adherence
          FROM gameplay_v3_learning_sessions WHERE map_hash = ? AND difficulty = ? AND expires_at > ?${predicate}
          GROUP BY goal ORDER BY goal LIMIT 513`).bind(c.map_hash, c.difficulty, now).all();
        const units = await env.DB.prepare(`SELECT s.goal,u.unit,sum(s.clear) AS clears,sum(1-s.clear) AS fails
          FROM gameplay_v3_learning_sessions s JOIN gameplay_v3_learning_units u ON u.match_id=s.match_id
          WHERE s.map_hash=? AND s.difficulty=? AND s.expires_at>?${predicate} GROUP BY s.goal,u.unit ORDER BY s.goal,u.unit LIMIT 262145`)
          .bind(c.map_hash, c.difficulty, now).all();
        if (goals.results.length > 512 || units.results.length > 262144) throw new Error("Gameplay cohort exceeds supported catalog bounds");
        const stats = buildStats(versions.results.length <= 1 ? goals.results : [],
          versions.results.length <= 1 ? units.results : [], c.difficulty, new Date(now).toISOString(), profile);
        if (profile) {
          publication.push(env.DB.prepare(`INSERT INTO gameplay_v3_profile_stats(map_script_sha256,difficulty,profile,stats,updated_at)
            VALUES(?,?,?,?,?) ON CONFLICT(map_script_sha256,difficulty,profile)
            DO UPDATE SET stats=excluded.stats,updated_at=excluded.updated_at`)
            .bind(c.map_hash, c.difficulty, profile, JSON.stringify(stats), now));
        } else {
          publication.push(env.DB.prepare(`INSERT INTO gameplay_v3_live_stats(map_script_sha256,difficulty,stats,updated_at)
            VALUES(?,?,?,?) ON CONFLICT(map_script_sha256,difficulty)
            DO UPDATE SET stats=excluded.stats,updated_at=excluded.updated_at`)
            .bind(c.map_hash, c.difficulty, JSON.stringify(stats), now));
        }
      }
      publication.push(env.DB.prepare("DELETE FROM gameplay_v3_dirty_cohorts WHERE map_hash=? AND difficulty=?").bind(c.map_hash, c.difficulty));
      await env.DB.batch(publication);
    }
    return { processed: page.results.length, published: dirty.results.length };
  } finally {
    await env.DB.prepare("UPDATE gameplay_v3_learning_job SET lease_until=0 WHERE singleton=1 AND lease_token=?").bind(token).run();
  }
}

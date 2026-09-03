const COMPONENTS_V2_FLAG = 1 << 15;
const ACCENT = {
  ok: 0x3ddc84,
  empty: 0x8b93a7,
  stale: 0xf0b446,
  error: 0xe5484d,
};
const STATUS_LABEL = {
  ok: "정상",
  empty: "수집 대기",
  stale: "지연",
  error: "연결 오류",
};

export function renderDiscordPayload(snapshot) {
  const windows = snapshot.windows;
  const components = [
    text(`# 원랜디 클리어 현황\n**상태:** ${STATUS_LABEL[snapshot.status]}`),
    separator(),
    text(
      `### 기간별 클리어\n`
      + `최근 1시간 **${windows["1h"].clearCount}** · `
      + `24시간 **${windows["24h"].clearCount}** · `
      + `7일 **${windows["7d"].clearCount}**`,
    ),
    separator(),
    text(
      `### 7일 분포\n`
      + `**난이도**  ${formatDistribution(snapshot.distributions.byDifficulty)}\n`
      + `**목표**  ${formatDistribution(snapshot.distributions.byGoal)}`,
    ),
    separator(),
    text(renderFreshness(snapshot)),
  ];

  return {
    flags: COMPONENTS_V2_FLAG,
    components: [{
      type: 17,
      accent_color: ACCENT[snapshot.status],
      components,
    }],
  };
}

function renderFreshness(snapshot) {
  const recent = snapshot.recentClearAt === null
    ? "없음"
    : `<t:${Math.floor(Date.parse(snapshot.recentClearAt) / 1000)}:R>`;
  const refreshed = `<t:${Math.floor(Date.parse(snapshot.refreshedAt) / 1000)}:R>`;
  const delay = snapshot.dataDelaySeconds === null
    ? "계산 불가"
    : formatDuration(snapshot.dataDelaySeconds);
  const error = snapshot.error === null ? "" : `\n**오류 코드:** \`${snapshot.error.code}\``;
  return `### 신선도\n최근 클리어 ${recent} · 데이터 지연 **${delay}**\n마지막 갱신 ${refreshed}${error}`;
}

function formatDistribution(entries) {
  if (entries.length === 0) return "데이터 없음";
  return entries.map((entry) => `${entry.key} ${entry.clearCount}`).join(" · ");
}

function formatDuration(seconds) {
  if (seconds < 60) return `${Math.floor(seconds)}초`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}분`;
  return `${Math.floor(seconds / 3600)}시간`;
}

function text(content) {
  return { type: 10, content };
}

function separator() {
  return { type: 14, divider: true, spacing: 1 };
}

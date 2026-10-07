import {
  configurationErrorSnapshot,
  connectionErrorSnapshot,
  parseSnapshot,
} from "./snapshot.mjs";

export async function pollDashboard(options) {
  const now = options.now();
  let endpoint;
  try {
    endpoint = new URL(options.endpoint);
  } catch {
    return configurationErrorSnapshot(now);
  }
  if (endpoint.protocol !== "https:" || !options.allowedHosts.includes(endpoint.hostname)) {
    return configurationErrorSnapshot(now);
  }

  try {
    const response = await options.fetchImpl(endpoint, {
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${options.readKey}`,
      },
      signal: AbortSignal.timeout(5000),
    });
    if (!response.ok) return connectionErrorSnapshot(now, `HTTP ${response.status}`);
    return parseSnapshot(await response.json());
  } catch (error) {
    if (error instanceof Error) return connectionErrorSnapshot(now, error.name);
    throw error;
  }
}

const SECURITY_HEADERS = {
  "Content-Security-Policy":
    "default-src 'none'; style-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
  "Strict-Transport-Security": "max-age=31536000; includeSubDomains",
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "strict-origin-when-cross-origin",
  "Permissions-Policy": "camera=(), microphone=(), geolocation=()",
  "X-Frame-Options": "DENY",
};

function withSecurity(headers) {
  const out = new Headers(headers);
  for (const [k, v] of Object.entries(SECURITY_HEADERS)) out.set(k, v);
  return out;
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.protocol === "http:" && url.hostname.endsWith("randypick.com")) {
      return new Response(null, {
        status: 301,
        headers: withSecurity({ Location: "https://randypick.com" + url.pathname + url.search }),
      });
    }

    if (url.hostname === "www.randypick.com") {
      return new Response(null, {
        status: 301,
        headers: withSecurity({ Location: "https://randypick.com" + url.pathname + url.search }),
      });
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method Not Allowed", {
        status: 405,
        headers: withSecurity({ Allow: "GET, HEAD", "Content-Type": "text/plain; charset=utf-8" }),
      });
    }

    if (request.method === "GET" && url.pathname === "/healthz") {
      return new Response(JSON.stringify({ status: "ok", service: "randypick-web" }), {
        status: 200,
        headers: withSecurity({ "Content-Type": "application/json", "Cache-Control": "no-store" }),
      });
    }

    const res = await env.ASSETS.fetch(request);
    const headers = withSecurity(res.headers);
    if ((res.headers.get("Content-Type") || "").startsWith("text/html")) {
      headers.set("Cache-Control", "public, max-age=0, must-revalidate");
    }
    return new Response(res.body, { status: res.status, statusText: res.statusText, headers });
  },
};

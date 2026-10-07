const root = import.meta.dir;
const server = Bun.serve({
  hostname: "127.0.0.1", port: 0,
  async fetch(request) {
    const path = decodeURIComponent(new URL(request.url).pathname);
    if (path.includes("..")) return new Response("Not found", { status: 404 });
    const file = Bun.file(root + (path === "/" ? "/index.html" : path));
    return await file.exists() ? new Response(file) : new Response("Not found", { status: 404 });
  }
});
try {
  await using browser = new Bun.WebView({ width: 1600, height: 1400 });
  await browser.navigate(server.url.href);
  console.log(await browser.evaluate(`JSON.stringify({title:document.title,images:[...document.images].every(i=>i.complete&&i.naturalWidth>0),overflow:document.documentElement.scrollWidth>innerWidth})`));
  await browser.evaluate(`document.querySelector('#references-title').scrollIntoView()`);
  await Bun.write(root + "/preview-references.png", await browser.screenshot());
  await browser.evaluate(`document.querySelector('#concept-title').scrollIntoView()`);
  await Bun.write(root + "/preview-desktop.png", await browser.screenshot());
  await browser.click('[data-state="pending"]');
  await Bun.write(root + "/preview-pending.png", await browser.screenshot());
  await browser.click('[data-state="idle"]');
  console.log("IDLE", await browser.evaluate(`document.querySelector('.stage .connected').textContent`));
  if (!await browser.evaluate(`[...document.querySelectorAll('.stage')].every(s=>!s.innerText.includes('5개 보관')&&!s.innerText.includes('2/3'))`))
    throw new Error("Idle view retains invented match progress");
  await Bun.write(root + "/preview-idle.png", await browser.screenshot());
  await browser.cdp("Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 1, mobile: false });
  await browser.evaluate(`document.querySelector('[data-state="ready"]').click()`);
  await browser.evaluate(`document.querySelector('.concept').scrollIntoView()`);
  await Bun.write(root + "/preview-mobile.png", await browser.screenshot());
  for (let i = 1; i < 3; i++) {
    await browser.evaluate(`document.querySelectorAll('.concept')[${i}].scrollIntoView()`);
    await Bun.write(root + "/preview-mobile-" + i + ".png", await browser.screenshot());
  }
  console.log("MOBILE", await browser.evaluate(`JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth,title:document.title})`));
  console.log("PREVIEW_PASS");
} finally { server.stop(true); }

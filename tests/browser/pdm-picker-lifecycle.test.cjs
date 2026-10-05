const assert = require("node:assert/strict");
const fs = require("node:fs");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const { test } = require("node:test");
const { chromium } = require("playwright");

const pickerPath = process.env.PICKER_SOURCE || path.resolve(__dirname, "../../docker/browser/pdm-picker.js");
const chromiumExecutable = process.env.PDM_CHROMIUM_EXECUTABLE || "/usr/bin/chromium";
const pickerSource = fs.readFileSync(pickerPath, "utf8");
const pickerCss = fs.readFileSync(path.resolve(__dirname, "../../docker/browser/pdm-picker.css"), "utf8");

async function createFixture() {
  const state = { pollCalls: 0, pollTimes: [], activeRequest: null, cancels: [], uploads: [], stallHeaders: 0, stallJson: 0, failCancelOnce: 0 };
  const server = http.createServer((req, res) => {
    if (req.url === "/") {
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
      res.end(`<!doctype html><html><head><meta charset="utf-8"><style>${pickerCss}</style></head><body>
        <div id="noVNC_container" style="width:100vw;height:100vh">
          <button id="fullscreen" type="button">Fullscreen</button><div id="screen"></div>
        </div>
        <script>document.querySelector('#fullscreen').addEventListener('click', () => document.querySelector('#noVNC_container').requestFullscreen())</script>
      </body></html>`);
      return;
    }
    if (req.url === "/pdm-picker/pending") {
      state.pollCalls++;
      state.pollTimes.push(Date.now());
      if (state.stallHeaders > 0) {
        state.stallHeaders--;
        res.on("close", () => {});
        return;
      }
      if (state.stallJson > 0) {
        state.stallJson--;
        res.writeHead(200, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
        res.flushHeaders();
        res.on("close", () => {});
        return;
      }
      res.setHeader("Cache-Control", "no-store");
      if (state.activeRequest) {
        res.writeHead(200, { "Content-Type": "application/json; charset=utf-8" });
        res.end(JSON.stringify(state.activeRequest));
      } else {
        res.writeHead(204);
        res.end();
      }
      return;
    }
    if (req.url.endsWith("/cancel")) {
      state.cancels.push(req.url);
      req.resume();
      if (state.failCancelOnce > 0) {
        state.failCancelOnce--;
        res.writeHead(503);
        res.end();
        return;
      }
      state.activeRequest = null;
      res.writeHead(204);
      res.end();
      return;
    }
    if (req.url.endsWith("/files")) {
      const chunks = [];
      req.on("data", chunk => chunks.push(chunk));
      req.on("end", () => {
        state.uploads.push(Buffer.concat(chunks).toString("utf8"));
        state.activeRequest = null;
        res.writeHead(204);
        res.end();
      });
      return;
    }
    res.writeHead(404);
    res.end();
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  return {
    state,
    url: `http://127.0.0.1:${server.address().port}/`,
    close: () => new Promise(resolve => server.close(resolve))
  };
}

async function createPage(browser, fixture, beforeScript = null) {
  const page = await browser.newPage();
  await page.goto(fixture.url);
  if (beforeScript) await beforeScript(page);
  await page.addScriptTag({ content: pickerSource });
  return page;
}

function request(id, nonce = `nonce-${id}`) {
  return { requestId: id, nonce, expiresAt: new Date(Date.now() + 60_000).toISOString() };
}

async function waitForPolls(fixture, count, timeout = 5000) {
  const deadline = Date.now() + timeout;
  while (fixture.state.pollCalls < count && Date.now() < deadline) {
    await new Promise(resolve => setTimeout(resolve, 25));
  }
  assert.ok(fixture.state.pollCalls >= count, `expected ${count} pending polls, got ${fixture.state.pollCalls}`);
}

test("restores one pending poll loop after persisted pagehide/pageshow", { timeout: 15000 }, async t => {
  const browser = await chromium.launch({ executablePath: chromiumExecutable, headless: true, args: ["--no-sandbox"] });
  const fixture = await createFixture();
  t.after(async () => { await browser.close(); await fixture.close(); });
  const page = await createPage(browser, fixture);
  t.after(() => page.close());
  await waitForPolls(fixture, 1);
  const start = fixture.state.pollCalls;
  await page.evaluate(() => {
    window.dispatchEvent(new PageTransitionEvent("pagehide", { persisted: true }));
    window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true }));
    window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true }));
  });
  await waitForPolls(fixture, start + 3, 4000);
  await page.waitForTimeout(600);
  assert.ok(fixture.state.pollCalls >= start + 2, `polling did not resume: ${start} -> ${fixture.state.pollCalls}`);
  const times = fixture.state.pollTimes.slice(-3);
  const gaps = times.slice(1).map((time, index) => time - times[index]);
  assert.ok(gaps.every(gap => gap >= 400), `poll loop ran more than once: ${gaps.join(", ")}ms`);
});

test("ignores a stale JSON response after pagehide and restart", { timeout: 10000 }, async t => {
  const browser = await chromium.launch({ executablePath: chromiumExecutable, headless: true, args: ["--no-sandbox"] });
  const fixture = await createFixture();
  const page = await createPage(browser, fixture, page => page.evaluate(() => {
    const browserFetch = window.fetch.bind(window);
    let delayFirstBody = true;
    window.__releaseStalePickerJson = null;
    window.fetch = (url, options) => {
      if (delayFirstBody && url.endsWith("/pending")) {
        delayFirstBody = false;
        return Promise.resolve({
          status: 200,
          json: () => new Promise(resolve => { window.__releaseStalePickerJson = resolve; })
        });
      }
      return browserFetch(url, options);
    };
  }));
  t.after(async () => { await page.close(); await browser.close(); await fixture.close(); });
  await page.waitForFunction(() => window.__releaseStalePickerJson !== null);
  await page.evaluate(() => {
    window.dispatchEvent(new PageTransitionEvent("pagehide", { persisted: true }));
    window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true }));
  });
  await waitForPolls(fixture, 1);
  await page.evaluate(() => window.__releaseStalePickerJson({
    requestId: "stale", nonce: "stale-nonce", expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  await page.waitForTimeout(100);
  assert.equal(await page.locator("#pdm-picker-overlay").isVisible(), false);
});

test("times out stalled HTTP response headers and stalled response JSON", { timeout: 30000 }, async t => {
  const browser = await chromium.launch({ executablePath: chromiumExecutable, headless: true, args: ["--no-sandbox"] });
  const fixture = await createFixture();
  fixture.state.stallHeaders = 1;
  fixture.state.stallJson = 1;
  const page = await createPage(browser, fixture);
  t.after(async () => { await page.close(); await browser.close(); await fixture.close(); });
  await waitForPolls(fixture, 3, 28000);
  assert.ok(fixture.state.pollCalls >= 3, `polling did not recover from both stalls: ${fixture.state.pollCalls} calls`);
  assert.equal(await page.locator("#pdm-picker-overlay").isVisible(), false);
});

test("places the dialog inside the actual fullscreen noVNC element and keeps actions usable", { timeout: 10000 }, async t => {
  const browser = await chromium.launch({ executablePath: chromiumExecutable, headless: true, args: ["--no-sandbox"] });
  const fixture = await createFixture();
  const page = await createPage(browser, fixture);
  t.after(async () => { await page.close(); await browser.close(); await fixture.close(); });
  await page.locator("#fullscreen").click();
  await page.waitForFunction(() => document.fullscreenElement?.id === "noVNC_container");
  fixture.state.activeRequest = request("fullscreen");
  const cancel = page.locator("#pdm-picker-cancel");
  await cancel.waitFor({ state: "visible" });
  assert.equal(await page.locator("#pdm-picker-overlay").evaluate(element => element.parentElement.id), "noVNC_container");
  await cancel.click();
  await page.waitForFunction(() => document.querySelector("#pdm-picker-overlay").hidden);
  await page.evaluate(() => document.exitFullscreen());
  await page.waitForFunction(() => document.fullscreenElement === null);
  await page.waitForFunction(() => document.querySelector("#pdm-picker-overlay").parentElement === document.body);
  assert.equal(await page.locator("#pdm-picker-overlay").evaluate(element => element.parentElement === document.body), true);
  assert.equal(fixture.state.cancels.length, 1);
});

test("cancel, retry, validation, upload, and a later request retain their flow", { timeout: 30000 }, async t => {
  const browser = await chromium.launch({ executablePath: chromiumExecutable, headless: true, args: ["--no-sandbox"] });
  const fixture = await createFixture();
  const page = await createPage(browser, fixture);
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "pdm-picker-browser-"));
  t.after(async () => { fs.rmSync(tempRoot, { recursive: true, force: true }); await page.close(); await browser.close(); await fixture.close(); });
  fixture.state.activeRequest = request("first");
  fixture.state.failCancelOnce = 1;
  const overlay = page.locator("#pdm-picker-overlay");
  await overlay.waitFor({ state: "visible", timeout: 3000 });
  await page.locator("#pdm-picker-cancel").click();
  await page.waitForFunction(() => document.querySelector("#pdm-picker-status").textContent.includes("Не удалось отменить"), null, { timeout: 3000 });
  assert.equal(await overlay.isVisible(), true);
  await page.locator("#pdm-picker-cancel").click();
  await page.waitForFunction(() => document.querySelector("#pdm-picker-overlay").hidden);
  fixture.state.activeRequest = request("second");
  await overlay.waitFor({ state: "visible", timeout: 3000 });

  const duplicateDirectory = path.join(tempRoot, "duplicates");
  fs.mkdirSync(path.join(duplicateDirectory, "one"), { recursive: true });
  fs.mkdirSync(path.join(duplicateDirectory, "two"), { recursive: true });
  fs.writeFileSync(path.join(duplicateDirectory, "one", "duplicate.m3d"), "{}");
  fs.writeFileSync(path.join(duplicateDirectory, "two", "duplicate.m3d"), "{}");
  const validDirectory = path.join(tempRoot, "valid");
  fs.mkdirSync(validDirectory);
  fs.writeFileSync(path.join(validDirectory, "part.m3d"), "{}");

  let chooserPromise = page.waitForEvent("filechooser", { timeout: 3000 });
  await page.locator("#pdm-picker-choose").click();
  let chooser = await chooserPromise;
  await chooser.setFiles(duplicateDirectory);
  await page.waitForFunction(() => document.querySelector("#pdm-picker-status").textContent.includes("встречается несколько раз"), null, { timeout: 3000 });

  chooserPromise = page.waitForEvent("filechooser", { timeout: 3000 });
  await page.locator("#pdm-picker-choose").click();
  chooser = await chooserPromise;
  await chooser.setFiles(validDirectory);
  await page.waitForFunction(() => document.querySelector("#pdm-picker-overlay").hidden, null, { timeout: 3000 });
  assert.equal(fixture.state.uploads.length, 1);
  assert.match(fixture.state.uploads[0], /filename="part\.m3d"/);

  fixture.state.activeRequest = request("third");
  await overlay.waitFor({ state: "visible", timeout: 3000 });
  await page.locator("#pdm-picker-cancel").click();
  await page.waitForFunction(() => document.querySelector("#pdm-picker-overlay").hidden);
  assert.equal(fixture.state.cancels.length, 3);
});

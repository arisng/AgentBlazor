/* Fast-feedback smoke for AgentBlazor.markdown.enhance (Phase 3, Slice A).
   Loads the real min.js into a blank page, enhances a container with a
   mermaid div + fenced code block, and asserts the rendered output.
   Run: node scripts/markdown-enhance-smoke.cjs   (from tests/e2e) */
const { chromium } = require("@playwright/test");
const path = require("path");

const MIN_JS = path.resolve(__dirname, "../../../src/AgentBlazor.Components/wwwroot/AgentBlazor.min.js");
const BASE = "http://127.0.0.1:9999"; // unused; page.setContent + addScriptTag

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const results = [];
  const check = (name, pass, detail) => results.push({ name, pass, detail });

  await page.setContent(`<html><body>
    <div data-theme="dark">
      <div id="container">
        <div class="mermaid">graph TD&#10;A --> B</div>
        <pre><code class="language-csharp">public void X() { }</code></pre>
      </div>
    </div>
  </body></html>`);

  await page.addScriptTag({ path: MIN_JS });

  // 1. Basic enhance: mermaid → SVG + a11y attributes; code → hljs spans.
  const result = await page.evaluate(async () => {
    const container = document.getElementById("container");
    const ok = await window.AgentBlazor.markdown.enhance(container, { sourceHash: "abc" });
    return {
      ok,
      svg: container.querySelector(".mermaid svg") !== null,
      role: container.querySelector(".mermaid svg")?.getAttribute("role") || null,
      aria: container.querySelector(".mermaid svg")?.getAttribute("aria-label") || null,
      highlighted: container.querySelectorAll('pre code span[class^="hljs-"]').length > 0,
      processed: container.getAttribute("data-ab-processed"),
    };
  });
  check("enhance resolves true", result.ok === true, JSON.stringify(result));
  check("mermaid renders SVG", result.svg === true);
  check("svg role=img", result.role === "img");
  check("svg aria-label set", !!result.aria && result.aria.includes("Diagram"));
  check("code highlighted", result.highlighted === true);
  check("data-ab-processed set", !!result.processed);

  // 2. Idempotency: same sourceHash → second call must NOT re-render.
  const svgCountAfterFirst = await page.evaluate(() =>
    document.querySelectorAll("#container .mermaid svg").length);
  const second = await page.evaluate(async () => {
    const container = document.getElementById("container");
    const ok = await window.AgentBlazor.markdown.enhance(container, { sourceHash: "abc" });
    return { ok, svgCount: container.querySelectorAll(".mermaid svg").length };
  });
  check("second call resolves false (guard)", second.ok === false, JSON.stringify(second));
  check("no double render", second.svgCount === svgCountAfterFirst);

  // 3. New sourceHash → enhances again.
  const third = await page.evaluate(async () => {
    const container = document.getElementById("container");
    container.innerHTML = '<div class="mermaid">graph LR&#10;X --> Y</div>';
    const ok = await window.AgentBlazor.markdown.enhance(container, { sourceHash: "def" });
    return { ok, svg: container.querySelectorAll(".mermaid svg").length };
  });
  check("changed content re-enhances", third.ok === true && third.svg === 1, JSON.stringify(third));

  // 4. Broken diagram → graceful fallback (raw text remains, no crash).
  const broken = await page.evaluate(async () => {
    const container = document.getElementById("container");
    container.innerHTML = '<div class="mermaid">graph TD&#10;A -&gt;&gt; </div>'; // invalid
    container.removeAttribute("data-ab-processed");
    const ok = await window.AgentBlazor.markdown.enhance(container, { sourceHash: "bad" });
    return { ok, text: container.textContent.trim() };
  });
  check("broken diagram suppressed, no crash", broken.ok === false, JSON.stringify(broken));

  // 5. enableMermaid=false → no mermaid load/render (fast, no CDN).
  const disabled = await page.evaluate(async () => {
    const container = document.getElementById("container");
    container.innerHTML = '<div class="mermaid">graph TD&#10;A --> B</div>';
    container.removeAttribute("data-ab-processed");
    const ok = await window.AgentBlazor.markdown.enhance(container, { enableMermaid: false, sourceHash: "off" });
    return { ok, svg: container.querySelectorAll(".mermaid svg").length };
  });
  check("enableMermaid=false skips diagrams", disabled.ok === false && disabled.svg === 0, JSON.stringify(disabled));

  await browser.close();

  let failed = 0;
  for (const r of results) {
    console.log(`${r.pass ? "PASS" : "FAIL"}  ${r.name}${r.detail ? "  -> " + r.detail : ""}`);
    if (!r.pass) failed++;
  }
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(1); });

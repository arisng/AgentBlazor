// Phase 3 — AgentBlazor.markdown.enhance client pass.
// Loads the real AgentBlazor.min.js into a blank page and verifies the
// browser-side behavior: mermaid→SVG (a11y), syntax highlighting, content-hash
// idempotency guard, graceful fallback, and disable flags.
// Run: npx playwright test -c markdown-enhance.config.cjs
const { test, expect } = require("@playwright/test");
const path = require("path");

const MIN_JS = path.resolve(__dirname, "../../../src/AgentBlazor.Components/wwwroot/AgentBlazor.min.js");

test.describe("AgentBlazor.markdown.enhance", () => {
  test("renders mermaid to SVG with a11y and highlights code", async ({ page }) => {
    await page.setContent(`<html><body>
      <div data-theme="dark">
        <div id="container">
          <div class="mermaid">graph TD&#10;A --> B</div>
          <pre><code class="language-csharp">public void X() { }</code></pre>
        </div>
      </div>
    </body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const result = await page.evaluate(async () => {
      const container = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(container, { sourceHash: "s1" });
      return {
        ok,
        svg: container.querySelector(".mermaid svg") !== null,
        role: container.querySelector(".mermaid svg")?.getAttribute("role") || null,
        aria: container.querySelector(".mermaid svg")?.getAttribute("aria-label") || null,
        highlighted: container.querySelectorAll('pre code span[class^="hljs-"]').length > 0,
        processed: container.getAttribute("data-ab-processed"),
      };
    });

    expect(result.ok).toBe(true);
    expect(result.svg).toBe(true);
    expect(result.role).toBe("img");
    expect(result.aria).toContain("Diagram");
    expect(result.highlighted).toBe(true);
    expect(result.processed).toBe("s1");
  });

  test("content-hash guard: same source never re-renders, new source does", async ({ page }) => {
    await page.setContent(`<html><body><div id="container">
      <div class="mermaid">graph TD&#10;A --> B</div>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const first = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, { sourceHash: "same" });
      return { ok, svgCount: c.querySelectorAll(".mermaid svg").length };
    });
    expect(first.ok).toBe(true);
    expect(first.svgCount).toBe(1);

    const second = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, { sourceHash: "same" });
      return { ok, svgCount: c.querySelectorAll(".mermaid svg").length };
    });
    expect(second.ok).toBe(false); // guard short-circuits
    expect(second.svgCount).toBe(1); // no double render

    const third = await page.evaluate(async () => {
      const c = document.getElementById("container");
      c.innerHTML = '<div class="mermaid">graph LR&#10;X --> Y</div>';
      const ok = await window.AgentBlazor.markdown.enhance(c, { sourceHash: "new" });
      return { ok, svgCount: c.querySelectorAll(".mermaid svg").length };
    });
    expect(third.ok).toBe(true);
    expect(third.svgCount).toBe(1);
  });

  test("broken diagram: suppressed error, raw source stays visible", async ({ page }) => {
    await page.setContent(`<html><body><div id="container">
      <div class="mermaid">graph TD&#10;A -&gt;&gt; </div>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const result = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, { sourceHash: "bad" });
      return { ok, text: c.textContent.trim() };
    });

    expect(result.ok).toBe(false);
    expect(result.text).toContain("graph TD");
  });

  test("enableMermaid=false and enableSyntaxHighlighting=false skip work", async ({ page }) => {
    await page.setContent(`<html><body><div id="container">
      <div class="mermaid">graph TD&#10;A --> B</div>
      <pre><code class="language-csharp">public void X() { }</code></pre>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const result = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, {
        sourceHash: "off",
        enableMermaid: false,
        enableSyntaxHighlighting: false,
      });
      return {
        ok,
        svg: c.querySelectorAll(".mermaid svg").length,
        spans: c.querySelectorAll("pre code span").length,
      };
    });

    expect(result.ok).toBe(false);
    expect(result.svg).toBe(0);
    expect(result.spans).toBe(0);
  });
});

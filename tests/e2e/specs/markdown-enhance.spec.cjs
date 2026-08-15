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
        enableCodeCopy: false,
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

  test("renders nomnoml fences to SVG with a11y", async ({ page }) => {
    await page.setContent(`<html><body><div id="container">
      <div class="nomnoml">[AgentBlazor] -&gt; [ChatSurface]</div>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const result = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, { sourceHash: "nn1" });
      const svg = c.querySelector(".nomnoml svg");
      return {
        ok,
        svg: svg !== null,
        role: svg?.getAttribute("role") || null,
        aria: svg?.getAttribute("aria-label") || null,
        processed: c.getAttribute("data-ab-processed"),
      };
    });

    expect(result.ok).toBe(true);
    expect(result.svg).toBe(true);
    expect(result.role).toBe("img");
    expect(result.aria).toContain("nomnoml");
    expect(result.processed).toBe("nn1");
  });

  test("wires copy buttons on code blocks and copies code to clipboard", async ({ page }) => {
    await page.setContent(`<html><body><div id="container">
      <pre><code class="language-csharp">int answer = 42;</code></pre>
    </div></body></html>`);
    // about:blank is not a secure context, so navigator.clipboard does not
    // exist — shim it to capture what our wiring hands to writeText.
    // (page.setContent is not a navigation, so addInitScript would not run.)
    await page.evaluate(() => {
      Object.defineProperty(window.navigator, "clipboard", {
        value: {
          writeText: (t) => {
            window.__abCopied = t;
            return Promise.resolve();
          },
        },
        configurable: true,
      });
    });
    await page.addScriptTag({ path: MIN_JS });

    const wired = await page.evaluate(async () => {
      const c = document.getElementById("container");
      await window.AgentBlazor.markdown.enhance(c, { sourceHash: "copy1" });
      return {
        buttons: c.querySelectorAll(".ab-copy-btn").length,
        label: c.querySelector(".ab-copy-btn")?.getAttribute("aria-label") || null,
      };
    });
    expect(wired.buttons).toBe(1);
    expect(wired.label).toBe("Copy code");

    await page.click("#container .ab-copy-btn");
    // Button flashes "Copied!" with data-copied for ~1.6s.
    await expect(page.locator("#container .ab-copy-btn")).toHaveText("Copied!");
    await expect(page.locator("#container .ab-copy-btn")).toHaveAttribute("data-copied", "true");

    const copied = await page.evaluate(() => window.__abCopied);
    expect(copied.trim()).toBe("int answer = 42;");
  });

  test("enableCodeCopy=false does not wire copy buttons", async ({ page }) => {
    await page.setContent(`<html><body><div id="container">
      <pre><code class="language-js">const x = 1;</code></pre>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const result = await page.evaluate(async () => {
      const c = document.getElementById("container");
      await window.AgentBlazor.markdown.enhance(c, { sourceHash: "nocc", enableCodeCopy: false });
      return c.querySelectorAll(".ab-copy-btn").length;
    });
    expect(result).toBe(0);
  });

  test("Markdig-style pre.mermaid renders and never gets a copy button", async ({ page }) => {
    // Markdig UseDiagrams emits <pre class="mermaid"> (verified on the live
    // demo page). The copy-button pass must skip diagram containers — wiring
    // one appends "Copy" to the diagram source and mermaid's parse fails.
    await page.setContent(`<html><body><div id="container">
      <pre class="mermaid">graph TD&#10;    A[Markdown source] --> B[Markdig pipeline]&#10;    B --> C[Allowlist sanitizer]&#10;    C --> D[MarkupString]&#10;    D --> E{Enhance?}</pre>
      <pre><code class="language-csharp">int x = 1;</code></pre>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const result = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, { sourceHash: "pre-md" });
      return {
        ok,
        svg: c.querySelectorAll(".mermaid svg").length,
        mermaidButtons: c.querySelectorAll("pre.mermaid .ab-copy-btn").length,
        codeButtons: c.querySelectorAll('pre:not(.mermaid) .ab-copy-btn').length,
      };
    });

    expect(result.ok).toBe(true);
    expect(result.svg).toBe(1); // diagram renders (source uncorrupted)
    expect(result.mermaidButtons).toBe(0); // no copy button on diagrams
    expect(result.codeButtons).toBe(1); // real code blocks still get one
  });

  test("fallback hash ignores copy buttons: re-enhance without sourceHash stays idempotent", async ({ page }) => {
    // The copy button mutates the DOM, so the no-sourceHash fallback must
    // exclude it from the hash — otherwise the second call re-enhances and
    // duplicates buttons/diagrams.
    await page.setContent(`<html><body><div id="container">
      <div class="mermaid">graph TD&#10;A --> B</div>
      <pre><code class="language-csharp">void X() { }</code></pre>
    </div></body></html>`);
    await page.addScriptTag({ path: MIN_JS });

    const first = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, {});
      return {
        ok,
        svg: c.querySelectorAll(".mermaid svg").length,
        buttons: c.querySelectorAll(".ab-copy-btn").length,
        processed: c.getAttribute("data-ab-processed"),
      };
    });
    expect(first.ok).toBe(true);
    expect(first.svg).toBe(1);
    expect(first.buttons).toBe(1);

    const second = await page.evaluate(async () => {
      const c = document.getElementById("container");
      const ok = await window.AgentBlazor.markdown.enhance(c, {});
      return {
        ok,
        svg: c.querySelectorAll(".mermaid svg").length,
        buttons: c.querySelectorAll(".ab-copy-btn").length,
        processed: c.getAttribute("data-ab-processed"),
      };
    });
    expect(second.ok).toBe(false); // guard short-circuits (hash is stable)
    expect(second.svg).toBe(1); // no double render
    expect(second.buttons).toBe(1); // no duplicate buttons
  });
});

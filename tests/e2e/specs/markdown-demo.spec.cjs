// Phase 4 — demo markdown showcase page e2e.
// Runs against the REAL AgentBlazor.Demo app (Development, no provider) and
// verifies the full server-render path: AgentMarkdownContent output, the
// client enhance pass (mermaid → SVG, hljs, copy buttons) inside the demo
// shell, plus the launchpad link.
// Run: npm run test:markdown-demo
const { test, expect } = require("@playwright/test");

const SHOWCASE_URL = "/demo/markdown-showcase";

test("showcase page renders mermaid to SVG, highlights code, and wires copy buttons", async ({ page }) => {
  await page.goto(SHOWCASE_URL);
  await expect(page.locator(".md-showcase__hero h1")).toContainText("Markdown rendering showcase");

  // Client enhance pass: mermaid fences → SVG with a11y attrs.
  const svg = page.locator(".mermaid svg").first();
  await expect(svg).toBeVisible({ timeout: 30_000 });
  await expect(svg).toHaveAttribute("role", "img");
  await expect(svg).toHaveAttribute("aria-label", /Diagram: mermaid/);
  await expect(page.locator(".mermaid svg")).toHaveCount(2); // graph TD + sequenceDiagram

  // nomnoml fence renders with its own lib (not mermaid).
  const nomnomlSvg = page.locator(".nomnoml svg").first();
  await expect(nomnomlSvg).toBeVisible();
  await expect(nomnomlSvg).toHaveAttribute("role", "img");
  await expect(nomnomlSvg).toHaveAttribute("aria-label", /Diagram: nomnoml/);

  // Syntax highlighting: hljs spans inside fenced code blocks.
  await expect(page.locator('pre code span[class^="hljs-"]').first()).toBeVisible();

  // Copy buttons on every fenced code block.
  const copyButtons = page.locator(".ab-copy-btn");
  await expect(copyButtons.first()).toBeVisible();
  const expectedBlocks = 4; // csharp, javascript, bash, json
  await expect(copyButtons).toHaveCount(expectedBlocks);
});

test("copy button copies the code block text", async ({ page }) => {
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto(SHOWCASE_URL);
  const firstBtn = page.locator(".ab-copy-btn").first();
  await expect(firstBtn).toBeVisible({ timeout: 30_000 });

  await firstBtn.click();
  await expect(firstBtn).toHaveText("Copied!");
  await expect(firstBtn).toHaveAttribute("data-copied", "true");

  const copied = await page.evaluate(() => navigator.clipboard.readText());
  expect(copied).toContain("public sealed class MarkdownOptions");
});

test("launchpad home links to the showcase page", async ({ page }) => {
  await page.goto("/demo");
  const link = page.locator('a[href="/demo/markdown-showcase"]');
  await expect(link).toBeVisible();
  await expect(link).toContainText("Open showcase");
});

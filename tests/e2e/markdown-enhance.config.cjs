const { defineConfig } = require("@playwright/test");

// Isolated config for the markdown-enhance spec: the spec loads
// AgentBlazor.min.js directly via addScriptTag, so no webServer is needed
// (unlike the demo-backed playwright.config.cjs, which targets the Linux CI
// runtime and cannot start on Windows).
module.exports = defineConfig({
  testDir: "./specs",
  testMatch: /markdown-enhance\.spec\.cjs/,
  timeout: 120000,
  expect: { timeout: 30000 },
  outputDir: "./test-results/markdown-enhance",
  reporter: [["list"]],
  use: {
    headless: true,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
});

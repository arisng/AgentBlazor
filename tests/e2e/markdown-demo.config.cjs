// Phase 4 — demo markdown showcase page e2e.
// Boots the REAL AgentBlazor.Demo app (Development, no provider needed) and
// verifies the /demo/markdown-showcase page renders mermaid → SVG, syntax
// highlighting, and copy buttons through the full server render path.
// Isolated from the main playwright.config.cjs (which targets Linux CI).
// Run: npm run test:markdown-demo
const { defineConfig } = require("@playwright/test");

// Fixed port: the config module is evaluated more than once per run (main
// process + workers), so a random port would differ per evaluation and the
// webServer would boot on a port the tests never hit. reuseExistingServer is
// false so a leftover `dotnet run` orphan (old build) can never be reused —
// kill any listener on this port before running if a previous run was
// interrupted (dotnet run can orphan its child app process).
const PORT = 5178;
const BASE_URL = `http://localhost:${PORT}`;

module.exports = defineConfig({
  testDir: "./specs",
  testMatch: /markdown-demo\.spec\.cjs/,
  timeout: 60_000,
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: BASE_URL,
    trace: "retain-on-failure",
  },
  webServer: {
    // --no-launch-profile avoids launchSettings.json (launchBrowser etc.).
    // ASPNETCORE_ENVIRONMENT=Development skips the provider-key guard in
    // Program.cs, so the static showcase page can boot without any LLM config.
    command: `dotnet run --project demo/AgentBlazor.Demo --no-launch-profile --urls http://localhost:${PORT}`,
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
    },
    cwd: require("path").resolve(__dirname, "../.."),
    url: BASE_URL,
    reuseExistingServer: false,
    timeout: 180_000,
    stdout: "ignore",
    stderr: "pipe",
  },
});

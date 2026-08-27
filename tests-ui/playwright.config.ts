import { defineConfig } from "@playwright/test";

// PRo3D is a desktop app rendering with the real GPU; one instance at a time.
export default defineConfig({
    testDir: "./tests",
    fullyParallel: false,
    workers: 1,
    // scene + kernel loading and OPC streaming take a while on first paint
    timeout: 5 * 60 * 1000,
    expect: { timeout: 30 * 1000 },
    retries: 0,
    reporter: [["list"], ["html", { open: "never" }]],
    use: {
        viewport: { width: 1600, height: 900 },
        screenshot: "only-on-failure",
        trace: "retain-on-failure",
    },
});

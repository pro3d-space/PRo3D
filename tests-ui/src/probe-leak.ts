// Memory growth while the camera moves (#841 leak hunt).
// Run: npx tsx src/probe-leak.ts <scene.pro3d> [seconds]
// Drags the camera back and forth and prints the viewer's dedicated/shared GPU memory and
// committed CPU memory every 15 s. PRO3D_EXE picks the build.
import { chromium } from "@playwright/test";
import { execSync } from "child_process";
import { launchPro3d, surfaceShadersReady } from "./pro3d";

function memory(pid: number): string {
    const ps =
        `$d = ((Get-Counter '\\GPU Process Memory(*)\\Dedicated Usage').CounterSamples | Where-Object { $_.InstanceName -match 'pid_${pid}_' } | Measure-Object CookedValue -Sum).Sum; ` +
        `$s = ((Get-Counter '\\GPU Process Memory(*)\\Shared Usage').CounterSamples | Where-Object { $_.InstanceName -match 'pid_${pid}_' } | Measure-Object CookedValue -Sum).Sum; ` +
        `$p = Get-Process -Id ${pid}; ` +
        `'gpu dedicated {0,6:N0} MB  shared {1,6:N0} MB  commit {2,6:N0} MB' -f ($d/1MB), ($s/1MB), ($p.PagedMemorySize64/1MB)`;
    return execSync(`powershell -NoProfile -Command "${ps.replace(/"/g, '\\"')}"`).toString().trim();
}

(async () => {
    const scene = process.argv[2];
    const seconds = Number(process.argv[3] ?? "90");
    const app = await launchPro3d(scene);
    const pid = app.proc.pid!;
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
    try {
        await page.goto(app.url + "?page=render");
        await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
        await surfaceShadersReady(page);
        await page.waitForTimeout(20_000);
        console.log(`t=   0s  ${memory(pid)}`);

        const started = Date.now();
        let nextSample = 15;
        let dir = 1;
        while ((Date.now() - started) / 1000 < seconds) {
            // a slow drag across the view and back: the camera turns, every frame changes
            await page.mouse.move(800, 450);
            await page.mouse.down();
            for (let i = 0; i < 20; i++) {
                await page.mouse.move(800 + dir * i * 10, 450 + i * 2);
                await page.waitForTimeout(30);
            }
            await page.mouse.up();
            dir = -dir;
            const t = (Date.now() - started) / 1000;
            if (t >= nextSample) {
                console.log(`t=${String(Math.round(t)).padStart(4)}s  ${memory(pid)}`);
                nextSample += 15;
            }
        }
        await page.waitForTimeout(5000);
        console.log(`idle 5s  ${memory(pid)}`);
    } finally {
        await browser.close();
        await app.stop();
        process.exit(0);
    }
})();

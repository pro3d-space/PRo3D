// Memory after repeatedly opening and closing the render page (#841 leak hunt).
// Run: npx tsx src/probe-reload.ts <scene.pro3d> [reloads]
// Every page load runs the view function again; whatever it builds must go away with the
// page. Prints the viewer's GPU/CPU memory after each close. PRO3D_EXE picks the build.
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
    const reloads = Number(process.argv[3] ?? "6");
    const app = await launchPro3d(scene);
    const pid = app.proc.pid!;
    const browser = await chromium.launch();
    try {
        for (let i = 1; i <= reloads; i++) {
            const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
            await page.goto(app.url + "?page=render");
            await page.waitForSelector("img.rendercontrol", { timeout: 60_000 });
            if (i === 1) await surfaceShadersReady(page);
            await page.waitForTimeout(15_000);
            const open = memory(pid);
            await page.close();
            // past aardvark.media's scene destroy timeout, so a released task is gone
            await new Promise((r) => setTimeout(r, 15_000));
            console.log(`load ${i}: open  ${open}`);
            console.log(`load ${i}: closed ${memory(pid)}`);
        }
    } finally {
        await browser.close();
        await app.stop();
        process.exit(0);
    }
})();

// Launch PRo3D with --verbose, open the render page, dump interesting log lines.
import { spawn } from "child_process";
import * as http from "http";
import { chromium } from "@playwright/test";
import { config } from "./pro3d";
import * as path from "path";

(async () => {
    const lines: string[] = [];
    const proc = spawn(
        config.exe,
        ["--server", "--port", "54377", "--verbose", "--scene", config.scene],
        { cwd: path.dirname(config.exe), stdio: ["pipe", "pipe", "pipe"] }
    );
    const collect = (b: Buffer) =>
        b.toString().split(/\r?\n/).forEach((l) => l && lines.push(l));
    proc.stdout!.on("data", collect);
    proc.stderr!.on("data", collect);

    await new Promise<void>((resolve, reject) => {
        const started = Date.now();
        const poll = () => {
            const req = http.get("http://localhost:54377/", (r) => {
                r.resume();
                resolve();
            });
            req.on("error", () =>
                Date.now() - started > 120000 ? reject(new Error("timeout")) : setTimeout(poll, 500)
            );
        };
        poll();
    });

    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1200, height: 700 } });
    await page.goto("http://localhost:54377/?page=render");
    await page.waitForTimeout(20000);
    await page.screenshot({ path: "artifacts/probe-verbose.png" });
    await browser.close();
    proc.kill();

    const interesting = lines.filter(
        (l) =>
            /error|fail|exception|shader|glsl|link|compil|effect/i.test(l) &&
            !/conversion failed|errorReporting|metres despite|DATE-OBS|band file paths|json metadata/.test(l)
    );
    console.log("=== interesting (" + interesting.length + " of " + lines.length + ") ===");
    interesting.slice(0, 60).forEach((l) => console.log(l));
    process.exit(0);
})();

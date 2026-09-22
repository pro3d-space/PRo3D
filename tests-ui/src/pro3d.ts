import { ChildProcess, spawn } from "child_process";
import * as http from "http";
import * as net from "net";
import * as path from "path";
import * as fs from "fs";
import type { Page } from "@playwright/test";

// Local test data + binaries; override via environment for other machines.
// The tests are inherently machine-local (real GPU, big OPC data sets) and are
// not meant for CI as-is.
const repoRoot = path.resolve(__dirname, "..", "..");
const artifacts = path.join(__dirname, "..", "artifacts");

/// A PRo3D.Resources.TestData checkout, from PRO3D_TEST_DATA. The projection specs use
/// its Dimorphos OPC and the simulated AFC-1 frames rendered from it, whose sidecars
/// describe the exact render camera.
export const testData = process.env.PRO3D_TEST_DATA ?? "";
export const fixture = {
    opc: path.join(testData, "HERA", "Dimorphos_opc", "Dimorphos"),
    frames: path.join(testData, "HERA", "Dimorphos_opc", "AFC_2027-03-21"),
    /// a scene set up for projection; its paths are the generating machine's, so it
    /// is a template -- see sceneFor
    sceneTemplate: path.join(testData, "HERA", "Dimorphos_opc", "AFC_2027-03-21", "ProjectionTest.pro3d"),
};

export const config = {
    exe:
        process.env.PRO3D_EXE ??
        path.join(repoRoot, "bin", "Release", "net9.0", "PRo3D.Viewer.exe"),
    /// unset: the test-data scene (sceneFor); "" launches an empty PRo3D
    scene: process.env.PRO3D_SCENE,
    /// folder of images with .mbi.json sidecars, for the import-driven specs
    imageDir: process.env.PRO3D_IMAGE_DIR ?? fixture.frames,
    /// A port for every launch. PRO3D_PORT pins one (handy while debugging by hand); otherwise
    /// each launch takes a free one, so a previous PRo3D still shutting down, or another one on
    /// the machine, cannot collide with it.
    port: process.env.PRO3D_PORT ? Number(process.env.PRO3D_PORT) : undefined,
};

/** A port nothing is listening on right now. */
async function freePort(): Promise<number> {
    return new Promise((resolve, reject) => {
        const srv = net.createServer();
        srv.on("error", reject);
        srv.listen(0, "127.0.0.1", () => {
            const port = (srv.address() as net.AddressInfo).port;
            srv.close(() => resolve(port));
        });
    });
}

/** Library rows carry the image file names; this matches one (Playwright text=). */
export const imageRow = "text=/^[\\w.-]+\\.(png|tiff?)$/";

/** The first image of a folder, by name -- the default image a spec works with. */
export function firstImage(dir: string): string {
    const images = fs.readdirSync(dir).filter((f) => /\.(png|tiff?)$/i.test(f)).sort();
    if (images.length === 0) throw new Error(`no image in ${dir}`);
    return images[0];
}

/** PRO3D_SPICE_KERNELS as a kernel root (it may name the tree or its `kernels` dir). */
function kernelRoot(): string | undefined {
    const k = process.env.PRO3D_SPICE_KERNELS;
    if (!k) return undefined;
    return fs.existsSync(path.join(k, "mk")) ? k : path.join(k, "kernels");
}

/**
 * `template` with its surface re-pointed at `opc` and its SPICE meta-kernel at the same
 * file name under PRO3D_SPICE_KERNELS, written to `out`. A scene stores absolute paths,
 * so a committed one only opens on the machine that wrote it.
 */
export function sceneFor(template: string, opc: string, out: string): string {
    if (!fs.existsSync(template))
        throw new Error(
            `scene template not found: ${template} -- set PRO3D_TEST_DATA to a PRo3D.Resources.TestData checkout, or PRO3D_SCENE`
        );
    const d = JSON.parse(fs.readFileSync(template, "utf-8").replace(/^﻿/, ""));
    const s = d.surfaceModel.surfaces.flat[0].Surfaces;
    s.importPath = opc;
    s.opcPaths = (s.opcPaths as string[]).map((p) => path.join(opc, path.win32.basename(p)));
    const opcx = fs.readdirSync(opc).find((f) => f.endsWith(".opcx"));
    if (opcx) s.opcxPath = path.join(opc, opcx);
    const k = kernelRoot();
    if (k && d.gisApp?.spiceKernel)
        d.gisApp.spiceKernel = path.join(k, "mk", path.win32.basename(d.gisApp.spiceKernel));
    d.scenePath = out;
    fs.mkdirSync(path.dirname(out), { recursive: true });
    fs.writeFileSync(out, JSON.stringify(d, null, 2));
    return out;
}

/** A scene derived from the test-data template (`sceneFor`), edited as JSON and written to
 *  `<dir>/<name>.pro3d` (default `artifacts/`). */
export function derivedScene(name: string, edit: (d: any) => void, dir = path.join(__dirname, "..", "artifacts")): string {
    fs.mkdirSync(dir, { recursive: true });
    const out = path.join(dir, `${name}.pro3d`);
    sceneFor(fixture.sceneTemplate, fixture.opc, out);
    const d = JSON.parse(fs.readFileSync(out, "utf-8"));
    edit(d);
    fs.writeFileSync(out, JSON.stringify(d, null, 2));
    return out;
}

export interface Pro3d {
    url: string;
    proc: ChildProcess;
    logFile: string;
    stop: () => Promise<void>;
}

/** Resolves when THIS process prints its URL. Waiting for an HTTP answer instead would accept
 *  one from another PRo3D on the same port, and the spec would then drive a foreign instance. */
function waitForServing(proc: ChildProcess, timeoutMs: number): Promise<void> {
    return new Promise((resolve, reject) => {
        let out = "";
        const timer = setTimeout(() => reject(new Error(`PRo3D did not start serving within ${timeoutMs} ms`)), timeoutMs);
        const onData = (chunk: Buffer) => {
            out += chunk.toString();
            if (out.includes("ELECTRON_URL:")) {
                clearTimeout(timer);
                proc.stdout!.off("data", onData);
                resolve();
            }
        };
        proc.stdout!.on("data", onData);
    });
}

function waitForHttp(url: string, timeoutMs: number): Promise<void> {
    const started = Date.now();
    return new Promise((resolve, reject) => {
        const attempt = () => {
            const req = http.get(url, (res) => {
                res.resume();
                resolve();
            });
            req.on("error", () => {
                if (Date.now() - started > timeoutMs)
                    reject(new Error(`PRo3D did not serve ${url} within ${timeoutMs} ms`));
                else setTimeout(attempt, 500);
            });
        };
        attempt();
    });
}

/** Launch PRo3D.Viewer in --server mode (no Aardium) and wait until it serves.
 *  `sceneOverride` replaces PRO3D_SCENE, for specs that generate their own scene;
 *  `env` adds environment variables for this launch (e.g. PRO3D_LAYOUT_DIR). */
export async function launchPro3d(sceneOverride?: string, env?: Record<string, string>): Promise<Pro3d> {
    if (!fs.existsSync(config.exe))
        throw new Error(`PRo3D exe not found: ${config.exe} (set PRO3D_EXE)`);
    const scene =
        sceneOverride ??
        config.scene ??
        sceneFor(fixture.sceneTemplate, fixture.opc, path.join(artifacts, "testdata-scene.pro3d"));
    // PRO3D_SCENE="" launches with no scene at all -- an empty PRo3D, which is where
    // an end-to-end test has to start if it is going to exercise the steps a user
    // actually performs (import a surface, bind it, set the epoch) rather than
    // inheriting them from a scene file that already had everything right.
    const withScene = scene.length > 0;
    if (withScene && !fs.existsSync(scene))
        throw new Error(`scene not found: ${scene} (set PRO3D_SCENE)`);

    const logFile = path.join(__dirname, "..", "pro3d.log");
    const log = fs.createWriteStream(logFile);
    // pro3d.log is the latest launch only - the next spec truncates it. Every launch
    // also keeps its own copy, so a hang or crash in an early spec is still readable
    // after the whole suite has run.
    const logDir = path.join(artifacts, "logs");
    fs.mkdirSync(logDir, { recursive: true });
    const keptLog = fs.createWriteStream(
        path.join(logDir, `pro3d-${new Date().toISOString().replace(/[:.]/g, "-")}.log`)
    );

    const port = config.port ?? (await freePort());
    const proc = spawn(
        config.exe,
        withScene
            ? ["--server", "--port", String(port), "--scene", scene]
            : ["--server", "--port", String(port)],
        {
            cwd: path.dirname(config.exe),
            env: { ...process.env, ...(env ?? {}) },
            // keep stdin an open pipe: server mode blocks on Console.Read()
            // and exits immediately when stdin is EOF (Program.fs)
            stdio: ["pipe", "pipe", "pipe"],
        }
    );
    proc.stdout!.pipe(log);
    proc.stderr!.pipe(log);
    proc.stdout!.pipe(keptLog);
    proc.stderr!.pipe(keptLog);

    const url = `http://localhost:${port}/`;

    const exited = new Promise<never>((_, reject) => {
        proc.on("exit", (code) =>
            reject(new Error(`PRo3D exited early (code ${code}), see ${logFile}`))
        );
    });

    // serving (this process said so), then answering
    await Promise.race([waitForServing(proc, 120_000), exited]);
    await Promise.race([waitForHttp(url, 60_000), exited]);

    return {
        url,
        proc,
        logFile,
        stop: () =>
            new Promise<void>((resolve) => {
                proc.removeAllListeners("exit");
                proc.once("exit", () => resolve());
                // closing stdin unblocks Console.Read() -> clean shutdown path
                proc.stdin!.end();
                setTimeout(() => {
                    proc.kill();
                    resolve();
                }, 5000).unref();
            }),
    };
}

/**
 * Waits until the viewer has linked its OPC surface effect (#719) - the seconds-long
 * step that has to finish before any surface can be drawn. Call it on the render page
 * after `img.rendercontrol` appears, and still wait for the render itself afterwards:
 * this resolves once the shared input layout exists, while generating the GL program for
 * the variant a surface actually uses happens on the render thread just after.
 */
export async function surfaceShadersReady(page: Page, timeoutMs = 270_000): Promise<void> {
    await page.waitForSelector('[data-surface-shaders="ready"]', { state: "attached", timeout: timeoutMs });
}

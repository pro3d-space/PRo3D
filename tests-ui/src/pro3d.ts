import { ChildProcess, spawn } from "child_process";
import * as http from "http";
import * as path from "path";
import * as fs from "fs";

// Local test data + binaries; override via environment for other machines.
// The tests are inherently machine-local (real GPU, big OPC data sets) and are
// not meant for CI as-is.
const repoRoot = path.resolve(__dirname, "..", "..");

export const config = {
    exe:
        process.env.PRO3D_EXE ??
        path.join(repoRoot, "bin", "Release", "net9.0", "PRo3D.Viewer.exe"),
    scene:
        process.env.PRO3D_SCENE ??
        "C:\\pro3ddata\\HERA\\workshop3\\projectionScene.pro3d",
    /// one date folder of the COP simulated images (png + .mbi.json sidecars)
    imageDir:
        process.env.PRO3D_IMAGE_DIR ??
        "C:\\pro3ddata\\HERA\\workshop3\\COP\\COP\\2027-02-05",
    port: Number(process.env.PRO3D_PORT ?? 54321),
};

export interface Pro3d {
    url: string;
    proc: ChildProcess;
    logFile: string;
    stop: () => Promise<void>;
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

/** Launch PRo3D.Viewer in --server mode (no Aardium) and wait until it serves. */
export async function launchPro3d(): Promise<Pro3d> {
    if (!fs.existsSync(config.exe))
        throw new Error(`PRo3D exe not found: ${config.exe} (set PRO3D_EXE)`);
    if (!fs.existsSync(config.scene))
        throw new Error(`scene not found: ${config.scene} (set PRO3D_SCENE)`);

    const logFile = path.join(__dirname, "..", "pro3d.log");
    const log = fs.createWriteStream(logFile);

    const proc = spawn(
        config.exe,
        ["--server", "--port", String(config.port), "--scene", config.scene],
        {
            cwd: path.dirname(config.exe),
            // keep stdin an open pipe: server mode blocks on Console.Read()
            // and exits immediately when stdin is EOF (Program.fs)
            stdio: ["pipe", "pipe", "pipe"],
        }
    );
    proc.stdout!.pipe(log);
    proc.stderr!.pipe(log);

    const url = `http://localhost:${config.port}/`;

    const exited = new Promise<never>((_, reject) => {
        proc.on("exit", (code) =>
            reject(new Error(`PRo3D exited early (code ${code}), see ${logFile}`))
        );
    });

    await Promise.race([waitForHttp(url, 120_000), exited]);

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

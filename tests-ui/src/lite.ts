// Launcher for PRo3D Lite (src/PRo3D.Lite, docs/PRo3DLite.md) in --server mode.
import { spawn, ChildProcess } from "child_process";
import * as fs from "fs";
import * as http from "http";
import * as net from "net";
import * as path from "path";

export const liteExe =
    process.env.PRO3D_LITE_EXE ??
    path.join(__dirname, "..", "..", "bin", "Release", "net9.0", "PRo3D.Lite.exe");

/** the MSL Stimson OPC the Expecto feature tests use: PRO3D_TEST_DATA, else the submodule */
export const liteOpc =
    process.env.PRO3D_LITE_OPC ??
    [
        process.env.PRO3D_TEST_DATA ? path.join(process.env.PRO3D_TEST_DATA, "MSL", "1087_004779_MSLMST_0011") : "",
        path.join(__dirname, "..", "..", "src", "Tests", "resources", "MSL", "1087_004779_MSLMST_0011"),
        path.join(__dirname, "..", "..", "src", "Tests", "resources", "1087_004779_MSLMST_0011"),
    ].find((p) => p !== "" && fs.existsSync(p)) ??
    "";

export interface LiteApp {
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
                if (Date.now() - started > timeoutMs) reject(new Error(`PRo3D Lite did not serve ${url} within ${timeoutMs} ms`));
                else setTimeout(attempt, 500);
            });
        };
        attempt();
    });
}

async function freePort(): Promise<number> {
    return new Promise((resolve, reject) => {
        const srv = net.createServer();
        srv.on("error", reject);
        srv.listen(0, "127.0.0.1", () => {
            const p = (srv.address() as net.AddressInfo).port;
            srv.close(() => resolve(p));
        });
    });
}

/** Start PRo3D.Lite.exe in --server mode and wait until it serves. Server mode blocks on
 *  Console.Read(), so stdin stays an open pipe; closing it shuts the app down. */
export async function launchLite(args: string[]): Promise<LiteApp> {
    const port = await freePort();
    if (!fs.existsSync(liteExe)) throw new Error(`PRo3D Lite exe not found: ${liteExe} (build src/PRo3D.Lite -c Release or set PRO3D_LITE_EXE)`);
    const logDir = path.join(__dirname, "..", "artifacts", "logs");
    fs.mkdirSync(logDir, { recursive: true });
    const logFile = path.join(logDir, `lite-${new Date().toISOString().replace(/[:.]/g, "-")}.log`);
    const log = fs.createWriteStream(logFile);
    const proc = spawn(liteExe, ["--server", "--port", String(port), ...args], {
        cwd: path.dirname(liteExe),
        stdio: ["pipe", "pipe", "pipe"],
    });
    proc.stdout!.pipe(log);
    proc.stderr!.pipe(log);
    const url = `http://localhost:${port}/`;
    const exited = new Promise<never>((_, reject) =>
        proc.on("exit", (code) => reject(new Error(`PRo3D Lite exited early (code ${code}), see ${logFile}`)))
    );
    const serving = new Promise<void>((resolve, reject) => {
        let out = "";
        const timer = setTimeout(() => reject(new Error(`PRo3D Lite did not start serving within 120000 ms, see ${logFile}`)), 120_000);
        proc.stdout!.on("data", (chunk: Buffer) => {
            out += chunk.toString();
            if (out.includes("LITE_URL:")) {
                clearTimeout(timer);
                resolve();
            }
        });
    });
    await Promise.race([serving, exited]);
    await Promise.race([waitForHttp(url, 30_000), exited]);
    return {
        url,
        proc,
        logFile,
        stop: () =>
            new Promise<void>((resolve) => {
                proc.removeAllListeners("exit");
                proc.once("exit", () => resolve());
                proc.stdin!.end();
                setTimeout(() => {
                    proc.kill();
                    resolve();
                }, 5000).unref();
            }),
    };
}

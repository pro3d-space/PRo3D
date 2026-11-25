// Launcher for the standalone map projection app (PRo3D.MapProjection.Standalone.exe, #772).
import { spawn, ChildProcess } from "child_process";
import * as fs from "fs";
import * as http from "http";
import * as net from "net";
import * as path from "path";

export const mapExe =
    process.env.PRO3D_MAP_EXE ??
    path.join(__dirname, "..", "..", "bin", "Release", "net9.0", "PRo3D.MapProjection.Standalone.exe");

export interface MapApp {
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
                    reject(new Error(`map projection app did not serve ${url} within ${timeoutMs} ms`));
                else setTimeout(attempt, 500);
            });
        };
        attempt();
    });
}

/** A port nothing is listening on right now. */
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

/** Start PRo3D.MapProjection.Standalone.exe in --server mode on `opcs` and wait until it serves. A free port
 *  per launch: a fixed one collides with a previous app still shutting down. */
export async function launchMap(opcs: string[], extraArgs: string[] = [], fixedPort?: number): Promise<MapApp> {
    const port = fixedPort ?? (await freePort());
    if (!fs.existsSync(mapExe))
        throw new Error(`map projection exe not found: ${mapExe} (build src/PRo3D.MapProjection or set PRO3D_MAP_EXE)`);
    const logDir = path.join(__dirname, "..", "artifacts", "logs");
    fs.mkdirSync(logDir, { recursive: true });
    const logFile = path.join(logDir, `map-projection-${new Date().toISOString().replace(/[:.]/g, "-")}.log`);
    const log = fs.createWriteStream(logFile);
    const args = ["--server", "--port", String(port), ...opcs.flatMap((o) => ["--opc", o]), ...extraArgs];
    const proc = spawn(mapExe, args, {
        cwd: path.dirname(mapExe),
        // server mode blocks on Console.Read(); stdin EOF shuts it down
        stdio: ["pipe", "pipe", "pipe"],
    });
    proc.stdout!.pipe(log);
    proc.stderr!.pipe(log);
    const url = `http://localhost:${port}/`;
    const exited = new Promise<never>((_, reject) =>
        proc.on("exit", (code) => reject(new Error(`map projection app exited early (code ${code}), see ${logFile}`)))
    );
    // this process announcing its url, then answering: an HTTP check alone would accept an
    // answer from another app on the port
    const serving = new Promise<void>((resolve, reject) => {
        let out = "";
        const timer = setTimeout(() => reject(new Error(`map projection app did not start serving within 60000 ms`)), 60_000);
        proc.stdout!.on("data", (chunk: Buffer) => {
            out += chunk.toString();
            if (out.includes("MAP_PROJECTION_URL:")) {
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

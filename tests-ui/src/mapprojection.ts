// Launcher for the standalone map projection app (PRo3D.MapProjection.exe, #772).
import { spawn, ChildProcess } from "child_process";
import * as fs from "fs";
import * as http from "http";
import * as path from "path";

export const mapExe =
    process.env.PRO3D_MAP_EXE ??
    path.join(__dirname, "..", "..", "bin", "Release", "net9.0", "PRo3D.MapProjection.exe");

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

/** Start PRo3D.MapProjection.exe in --server mode on `opcs` and wait until it serves. */
export async function launchMap(opcs: string[], extraArgs: string[] = [], port = 54331): Promise<MapApp> {
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
    await Promise.race([waitForHttp(url, 60_000), exited]);
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

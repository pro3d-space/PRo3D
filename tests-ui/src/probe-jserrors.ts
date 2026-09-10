// Watch the browser console while a PRo3D page loads and an accordion is clicked.
// onBoot scripts run in the page; if one throws, the widget silently never initialises
// and the only evidence is in the console -- which none of the other probes were reading.
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";

const PAGE = process.env.PRO3D_JS_PAGE ?? "gis";
const LABEL = process.env.PRO3D_ACC_LABEL ?? "Projected Images";
const INNER = process.env.PRO3D_ACC_INNER ?? "Projection Settings";

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await (await browser.newContext()).newPage();

    const seen: string[] = [];
    page.on("console", (m) => {
        if (m.type() === "error" || m.type() === "warning")
            seen.push(`[console.${m.type()}] ${m.text()}`);
    });
    page.on("pageerror", (e) => seen.push(`[pageerror] ${e.message.split("\n")[0]}`));
    page.on("requestfailed", (r) =>
        seen.push(`[requestfailed] ${r.url()} ${r.failure()?.errorText ?? ""}`)
    );

    await page.goto(app.url + "?page=" + PAGE);
    await page.waitForLoadState("networkidle");
    await page.locator(`text=${LABEL}`).first().waitFor({ timeout: 60_000 });
    await page.waitForTimeout(3000);
    console.log(`--- after load (${seen.length} messages) ---`);
    seen.forEach((s) => console.log("  " + s));
    seen.length = 0;

    const click = async (label: string) =>
        await page.evaluate((label) => {
            const t = Array.from(document.querySelectorAll(".title")).find(
                (e) => (e.textContent ?? "").trim() === label
            );
            if (!t) return "not found";
            (t as HTMLElement).click();
            return "clicked";
        }, label);

    console.log(`\nclick "${LABEL}": ${await click(LABEL)}`);
    await page.waitForTimeout(1500);
    console.log(`click "${INNER}": ${await click(INNER)}`);
    await page.waitForTimeout(1500);
    console.log(`--- after clicks (${seen.length} messages) ---`);
    seen.forEach((s) => console.log("  " + s));

    await browser.close();
    await app.stop();
})();

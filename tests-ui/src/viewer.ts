import { Page } from "@playwright/test";

/**
 * The planet line of the render page's overlay (ViewerGUI.textOverlays: the first cell
 * of its table, e.g. "Dimorphos", "Mars (IAU ellipsoid)", "None xyz"). Exact, unlike a
 * text= locator, which matches any element containing the word anywhere on the page.
 */
export async function overlayPlanet(render: Page): Promise<string> {
    return render.evaluate(() => {
        const td = document.querySelector("table td");
        return (td?.textContent ?? "").trim();
    });
}

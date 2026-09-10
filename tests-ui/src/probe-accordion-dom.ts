// What does an accordion's DOM actually look like, and what does jQuery match?
// Written because two guesses at the selector both produced "nothing opens", which
// means the selector matched nothing -- so stop guessing and print the tree.
import { chromium } from "@playwright/test";
import { launchPro3d } from "./pro3d";

const LABEL = process.env.PRO3D_ACC_LABEL ?? "Projected Images";

(async () => {
    const app = await launchPro3d();
    const browser = await chromium.launch();
    const page = await (await browser.newContext()).newPage();
    await page.goto(app.url + "?page=gis");
    await page.waitForLoadState("networkidle");
    await page.locator(`text=${LABEL}`).first().waitFor({ timeout: 60_000 });
    await page.waitForTimeout(2500);

    const out: string = await page.evaluate(`
        (() => {
            const lines = [];
            const titles = Array.from(document.querySelectorAll('.title'));
            const t = titles.find(e => (e.textContent || '').trim() === ${JSON.stringify(LABEL)});
            if (!t) return 'title not found';
            lines.push('title      : <' + t.tagName.toLowerCase() + ' class="' + t.className + '">');
            let p = t.parentElement, depth = 0;
            while (p && depth < 4) {
                lines.push('parent[' + depth + '] : <' + p.tagName.toLowerCase() +
                           ' id="' + (p.id || '') + '" class="' + p.className + '">');
                p = p.parentElement; depth++;
            }
            // the element the onBoot id sits on: walk up to the first with an id
            let idEl = t.parentElement;
            while (idEl && !idEl.id) idEl = idEl.parentElement;
            if (!idEl) { lines.push('no ancestor with an id'); return lines.join('\\n'); }
            lines.push('');
            lines.push('nearest ancestor with an id: #' + idEl.id +
                       ' class="' + idEl.className + '"');
            const jq = window.jQuery || window.$;
            if (!jq) { lines.push('jQuery not present'); return lines.join('\\n'); }
            const probes = [
                ".children('.ui.accordion')",
                ".find('.ui.accordion')",
                ".find('> .ui.accordion')",
                ".find('.title')",
                ".find('> .title')",
                ".children('.title')",
            ];
            lines.push('');
            for (const p of probes) {
                let n = -1;
                try { n = eval("jq('#' + idEl.id)" + p + ".length"); } catch (e) { n = -2; }
                lines.push('  $(#' + idEl.id + ')' + p + ' -> ' + n);
            }
            lines.push('');
            lines.push('is jQuery.fn.accordion present? ' + (typeof jq.fn.accordion));
            return lines.join('\\n');
        })()
    `);
    console.log(out);
    await browser.close();
    await app.stop();
})();

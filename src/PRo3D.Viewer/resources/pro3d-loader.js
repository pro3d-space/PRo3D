/* PRo3D boot screen: a "Refresh" button under the artwork.
 *
 * The loader element belongs to Aardvark.UI (resources/renderer.js,
 * Renderer.createLoader): it appends a div.loader to the render control's
 * div.aardvark, removes it once the first frame has faded in, and creates it
 * again if the connection drops. A boot screen that hangs is usually cured by
 * reloading the page, so we put a button for that into every loader that
 * appears. It does what the key it names does: reload the page.
 *
 * Styling lives in pro3d-loader.css. See docs/LoadingScreen.md.
 */
(function () {
    "use strict";

    const isMac = /Mac/i.test(navigator.platform || navigator.userAgent || "");

    // Feather-style "rotate-cw", stroked in currentColor
    const icon =
        "<svg viewBox='0 0 24 24' width='16' height='16' aria-hidden='true' fill='none' " +
        "stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>" +
        "<polyline points='23 4 23 10 17 10'></polyline>" +
        "<path d='M20.49 15a9 9 0 1 1-2.12-9.36L23 10'></path>" +
        "</svg>";

    const keys = isMac ? "<kbd>Fn</kbd>+<kbd>F5</kbd>" : "<kbd>F5</kbd>";

    function addButton(loader) {
        if (loader.querySelector(":scope > .pro3d-loader-refresh")) return;

        const button = document.createElement("button");
        button.type = "button";
        button.className = "pro3d-loader-refresh";
        button.title = "Reload PRo3D (" + (isMac ? "Fn+F5" : "F5") + ")";
        button.innerHTML = icon + "<span>Refresh</span><span class='pro3d-loader-keys'>" + keys + "</span>";

        // the render control listens for pointer input on its div; keep the
        // button's clicks from turning into camera interaction
        for (const type of ["pointerdown", "pointerup", "mousedown", "mouseup", "click", "dblclick", "wheel"]) {
            button.addEventListener(type, function (e) { e.stopPropagation(); });
        }
        button.addEventListener("click", function () { window.location.reload(); });

        loader.appendChild(button);
    }

    function scan(root) {
        if (root.nodeType !== Node.ELEMENT_NODE) return;
        if (root.matches("div.aardvark > div.loader")) addButton(root);
        for (const loader of root.querySelectorAll("div.aardvark > div.loader")) addButton(loader);
    }

    function start() {
        scan(document.body);
        new MutationObserver(function (mutations) {
            for (const m of mutations)
                for (const node of m.addedNodes) scan(node);
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start);
})();

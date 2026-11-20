const kdTreeRebuildOptions = {
	type: 'question',
	buttons: ['Cancel', 'Yes'],
	defaultId: 0,
	title: 'Rebuilding KdTrees',
	message: 'Creating KdTrees may take some time.',
	detail: 'For progress and feedback please look at the console output. After regeneration, reload restart PRo3D and reload the surface.'
};

// --- busy indicator ------------------------------------------------------------------
//
// PRo3D's update thread, its DOM-diff thread and the render service share one lock, so
// while a slow update runs the server cannot repaint this page and the 3D stream stops.
// Everything here therefore has to be client side: the spinner is a CSS animation (the
// compositor keeps it moving with the server frozen) and the state comes from /busy, a
// route that reads one field and takes no lock. See docs/BusyIndicator.md.

function startBusyIndicator(id, thresholdMs) {
	if (!thresholdMs || thresholdMs <= 0) { return; }          // -nobusy

	var el = document.getElementById(id);
	if (!el) { return; }

	if (!document.getElementById('pro3d-busy-style')) {
		var css = document.createElement('style');
		css.id = 'pro3d-busy-style';
		css.textContent =
			'@keyframes pro3dBusySpin { to { transform: rotate(360deg); } }' +
			// pointer-events:none is not cosmetic - however this element ends up, it must
			// never be able to swallow a click meant for the scene or a panel. It is also
			// set inline on the element, because this stylesheet does not exist at all when
			// the indicator is switched off; same for the hidden-by-default state, which is
			// why there is no `display` here.
			// Lighter than the app's own #1B1C1E, and opaque: a translucent near-black
			// chip on a near-black UI is invisible, which defeats the point of it.
			'.pro3d-busy { position: fixed; left: 50%; bottom: 32px; transform: translateX(-50%);' +
			'  z-index: 30000; pointer-events: none; align-items: center; gap: 14px;' +
			'  padding: 14px 26px; border-radius: 28px; background: #2b2e33;' +
			'  border: 1px solid rgba(227,179,65,0.55);' +
			'  color: #f2f2f2; font-family: "Roboto Mono", monospace; font-size: 19px;' +
			'  letter-spacing: 0.3px; white-space: nowrap;' +
			'  box-shadow: 0 6px 22px rgba(0,0,0,0.65); }' +
			// amber, the same accent the tool strip uses for the selection group
			'.pro3d-busy-spinner { width: 20px; height: 20px; border-radius: 50%; flex: 0 0 auto;' +
			'  border: 3px solid rgba(255,255,255,0.22); border-top-color: #e3b341;' +
			'  animation: pro3dBusySpin 0.8s linear infinite; }';
		document.head.appendChild(css);
	}

	var text = el.querySelector('.pro3d-busy-text');

	var show = function (label) {
		el.style.display = label ? 'flex' : 'none';
		if (label) { text.textContent = label; }
	};

	// More than one thing can be busy at once - a blocked update and a hover pick loading a
	// cold patch are independent. Each is held to the threshold on its own, so a brief one
	// alongside a long one adds nothing, and whatever is left is joined into one line.
	var describe = function (s) {
		if (s.busy !== true) { return null; }
		var ops = s.ops || [{ op: s.op, ms: s.ms }];
		var active = ops.filter(function (o) { return o && o.ms >= thresholdMs; });
		if (active.length === 0) { return null; }
		return active.map(function (o) {
			return o.op + '  ' + (o.ms / 1000).toFixed(1) + 's';
		}).join('   ·   ');
	};

	// One request at a time. /busy itself cannot block, but the process can stop answering
	// HTTP for a while (a GC pause, a saturated thread pool). Without this guard the page
	// would keep queueing 5 requests a second against a browser origin limit of ~6 sockets,
	// and every other same-origin request - the scripts and stylesheets aardvark pulls in
	// after a DOM diff - would end up waiting behind them.
	var inFlight = false;
	var canTimeout = typeof AbortSignal !== 'undefined' && !!AbortSignal.timeout;

	setInterval(function () {
		if (inFlight) { return; }
		inFlight = true;
		// the signal has to be built per request - one made up front would fire once and
		// then abort every later request forever
		var opts = { cache: 'no-store' };
		if (canTimeout) { opts.signal = AbortSignal.timeout(2000); }
		fetch('/busy', opts)
			.then(function (r) { return r.json(); })
			.then(function (s) { show(describe(s)); })
			.catch(function () { show(null); })     // shutting down, or gone - never throw
			.then(function () { inFlight = false; });
	}, 200);
}

// --- Ctrl shortcuts ----------------------------------------------------------------
//
// Every page body has an onkeydown that asks this whether to send a Ctrl+Z/Y/S to the
// server (Events.onCtrlShortcut, docs/KeyboardShortcuts.md). A text field's own undo is
// the browser's default action, not a listener that could stop the event, so the target
// decides: typing keeps Z/Y, and S never reaches the browser's "save page".

function pro3dIsTextEntry(t) {
	return !!t && (t.isContentEditable || t.tagName === 'TEXTAREA' ||
		(t.tagName === 'INPUT' && /^(text|number|search|email|url|tel|password)$/.test(t.type)));
}

function pro3dCtrlShortcut(event) {
	var k = event.keyCode;                                       // 90 Z, 89 Y, 83 S
	if (event.repeat || !(event.ctrlKey || event.metaKey) || event.altKey) { return false; }
	if (k !== 90 && k !== 89 && k !== 83) { return false; }
	if (pro3dIsTextEntry(event.target)) {
		if (k === 83) { event.preventDefault(); }
		return false;
	}
	event.preventDefault();
	return true;
}

// Delete with no modifier, on the pages that opt in (the 3D view, Annotations, Properties;
// Events.onCtrlShortcutOrDelete): deletes the selected annotations. In a text field it
// deletes text, and elsewhere - the Surfaces tree, say - it must not touch annotations.

function pro3dDeleteKey(event) {
	if (event.keyCode !== 46 || event.repeat) { return false; }
	if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) { return false; }
	if (pro3dIsTextEntry(event.target)) { return false; }
	event.preventDefault();
	return true;
}

// --- tooltips ------------------------------------------------------------------------
//
// Every `title` attribute in the UI is shown by this script instead of the browser
// (docs/Tooltips.md). The native tooltip on macOS appears only after ~1-1.5 s, a delay
// the page cannot change. On hover the element's title moves to data-pro3d-title, so
// the native one never shows, and this script shows its own after TOOLTIP_DELAY_MS. On
// leave the title goes back, so the DOM stays what aardvark.media's diff expects. A title
// the server changes while the element is hovered is picked up through a
// MutationObserver. `\n` in a title is a line break, as in a native tooltip.

(function () {
	var TOOLTIP_DELAY_MS = 250;
	var OFFSET = 14;           // px from the pointer

	var tip = null;            // the tooltip element, made on first use
	var current = null;        // the hovered element whose title is held
	var text = '';
	var timer = null;
	var shown = false;
	var mouseX = 0, mouseY = 0;
	var observer = null;

	function tooltipElement() {
		if (tip) { return tip; }
		var css = document.createElement('style');
		css.id = 'pro3d-tooltip-style';
		css.textContent =
			'.pro3d-tooltip { position: fixed; z-index: 30001; pointer-events: none;' +
			'  max-width: 380px; padding: 6px 9px; border-radius: 4px;' +
			'  background: #2b2e33; border: 1px solid rgba(255,255,255,0.18);' +
			'  box-shadow: 0 4px 14px rgba(0,0,0,0.55); color: #f2f2f2;' +
			'  font-size: 12px; line-height: 1.4; white-space: pre-line; display: none; }';
		document.head.appendChild(css);
		tip = document.createElement('div');
		tip.className = 'pro3d-tooltip';
		document.body.appendChild(tip);
		return tip;
	}

	function place() {
		var t = tooltipElement();
		var w = t.offsetWidth, h = t.offsetHeight;
		var x = mouseX + OFFSET, y = mouseY + OFFSET;
		// keep it inside the window: flip to the other side of the pointer when needed
		if (x + w > window.innerWidth - 4) { x = Math.max(4, mouseX - OFFSET - w); }
		if (y + h > window.innerHeight - 4) { y = Math.max(4, mouseY - OFFSET - h); }
		t.style.left = x + 'px';
		t.style.top = y + 'px';
	}

	function show() {
		timer = null;
		if (!current || !document.contains(current) || !text) { release(); return; }
		var t = tooltipElement();
		t.textContent = text;
		t.style.display = 'block';
		place();
		shown = true;
	}

	function hide() {
		if (timer) { clearTimeout(timer); timer = null; }
		if (tip) { tip.style.display = 'none'; }
		shown = false;
	}

	// hand the title back and forget the element
	function release() {
		hide();
		if (observer) { observer.disconnect(); observer = null; }
		if (current) {
			if (current.hasAttribute('data-pro3d-title')) {
				// the empty placeholder still there: hand the title back. Gone: the
				// server removed the title while hovered, so it stays removed.
				if (current.getAttribute('title') === '') { current.setAttribute('title', text); }
				current.removeAttribute('data-pro3d-title');
			}
		}
		current = null;
		text = '';
	}

	// While hovered the title is replaced by an empty one, not removed: title="" shows
	// no native tooltip, and a removal by the server stays observable (removing an
	// attribute that is not there reports nothing).
	function hold(el) {
		current = el;
		text = el.getAttribute('title') || '';
		el.setAttribute('data-pro3d-title', text);
		el.setAttribute('title', '');
		// Observer callbacks run later (microtasks): our own changes inside the callback
		// are discarded with takeRecords(). The change above happens before observe().
		observer = new MutationObserver(function () {
			if (current !== el) { return; }
			var t = el.getAttribute('title');
			if (t === '') { return; }
			if (t === null) {
				// the server removed the title while hovered: drop the held one, or
				// release() would put back a title the server no longer has
				text = '';
				el.removeAttribute('data-pro3d-title');
				hide();
				return;
			}
			// the server set a new title while hovered: take it, keep the native one off
			text = t;
			el.setAttribute('data-pro3d-title', t);
			el.setAttribute('title', '');
			observer.takeRecords();
			if (shown) { tooltipElement().textContent = t; place(); }
			else if (!timer) { timer = setTimeout(show, TOOLTIP_DELAY_MS); }
		});
		observer.observe(el, { attributes: true, attributeFilter: ['title'] });
		timer = setTimeout(show, TOOLTIP_DELAY_MS);
	}

	document.addEventListener('mouseover', function (e) {
		mouseX = e.clientX; mouseY = e.clientY;
		var el = e.target && e.target.closest ? e.target.closest('[title],[data-pro3d-title]') : null;
		if (el === current) { return; }
		release();
		if (el && el.getAttribute('title')) { hold(el); }
	}, true);

	document.addEventListener('mousemove', function (e) {
		mouseX = e.clientX; mouseY = e.clientY;
		// the hovered element was re-rendered away: nothing fires a mouseout for it
		if (current && !document.contains(current)) { release(); }
	}, true);

	document.addEventListener('mouseout', function (e) {
		if (!current) { return; }
		var to = e.relatedTarget;
		if (!to || !current.contains(to)) { release(); }
	}, true);

	// like a native tooltip: gone on a click, a key or scrolling
	['mousedown', 'keydown', 'wheel'].forEach(function (name) {
		document.addEventListener(name, hide, true);
	});
	window.addEventListener('blur', release);
})();

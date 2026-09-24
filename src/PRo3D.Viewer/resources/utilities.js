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

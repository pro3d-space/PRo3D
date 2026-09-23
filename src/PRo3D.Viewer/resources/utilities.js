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
			'.pro3d-busy { position: fixed; left: 50%; bottom: 22px; transform: translateX(-50%);' +
			'  z-index: 30000; pointer-events: none; align-items: center; gap: 10px;' +
			'  padding: 8px 16px; border-radius: 16px; background: rgba(20,21,23,0.88);' +
			'  color: #eee; font-family: "Roboto Mono", monospace; font-size: 13px;' +
			'  box-shadow: 0 2px 10px rgba(0,0,0,0.45); }' +
			'.pro3d-busy-spinner { width: 14px; height: 14px; border-radius: 50%;' +
			'  border: 2px solid rgba(255,255,255,0.25); border-top-color: #eee;' +
			'  animation: pro3dBusySpin 0.8s linear infinite; }';
		document.head.appendChild(css);
	}

	var text = el.querySelector('.pro3d-busy-text');

	var show = function (on, label, ms) {
		el.style.display = on ? 'flex' : 'none';
		if (on) { text.textContent = label + '  ' + (ms / 1000).toFixed(1) + 's'; }
	};

	setInterval(function () {
		fetch('/busy', { cache: 'no-store' })
			.then(function (r) { return r.json(); })
			.then(function (s) { show(s.busy === true && s.ms >= thresholdMs, s.op, s.ms); })
			.catch(function () { show(false); });   // shutting down, or gone - never throw
	}, 200);
}

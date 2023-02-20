function jscytoscape(id) {
	var nodes = aardvark.getChannel(id, "provenanceNodes");
	var edges = aardvark.getChannel(id, "provenanceEdges");
	var selectedNode = aardvark.getChannel(id, "selectedNode");
	var cy = cytoscape({
		container: document.getElementById(id),

		boxSelectionEnabled: false,
		autounselectify: true,

		elements: {
			nodes: [],
			edges: []
		},
		layout: {
			name: 'dagre',
			rankDir: 'LR',
			nodeDimensionsIncludeLabels: true
		},
		//style: 
		//	cytoscape.stylesheet()
		//		.selector('edge')
		//		.css({
		//			'content': 'data(label)'
		//		})

		style: [{
				selector: 'node',
				css: {
					'content': 'data(id)',
					'text-valign': 'center',
					'text-halign': 'center',
					'height': '60px',
					'width': '60px',
					'border-color': 'black',
					'border-opacity': '1',
					'border-width': '10px'
				}
			},
			{
				selector: '$node > node',
				css: {
					'padding-top': '10px',
					'padding-left': '10px',
					'padding-bottom': '10px',
					'padding-right': '10px',
					'text-valign': 'top',
					'text-halign': 'center',
					'background-color': '#bbb'
				}
			},
			{
				selector: 'edge',
				css: {
					'target-arrow-shape': 'triangle',
					'content': 'data(label)'
				}
			},
			{
				selector: ':selected',
				css: {
					'background-color': 'black',
					'line-color': 'black',
					'target-arrow-color': 'black',
					'source-arrow-color': 'black'
				}
			}
		]
		

	});

	cy.on('tap', 'node', function (evt) {
		aardvark.processEvent(id, "clickNode", evt.target.id());
	});

	var pendings = {};

	nodes.onmessage = function (e) {
		var count = e.cnt;
		var node = JSON.parse(e.value); // decodeURIComponent(e);
		console.warn(count + "  " + node);
		if (count == -1) {
			var collection = cy.elements('node[id =\'' + node.data.id + '\']');
			cy.remove(collection);
		}
		else if (count == 1) {
			cy.add(node);
			cy.center();
			cy.elements().layout({ name: "dagre" });
			var removals = [];
			for (var key in pendings) {
				var p = pendings[key];
				var source = cy.elements('node[id =\'' + p.source + '\']');
				var target = cy.elements('node[id =\'' + p.target + '\']');
				if (source.empty() || target.empty()) {
					// later..
				}
				else {
					debugger;
					cy.add(p.edge);
					removals.push(key);
				}
			}
			removals.forEach(x => delete pendings[x]);

		}
	}

	edges.onmessage = function (e) {
		var count = e.cnt;
		var edge = JSON.parse(e.value); // decodeURIComponent(e);
		console.warn(count + "  " + edge);
		if (count == -1) {
			var collection = cy.elements('edge[id =\'' + edge.data.id + '\']');
			cy.remove(collection);
		}
		else if (count == 1) {
			var source = cy.elements('node[id =\'' + edge.data.source + '\']');
			var target = cy.elements('node[id =\'' + edge.data.target + '\']');
			var pending = { source: edge.data.source, target: edge.data.target, edge: edge };
			if (source.empty() || target.empty()) {
				pendings[edge.data.id] = pending;
			}
			else {
				cy.add(edge);
			}
		}
	};



	// testing code....

	let socket = new WebSocket("ws://localhost:4321/api/provenanceGraph");

	socket.onopen = function (e) {
		console.warn("[open] Connection established");
		console.warn("Sending to server");
	};

	socket.onmessage = function (event) {
		console.warn(`[message] Data received from server: ${event.data}`);


		socket.send("ok");
	};

	socket.onclose = function (event) {
		if (event.wasClean) {
			console.warn(`[close] Connection closed cleanly, code=${event.code} reason=${event.reason}`);
		} else {
			// e.g. server process killed or network down
			// event.code is usually 1006 in this case
			console.warn('[close] Connection died');
		}
	};

	socket.onerror = function (error) {
		console.warn(`[error]`);
	};
}
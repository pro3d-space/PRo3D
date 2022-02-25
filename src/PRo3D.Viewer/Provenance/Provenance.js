var tree = d3.tree();
var rootCurr = null
var root = null;
var json = null;
var padding = 15;
var gapLevel = 75;

function getX(x) {
    return padding + x;
}

function getY(y) {
    var container = $('.rootSvg').parent();
    return padding + y * (container.height() - padding * 2); 
}

function getWidth(x) {
    return x + 2 * padding;
}

function diagonal(d) {
    return 'M' + getX(d.x) + ',' + getY(d.y)
         + 'C' + getX((d.x + d.parent.x) / 2) + ',' + getY(d.y)
         + ' ' + getX((d.x + d.parent.x) / 2) + ',' + getY(d.parent.y)
         + ' ' + getX(d.parent.x) + ',' + getY(d.parent.y);
}

function diagonalInit(d) {
    var p = getPreviousPos(d.parent);

    return 'M' + getX(p.x) + ',' + getY(p.y)
         + 'C' + getX(p.x) + ',' + getY(p.y)
         + ' ' + getX(p.x) + ',' + getY(p.y)
         + ' ' + getX(p.x) + ',' + getY(p.y);
}

function translate(d) {
    return `translate(${getX(d.x)}, ${getY(d.y)})`;
}

function translateInit(d) {
    if (d.parent !== null) {
        var p = getPreviousPos(d.parent);
        return `translate(${getX(p.x)}, ${getY(p.y)})`;
    } else {
        return translate(d);
    }
}

function key(node) {
    return node.data.id;
}

function selected(d) {
    return d.data.id === json.current;
}

function referenced(d) {
    return d.data.isReferenced.toLowerCase() === 'true';
}

function clazz(n) {
    var cl = 'node';
    cl += selected(n) ? ' selected' : '';
    cl += referenced(n) ? ' referenced' : '';

    return cl;
}

function hovered(d) {
    return (d.data.id === json.highlight) || d3.select(this.parentNode).classed('hovered');
}

function shadow(d) {
    var id = null;

    if (selected(d)) {
        id = $('filter.shadowSelected').attr('id');
        
    } else if (hovered.call(this, d)) {
        id = $('filter.shadowHovered').attr('id');
    }

    return (id !== null) ? `url(#${id})` : null;
}

function radius(d) {
    return selected(d) ? 4 : 2;
}

function size(d) {
    return selected(d) ? 20 : 16;
}

function rectOffset(d) {
    return -size(d) / 2;
}

function mouseEnter(d) {
    d3.select(this)
      .classed('hovered', true)
      .selectAll('rect')
      .attr('filter', shadow);

    var recv = $('.provenanceView').attr('id');
    aardvark.processEvent(recv, 'onnodemouseenter', d.data.id);
}

function mouseLeave() {
    d3.select(this)
      .classed('hovered', false)
      .selectAll('rect')
      .attr('filter', shadow);

    var recv = $('.provenanceView').attr('id');
    aardvark.processEvent(recv, 'onnodemouseleave');
}

function click(d) {
    var recv = $('.provenanceView').attr('id');
    aardvark.processEvent(recv, 'onnodeclick', d.data.id);
}

function getPreviousPos(d) {
    function rec(d, n) {
        if (n.data.id === d.data.id) {
            return { x: n.x, y: n.y };
        }

        if (typeof n.children !== 'undefined') {
            for (var i = 0; i < n.children.length; i++) {
                var p = rec(d, n.children[i]);

                if (p !== null) {
                    return p;
                }
            }
        }

        return null;
    }

    if (rootCurr !== null) {
        var p = rec(d, rootCurr);
        return (p !== null) ? p : { x: d.x, y: d.y };
    } else {
        return { x: d.x, y: d.y };
    }
}

function update(data) {
    // Parse json string
    try {
        json = JSON.parse(data);
    } catch (err) {
        console.error(`${err.message}: '${data}'`);
        return;
    }

    // Create tree hierarchy
    var nodes = d3.hierarchy(json.tree);
    root = tree(nodes);

    // Swap x / y and make gap between levels constant
    // Set width for svg element accordingly
    root.descendants().forEach(function (d) {
        d.y = d.x;
        d.x = d.depth * gapLevel;
        $('.rootSvg').width(getWidth(d.x));
    });

    // Draw tree
    redraw();

    // Save the data so we can find the previous position of the parent of a new node
    rootCurr = root;
}

function redraw() {
    if (root === null) {
        return;
    }

    // Update, add and remove links
    var link = d3.select('.linkLayer')
                 .selectAll('.link')
                 .data(root.descendants().slice(1), key)

    var linkEnter =
        link.enter()
            .append('path')
                .attr('class', 'link')
                .attr('d', diagonalInit);

    linkEnter.transition()
        .attr('d', diagonal);

    link.exit().remove();

    link.transition()
        .attr('d', diagonal);

    // Update, add and remove nodes
    var node = d3.select('.nodeLayer')
                 .selectAll('.node')
                 .data(root.descendants(), key);

    // Enter
    var nodeEnter =
        node.enter()
            .append('g')
                .attr('class', clazz)
                .attr('transform', translateInit)
                .on('click', click)
                .on('mouseenter', mouseEnter)
                .on('mouseleave', mouseLeave);

    nodeEnter
        .append('rect')
            .attr('filter', shadow)
            .attr('x', rectOffset)
            .attr('y', rectOffset)
            .attr('rx', radius)
            .attr('ry', radius)
            .attr('width', size)
            .attr('height', size);

    nodeEnter
        .append('text')
            .attr('text-anchor', 'middle')
            .attr('dominant-baseline', 'central')
            .text(function (d) {
                return d.data.msg;
        });

    nodeEnter.transition()
        .attr('transform', translate);

    // Exit
    node.exit().remove();

    // Update
    node.attr('class', clazz);

    node.transition()
        .attr('transform', translate);

    node.selectAll('rect')
        .attr('filter', shadow)
        .transition()
            .ease(d3.easeElastic.amplitude(2))
            .duration(1000)
                .attr('x', rectOffset)
                .attr('y', rectOffset)            
                .attr('rx', radius)
                .attr('ry', radius)            
                .attr('width', size)
                .attr('height', size);                
}

window.addEventListener('resize', redraw);

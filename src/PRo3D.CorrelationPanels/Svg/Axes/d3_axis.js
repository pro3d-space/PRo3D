function drawScaleX (id, data) {

	var pos = data[0]
	var dom = data[1]
	var dim = data[2]

	var xScale = d3.scaleLog()
    	.domain([dom.X, dom.Y])    
    	.range([dim.X, dim.Y]); 

	// function formatTick(d) {
	// 	const s = d3.format('~i')(d);
	// 	return this.parentNode.nextSibling ? `\xa0${s}` : `${s} μm`;
	//   }

	var axis = d3.axisBottom(xScale)
		.tickSize(6)
		.ticks(6, "~i");
	//.tickFormat(formatTick)

	d3.select(`#${id}`)
	.attr("transform", `translate(${pos.X},${pos.Y})`)
	.call(axis).selectAll("text")
		.style("text-anchor", "start")
        .attr("dx", ".8em")
        .attr("dy", "-.1em")
        .attr("transform", "rotate(65)");
}

function drawScaleY (id, data) {

	var pos = data[0];
	var dom = data[1];
	var dim = data[2];

	var yScale = d3.scaleLinear()
    	.domain([dom.Y, dom.X])    
    	.range([dim.X, dim.Y]); //.nice(); 

	function formatTick(d) {
	  const s = d3.format('.2f')(d);
	  return this.parentNode.nextSibling ? `\xa0${s}` : `${s} m`;
	}

    var axis = d3.axisLeft(yScale)
    	.tickFormat(formatTick);

	d3.select(`#${id}`)
		.attr("transform", `translate(${pos.X},${pos.Y})`)
		.call(axis);
}
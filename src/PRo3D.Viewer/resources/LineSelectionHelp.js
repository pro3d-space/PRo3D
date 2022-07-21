

transform_to_svgCoords = function (event, item) {
    var svg = document.getElementsByClassName(item)[0];
    var pt = svg.createSVGPoint();
    pt.x = event.clientX;
    pt.y = event.clientY;

    var transformed = pt.matrixTransform(svg.getScreenCTM().inverse());
    return transformed;
}


Next we need a tool which draws a surface profile as vertical cross section into an image. The tool should take:
 - the height of the cross section curtain (in meters), potentially overshooting the surface
 - the minimmum altitude of the cross section (in meters)
 - the profile (look at testProfile.csv). it is basically a line on the surface consisting of points. each point knows its rolled out distance to the beginning of the cross section and the altitude.
 - the vertical texture size in pixels


The output should be an image whith proper aspect (computed from real diemsions and the vertical texture size) with the surface (drawn as line) as well as a coordinate system showing distance to starting point (left most) and vertical depth (altitude) as a scale.

What tool (maybe simple python program?) whould be good to use?
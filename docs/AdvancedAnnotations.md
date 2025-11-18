## Advanced Annotation & Preview picking


Previously PRo3D provided no feedback where the intersection will happen. This one adds a 3D cursor which is continously computes intersections given cursor movements.
It runs in a background thread.

The feature can be explicityly disabled via `--disablePreviewIntersections`


## Caveats

Additional memory footprint due to stressed kdtre cache. The kdtree cache however should be reworked to use a LRU queue or similarly.


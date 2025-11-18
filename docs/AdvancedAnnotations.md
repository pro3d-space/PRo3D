## Advanced Annotation & Preview picking


Previously PRo3D provided no feedback where the intersection will happen. This one adds a 3D cursor which is continously computes intersections given cursor movements.
It runs in a background thread.

The feature can be explicityly disabled via `--disablePreviewIntersections`

Here is the additional annotation type: https://github.com/pro3d-space/PRo3D/pull/542/commits/54ff51f2545b7727b8bd2121a94a3a90a5d35c83#diff-e3ae8667e8128f79e46ea606a6958260ad62ddc73b7e187fccace130e9f14a01R33

Preview intersections can be controlled via: https://github.com/pro3d-space/PRo3D/pull/542/commits/54ff51f2545b7727b8bd2121a94a3a90a5d35c83#diff-5b8179e37acaceeb2a6bf8b9ce4ceedf9785501303594064bbee53391179b622R88


Async preview pickign is controlled via `PreviewPickSurface` message, see https://github.com/pro3d-space/PRo3D/pull/542/commits/c7435faa1963e592cdd5829157b4c804744d40d9#diff-0e871cb4421e88e1ad3efc5e947173c95acb60eedb089a13a0477143e2199f11R516.

## Caveats

Additional memory footprint due to stressed kdtre cache. The kdtree cache however should be reworked to use a LRU queue or similarly.


# PRo3D Notebook API

Generally there are two methods for working with pro3d in notebook setups:
 1. use pro3d lib directly via polyglot notebooks with F# kernels
 2. use the pro3d api and access pro3d functionality in a language agnostic manner (e.g. using vanilla python jupyter notebooks)

Here, we focus on (2). Technically, pro3d runs as a sever application and per default runs on ```http://localhost:4321```. 

By using the command line option ```--port 4321 --remoteApi``` the remote api can be accessed here ```http://localhost:4321/api```

Example notebooks are given in this directory.

Further, important endpoints are:
 - `/api/loadScene`
 - `/api/importOpc`
 - `/api/saveScene`
 - `/api/discoverSurfaces`
 - `/api/v2` (for procenance tracking)
    - `captureSnapshot`
    - `activateSnapshot`
    - `getProvenanceGraph`
    - `provenanceGraph` (websocket)
- `/integration`
    - `/geojson_latlon`
    - `ws/geojson_xyz`

The full and sketchy/experimental implementation can be found here: https://github.com/pro3d-space/PRo3D/blob/features/provenanceNew/src/PRo3D.Viewer/RemoteApi.fs#L495
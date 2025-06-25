### Rendering Priorites for Traverses


This feature allows to specify depth composition of traverse visualizations with surfaces.
Due to the interleaved way of how priorites are handled in PRo3D (for historical reasons), this feature quite complex.


Generally surfaces are grouped by priorities and rendered sequentially. Each traverse has now a setting for its priority and whether the priority setting should be used.

See: [traverse ui](../src/PRo3D.Viewer/TraverseApp.fs#L69)

The interleaving takes place 
 1. here interleaved with the [surfaces](https://github.com/pro3d-space/PRo3D/blob/1bfdf40dab3b1fd19699ec964db14e7cb387b9d2/src/PRo3D.Viewer/Viewer/Viewer-Utils.fs#L1045)
 2. and [here](https://github.com/pro3d-space/PRo3D/blob/5e6434ef1004fa9d47e780194bb361d73d3033c2/src/PRo3D.Viewer/Viewer/Viewer.fs#L2011) for traverses which either have no priority or have a wrong priority, thus are correctly depth composed on top of the highest priority surface (rendered last).

 In (1), the rendering always groups together the surfaces of the particular group (given by priority) and the traverse. This means at the end, all traverses have been rendered if they belong to a group.
 Traverses without a matching group (user used a priority not provided assigned to a surface also), would fall through.
 [This](https://github.com/pro3d-space/PRo3D/blob/1bfdf40dab3b1fd19699ec964db14e7cb387b9d2/src/PRo3D.Viewer/TraverseApp.fs#L860) complex visibility mechanism makes sure that surfaces which have a prority enabled, but have not been rendered yet, get a second chance. The particular trick is [here](https://github.com/pro3d-space/PRo3D/blob/1bfdf40dab3b1fd19699ec964db14e7cb387b9d2/src/PRo3D.Viewer/TraverseApp.fs#L869). 
 The general scheme is that, in (1) the view funciton is provided with this [parameter](https://github.com/pro3d-space/PRo3D/blob/1bfdf40dab3b1fd19699ec964db14e7cb387b9d2/src/PRo3D.Viewer/TraverseApp.fs#L852). [Here](https://github.com/pro3d-space/PRo3D/blob/1bfdf40dab3b1fd19699ec964db14e7cb387b9d2/src/PRo3D.Viewer/Viewer/Viewer-Utils.fs#L1055) with the priority of the group and in (2) with none.

 Unfortunately there is code duplication in the snapshots functionality. The scene graph construction there is in very bad shape. Due to time constraints, i tweaked it to work the same way as the viewer. E.g. [here](https://github.com/pro3d-space/PRo3D/blob/1bfdf40dab3b1fd19699ec964db14e7cb387b9d2/src/PRo3D.Viewer/Viewer/SnapshotSg.fs#L378) and in the surface construction code (look for TravereseApp.Sg.view as always).
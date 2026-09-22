picking via kdtree.

we are working in C:\Users\haral\Desktop\pro3d\pro3d-6\src\OpcViewer\MultiTexturingViewer.fs
please investigate how picking can be done using kdtrees. in the pro3d code there is already pickign via kdtrees. the only difficult part is to find the kdtree. for this look into "C:\Users\haral\Desktop\aardvark\OpcViewer\src\OPCViewer.Base\KdTrees.fs"
as well as C:\Users\haral\Desktop\pro3d\pro3d-6\src\PRo3D.Viewer\Viewer\Picking.fs
for discovering the kdtrees i think you need to load the kdtree for each of the patch hierarchies. 
maybe also look for  ViewerAction.PreviewPickSurface in Viewer.fs of pro3d code. this might also give a hint.
so given the callback for double click which currently is doing the picking, additionally do it via kdtrees and check whether the results are approximately the same. this will be the basis for further work. 
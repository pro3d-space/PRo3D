

The code which prepares surfaces for rendering is here: src\PRo3D.Core\Surface\Surface.Sg.fs (PathNode constructor). In the test viewer there is the same, "C:\Users\haral\Desktop\pro3d\PRo3D.CrossSections\src\SimpleViewer\TestViewer.fs"

the task is now to integrate the rendering into real pro3d. Thus, given the annoaation with cross section enabled (should only work for line annoations), take it, project it onto a plane and create a polygon on the plane. use hte current view.locaiton as reference point. the plane is defined by the first point of the annoation point and the normal comes from cootransformation to get the up vector. given this, you need to add another new vertex attribute which for each vertex computes whehter the projected vertex is in the polygon and pass -1.0 or 1.0 to the shader. 
we will review this and continue then.

make sure not to only take stuff from testViewer which is relevant and better ask before guessing how it should work
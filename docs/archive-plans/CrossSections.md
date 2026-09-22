The goal of this task is to implement cross sections in pro3d. the concept work was already done here: "C:\Users\haral\Desktop\pro3d\PRo3D.CrossSections\src\SimpleViewer\TestViewer.fs"

the concept is. given an annotaiton line, use it as a cross section gemoetry (similar to the line in the test viewer). For users this needs to be enabled per annotatation. 

The main steps are:
 - add a on/off switch for "cross section clipping" in the annoation gui: src\PRo3D.Core\Drawing\Drawing.UI.fs. to do this it needs to be added to the model (i think it is here: C:\Users\haral\Desktop\pro3d\PRo3D-5\src\PRo3D.Core\Drawing\Drawing-Model.fs). after the modification in the model make sure to run .\adapt.cmd in the root folder to generate the updated .g files.

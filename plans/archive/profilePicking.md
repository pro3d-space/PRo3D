goal if this project is to extend src\OpcViewer\MultiTexturingViewer.fs with readback based picking (retrieving 3d coordinates under cursor) to draw lines on the surfaces. 
the whole concept has been implemented here: "C:\Users\haral\Desktop\pro3d\PRo3D.CrossSections\src\SimpleViewer\SimpleViewer.fs".
please add the renderToColorAndDepth part and the line drawing from the simpleviewer example (this one is very bloated, make sure to jsut take the relevant parts)
you can run the application using dotnet run on src\OpcViewer\OpcViewer.fsproj
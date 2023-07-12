Here we brievly explain how SPICE interacts with PRo3D:

 - SPICE, CooRegistration and InstrumentPlatforms in PRo3D is provided by the dotnet wrapper which can be found here: https://github.com/pro3d-space/PRo3D-Extensions. 
 - Coordinate system transformations in PRo3D itself are mainly found here: https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/src/PRo3D.Base/CooTransformation.fs#L120 Those are just calls into the library mentioned above.
 - Initialization also takes place here: https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/src/PRo3D.Base/CooTransformation.fs#L64 and exposes how concrete spice kernels are loaded for pro3d. [here](https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/src/PRo3D.Base/CooTransformation.fs#L70) for example, Spice kernels are unpacked to the common AppData directory. This way, spice configuration can be changed without recompiling PRo3D.
 - In order to ship PRo3D with adapted SPICE kernels, the files [here](https://github.com/pro3d-space/PRo3D/tree/main/src/PRo3D.Base/resources) need to be updated accordingly (and the zip repacked).



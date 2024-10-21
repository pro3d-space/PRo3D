
## SPICE 

This refers to the current state as of march 2024 after reworking SPICE integration. 


```mermaid
flowchart LR
    spice["SPICE Toolkit"] --> cppwrapper
    cppwrapper["DaKup/PRo3D-Extensions"] --> pro3dspice
    pro3dspice["pro3d-space/PRo3D.SPICE"] --> pro3dviewer
    pro3dviewer["PRo3D.Viewer"]
    click spice "https://naif.jpl.nasa.gov/naif/toolkit.html" "The SPICE Toolkit"
    click wrapper "https://github.com/pro3d-space/PRo3D.SPICE" "look at the repository on github"
    click cppwrapper "https://github.com/DaKup/PRo3D-Extensions" "look at the repository on github"
```

The components are:
 1. The SPICE toolkit at [https://naif.jpl.nasa.gov/naif/toolkit.html](https://naif.jpl.nasa.gov/naif/toolkit.html)
 2. The CPP lib which wrapps some SPICE functionality and [JR](https://www.joanneum.at/)'s functionality. This one is deployed for all platforms using [github actions](https://github.com/DaKup/PRo3D-Extensions/actions).
 3. The [PRo3D.SPICE](https://github.com/pro3d-space/PRo3D.SPICE) repository provides a dotnet wrapper for the c++ lib and deploys itself via [github actions](https://github.com/pro3d-space/PRo3D.SPICE/actions) to a [nuget](https://www.nuget.org/packages/PRo3D.SPICE) package which works on all supported platforms.
 4. In this repository the nuget library is consumed [paket.dependencies](https://github.com/pro3d-space/PRo3D/blob/392fd2723bd66aca34c076c5d344fcb99f5d1b34/paket.dependencies#L71) using the paket (standard pro3d package management).


 Caveats:
  - A legacy library for working with instruments is still handled directly via pro3d (JR.Wrappers provides the wrapper). This can be subsumed by SPICE the functionality is ready.


## Details

Here we brievly explain how SPICE interacts with PRo3D:

 - SPICE, CooRegistration and InstrumentPlatforms in PRo3D is provided by the c++ lib which can be found here: https://github.com/pro3d-space/PRo3D-Extensions. 
 - Coordinate system transformations in PRo3D itself are mainly found here: https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/src/PRo3D.Base/CooTransformation.fs#L120 Those are just calls into the library mentioned above using dllimport for wrapping the necessary functions.
 - Initialization also takes place here: https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/src/PRo3D.Base/CooTransformation.fs#L64 and exposes how concrete spice kernels are loaded for pro3d. [here](https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/src/PRo3D.Base/CooTransformation.fs#L70) for example, Spice kernels are unpacked to the common AppData directory. This way, spice configuration can be changed without recompiling PRo3D.
 - In order to ship PRo3D with adapted SPICE kernels, the files [here](https://github.com/pro3d-space/PRo3D/tree/main/src/PRo3D.Base/resources) need to be updated accordingly (and the zip repacked).



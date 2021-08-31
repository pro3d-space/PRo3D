## 4.1.0

- using methods as described in `Quinn, D. P., & Ehlmann, B. L. (2019). A PCA‐based framework for determining remotely sensed geological surface orientations and their statistical quality. Earth and Space Science, 6(8), 1378-1408.`
  - using new method for plane fitting
  - added angular error measures
  - added versioned serialization and deserialzation
  - added export of dip and strike annotations as json for attitude integration (annotions>export>attitude planes (*.json))

## 4.0.1

- fixed "flyto animation does not reach destination" issue
- fixed issue with obj annotation when using flipZ https://github.com/pro3d-space/PRo3D/issues/98

## 4.0.0

contains features and fixes from all prereleases since 3.3.1

- features
  - Scalebar integration https://github.com/vrvis/PRo3D/issues/10
  - Import and Visualization of "SceneObjects" https://github.com/vrvis/PRo3D/issues/13
  - Geologic Surface Creation https://github.com/vrvis/PRo3D/issues/19
  - Mastcam-Z Improvements https://github.com/vrvis/PRo3D/issues/53
  - Adjust Focal Length https://github.com/vrvis/PRo3D/issues/54
  - Flip Z direction for surfaces and scene objects
  - Improved annotations performance https://github.com/vrvis/PRo3D/issues/60
  - new color correction UI + ordering of operations https://github.com/vrvis/PRo3D/issues/52
  - annotation picking
    - added picking tolerance in meters for annotation picking to viewconfig to address picking problems at orbital scale.
    - this tolerance does not affect the accuracy of the line picking itself
    - it rather affects performance: having a large tolerance for small scale scenes may result in unnecessary intersections tests
    - picking tolerance is serialized with view config (now version 2), older versions will be intialized with 0.01m
    - numeric control for picking tolerance directly next to interaction dropdown with text "eps.:"
    - removed unnecessary surface intersection computation when trying to pick annotations
    - added pixel-based real-time highlighting when `PickAnnotation` is active
  - annotation properties
    - added properties tab to docking GUI
    - currently only shows annotation properties when an annotation is selected
    - this will not show in already existing scenes. to reset the docking GUI and make properties visible press <kbd>F8</kbd> and save your scene afterwards.
  - geoJson export
    - added geoJSON export via menu > annotations > export xyz (*.json)
    - added geoJSON export via menu > annotations > export (*.json)
    - data is exported as `geometryCollection` of `geometry` objects with 3D coordinates `(lon, lat, alt)` computed via SPICE
  - coordinate prints
    - added print location / coordinate for point annotation
    - added print location / coordinate for bookmark location
    - added long lat alt prints via cootrafo
  - mac support (preliminary, unofficial, testing only)
- fixes
  - fixed screen space scaling of annotation spheres
  - changed lod metric to omit aggressive culling
  - added missing c libs on windows
  - updated CooTrafo build to release - should fix missing ucrtbase
  - fixed crash with goto animation and arc ball controller
  - fixed UNC aardium problem
  - current directory now set to main entry point location https://github.com/vrvis/PRo3D/issues/63, https://github.com/vrvis/PRo3D/issues/62
  - aardium path fix
  - Update navigationMode and exploreCenter in SaveScene and LoadScene
  - Import obj
  - LatLonAlt output sequence changed
  - isSurfaceFolder is always false for objs (in Surfaces) so the red exclamation icon in the little surface menu is shown  
  - fixed picking issues
  - tried to fix color picker history https://github.com/vrvis/PRo3D/issues/56
  - fixed dns colors
  - performance improvements for dns annotations
  - fixed multiselect
  - fixed duplication bug when moving multiple annotations via "select all" in a group mechanic
  - "select all" only selects all leaves in a group not including the leaves of sub groups
  - fixed problem with picking points on surface

## 4.0.0-prerelease6

- updated CooTrafo build to release - should fix missing ucrtbase

## 4.0.0-prerelease5

## 4.0.0-prerelease4

- fixed crash with goto animation and arc ball controller
- fixed screen space scaling of annotation spheres
- changed lod metric to omit aggressive culling
- added missing c libs on windows
- mac support (unofficial, testing only)

## 4.0.0-prerelease3

## 4.0.0-prerelease2

## 4.0.0-prerelease1

## 3.8.0-prerelease3

- fixed UNC aardium problem

## 3.8.0-prerelease3

- current directory now set to main entry point location https://github.com/vrvis/PRo3D/issues/63, https://github.com/vrvis/PRo3D/issues/62

## 3.8.0-prerelease2

- aardium path fix

## 3.8.0-prerelease1

- features:
  - Scalebar integration https://github.com/vrvis/PRo3D/issues/10
  - Import and Visualization of "SceneObjects" https://github.com/vrvis/PRo3D/issues/13
  - Geologic Surface Creation https://github.com/vrvis/PRo3D/issues/19
  - Mastcam-Z Improvements https://github.com/vrvis/PRo3D/issues/53
  - Adjust Focal Length https://github.com/vrvis/PRo3D/issues/54
  - Flip Z direction for surfaces and scene objects
  - Super slow annotations https://github.com/vrvis/PRo3D/issues/60
- bugfixes:
  - Update navigationMode and exploreCenter in SaveScene and LoadScene
  - Import obj
  - LatLonAlt output sequence changed
  - isSurfaceFolder is always false for objs (in Surfaces) so the red exclamation icon in the little surface menu is shown

## 3.7.0-prerelease6

- fixed picking issues
- tried to fix color picker history https://github.com/vrvis/PRo3D/issues/56

## 3.7.0-prerelease5

- new color correction UI + ordering of operations https://github.com/vrvis/PRo3D/issues/52
- fixed dns colors

## 3.7.0-prerelease4

- new color correction UI + ordering of operations https://github.com/vrvis/PRo3D/issues/52
- fixed dns colors

## 3.7.0-prerelease3

- added geoJSON export via menu > annotations > export xyz (*.json)

## 3.7.0-prerelease2

- performance improvements for dns annotations
- fixed multiselect

## 3.7.0-prerelease1

- added geoJSON export via menu > annotations > export (*.json)
- data is exported as `geometryCollection` of `geometry` objects with 3D coordinates `(lon, lat, alt)` computed via SPICE

## 3.6.1-prerelease1

- performance improvements take I

## 3.6.0-prerelease1

- fixed duplication bug when moving multiple annotations via "select all" in a group mechanic
- "select all" only selects all leaves in a group not including the leaves of sub groups

## 3.5.1-prerelease1

- added print location / coordinate for point annotation
- added print location / coordinate for bookmark location
- added long lat alt prints via cootrafo
## 3.4.1-prerelease2

- fixed problem with picking points on surface

## 3.4.1-prerelease1

- added properties tab to docking GUI
- currently only shows annotation properties when an annotation is selected
- this will not show in already existing scenes. to reset the docking GUI and make properties visible press F8 and save your scene afterwards.

## 3.4.0-prerelease2

* added picking tolerance in meters for annotation picking to viewconfig to address picking problems at orbital scale.
  * this tolerance does not affect the accuracy of the line picking itself
  * it rather affects performance: having a large tolerance for small scale scenes may result in unnecessary intersections tests
* picking tolerance is serialized with view config (now version 2), older versions will be intialized with 0.01m
* numeric control for picking tolerance directly next to interaction dropdown with text "eps.:"
* removed unnecessary surface intersection computation when trying to pick annotations

## 3.3.1

* csv export contains visible annotations only
* also added manualDipAngle and trueThickness result to export (NaN of not applicable to annotation)
* fixed error in AnnotationResultsSerialization
* single select annotations / multi select with ctrl + shift
* reactivated tooltips by default, -notooltip in command line supresses all tooltip displays
* fixed annotation flyto (could not reproduce anymore)
* added true thickness measurement tool TT to DrawAnnotation Geometry
* added units to measurement and dip and strike results
* removed positions from measurement properties
* changed direction of vertical distance computation

(contains changes from 3.1.4 prereleases)

## 3.2.1-prerelease

* test prerelease


## 3.2.0
* new deploy system 


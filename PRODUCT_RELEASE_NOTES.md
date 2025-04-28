## 5.0.0
- next major release preparation

## 4.26.0-prerelease1
- fixed surface priority
- improved handling of large trajectories
 
## 4.25.0-prerelease7
- distance- and trianglefilter fix
- fixed problem with sequenced bookmarks performance problem when a traverse is loaded
- fixed obj not being rendered with batch rendering

## 4.21.0-prerelease3
- bugfix contour lines

## 4.21.0-prerelease2
- Bugfix Entity creation
- Added new default Entities
- Spice Kernel is now loaded when a scene is loaded and a spice kernel is defined in GisApp

## 4.21.0-prerelease1
- added readme to opc-tool

## 4.24.0
- streamlined up kdtree loading

## 4.23.2   
- fixed sequenced bookmark loading in cross-platform scenarios https://github.com/pro3d-space/PRo3D/pull/391

## 4.23.1
- tweaked kdtree split limit epsilon for smaller kdtrees

## 4.23.0
- tweaked kdtree split limit epsilon for smaller kdtrees


## 4.22.0
- further improved kdtree loading on NTFS/macbook

## 4.21.0-prerelease3
- further improved kdtree loading on NTFS/macbook

## 4.21.0-prerelease2
- fixed kdtree loading on NTFS/mac
 
## 4.21.0-prerelease1
- added support to re-create kdtrees 
 
## 4.20.2
- opc tool now supports "ignoreMasterKdTree" option which can be used to force leaf kdtree construction

## 4.20.1
- added readme to opc-tool

## 4.20.0
- added opc tool

## 4.2.0-prerelease1
- added rake to annotations

## 4.20.0-prerelease1
- provex and multitexturing

## 4.12.0-prerelease10
- new trafo version
- distance filter for surface
- increased minimum value for depth image colors 

## 4.12.0-prerelease9
- bugfix sequenced bookmarks paths

## 4.12.0-prerelease8
- various fixes for focal length, batch rendering
- radiometry calculation changed

## 4.12.0-prerelease7
- #261 Zooming enhancements
- #167 radiometry

## 4.12.0-prerelease6
- show depth (+ gui and legend) in instrument view
- #234, snapshots: --renderDepth writes a depth image as tiff
- bugfix: #329 obj without textures import
- bugfix: #157 planet reset stopped
- bugfix: #324 traverse updates
- #314: surface transformation with pivot revised
- bugfix for "save footprint"

## 4.12.0-prerelease5
- opc rendering now works on linux

## 4.12.0-prerelease4
- opc rendering now works on linux

## 4.12.0-prerelease3
- workaround for case-sensitivity problem in isOpc: https://github.com/pro3d-space/PRo3D/issues/280

## 4.12.0-prerelease2
- testing linux deployment

## 4.12.0-prerelease1
- testing linux deployment

## 4.11.1
- #110, #126, #138, #144, #145, #166, #212, #231, #259,
- Transformations, hide exploration center, visibility of annotations- and scalebar text

## 4.11.0-prerelease4
- NewScene crash fixed: https://github.com/pro3d-space/PRo3D/issues/277

## 4.11.0-prerelease3
- #274: objs with multiple geometries fixed

## 4.10.3
- #179 and #246: serialization of viewplans
- #256: load scenes with wrong obj path and possibility to reload the obj

## 4.10.2
- build kdtrees for objs from faces of triangulated mesh copy for #264
- bugfix for large coordinates 
- load obj without textures; show vertex colors instead

## 4.10.1
- making projection measurments fit for profile extractions #247 containing to following features
  - exported projection measurements contain all sampling points
  - when creating a projection measurement users can control the sampling rate #203
  - the selected annotation can be exported as csv in the for of absolute elevation over distance #221
- also supports MSL traverse ingestion

## 4.10.0
- added configurable sampling scheme to measurement projections (viewpoint, sky) as requested in #203

## 4.9.7
- bugfix: near/farplane not set correctly in batch rendering [#241](https://github.com/pro3d-space/PRo3D/issues/241)

## 4.9.5
- fixed kdtree paths on osx
- switched to dotnet6 

## 4.9.4-prerelease3
- testing autodeploy

## 4.9.2-prerelease5
- bugfix: frustum now set correctly when batch rendering


## 4.9.1-prerelease3
- bugfix: frustum for batch rendering
- removed recording of animation, replaced with saving batch file directly (no looping, easing, splines, global animation for batch rendering)
- now allowing saving and restoring scene state for sequenced bookmark animations
- scene states for animation are now identified with data and time and listed under properties of sequenced bookmarks
- added traverses to scene state
- new import for objs with large coordinates

## 4.9.1-prerelease2
 
- bugfix: reading scene with sequenced bookmarks could lead to an error
- bugfix: scale bars not updated correctly when updating scene state for sequenced bookmarks

## 4.9.1-prerelease1

- sequenced bookmarks can now store scene state
- new animation features (easing, smooth path, looping, scene state is applied according to bookmarks)
- batch rendering can now use sequenced bookmarks and scene state
- anti-alisaing for batch rendering
- bugfix: wrong path opening when clicking on batch rendering output path

## 4.9.0-prerelease1

- geoJSON exports now contain sampled points of visible annotations [#217](https://github.com/pro3d-space/PRo3D/issues/217)

## 4.8.2-prerelease1
 
- electron build test   
   
## 4.7.0-prerelease1  

- added Continuous Export of Dip & Strike [#185](https://github.com/pro3d-space/PRo3D/issues/185)
- added Custom Background Color for Screenshots [#183](https://github.com/pro3d-space/PRo3D/issues/183)
- Integration of Mars2020 rover traverse [#127](https://github.com/pro3d-space/PRo3D/issues/127) also including custom sized waypoint labels [#154](https://github.com/pro3d-space/PRo3D/issues/154)

## 4.6.2-prerelease1

- adapted GeoJSON parser to read numeric values
- added file dialog to import traverses in the form of the specified GeoJSON M20_waypoints.json
- added datamodel and GUI to maintain multiple traverses
- added adjustable textsize for waypoints

## 4.6.1-prerelease2

- base 5.2 upgrade

## 4.6.1-prerelease1

- added viewplanner placement for traverse waypoints
- changed text positions for all annotations to center of the object
- fixed instrument view text scaling bug
- added waypoints file to resources

## 4.6.0-prerelease1

- revived viewplanner and footprint projection
- fixed triangle filter (was in projective space)
- added sequenced bookmarks (from other branch)

## 4.5.0-prerelease1

- added traverse loading and visualization (dots, text, lines)
- added local reference frames according to rover poses
- added flyto animation according to rover poses
- added list gui for sols and traverse visibility flags

## 4.4.4-prerelease1

- added csv export for vertical thickness computation
- added csv export of angular error values for dns computation

## 4.4.3-prerelease1

- added vertical thickness computation for TT (True Thickness) annotation tool

## 4.4.2-prerelease1

- added recalculation of all angular values dependent on north and up (dip and strike angle and azimuth, bearing, slope)

## 4.4.1-prerelease1

- fixed "box sequence must not be empty" exception when loading a scene with surfaces that have faulty paths

## 4.4.0-prerelease1

- added dip azimuth to true thickness tool [#17](https://github.com/pro3d-space/PRo3D/issues/17)
  - improved true thickness computation via point over plane height
- merged xzy coordinate system and renamed it to ENU (East North Up) [#117](https://github.com/pro3d-space/PRo3D/issues/117)
- added missing calculation numbers of measurements to csv export (slope, bearing, vertical distance, horizontal distance) [#100](https://github.com/pro3d-space/PRo3D/issues/100)
- added `showText`flag to annotations to show or hide text [#114](https://github.com/pro3d-space/PRo3D/issues/114)

## 4.3.0-prerelease1

- added xzy coordinate system and `sketchfab` transformation to support models created out of agisoft

## 4.2.0-prerelease1

- static screen-shot service that can be found in the `config` tab
- coordinate systems are inferred automatically mostly to distinguish between elipsoid (Mars, Earth) and Euclidean (None, JPL) / Rover Frame Systems

## 4.1.0-prerelease2

- fixed broken priority rendering

## 4.1.0-prerelease1

- using methods as described in `Quinn, D. P., & Ehlmann, B. L. (2019). A PCA‐based framework for determining remotely sensed geological surface orientations and their statistical quality. Earth and Space Science, 6(8), 1378-1408.`
  - using new method for plane fitting
  - added angular error measures
  - added versioned serialization and deserialzation
  - added export of dip and strike annotations as json for attitude integration (annotions>export>attitude planes (*.json))

## 4.0.3

* fixed arcball crash
* macbook pro amd graphics support
* removed automatic recent loading

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


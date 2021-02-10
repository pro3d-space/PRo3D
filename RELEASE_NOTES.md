## 3.4.1-prerelease1

* added properties tab to docking GUI
* currently only shows annotation properties when an annotation is selected
* this will not show in already existing scenes. to reset the docking GUI and make properties visible press F8 and save your scene afterwards.

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




====================
Add VRVis NuGet Feed
====================
All VRVis packages are now available via VRVis' own NuGet feed.
In order to use it you have to add the feed on your machine as follows:

(1) open a console window ...
(2) > cd [pro3d]
(3) > git pull
(4) > .paket\paket.exe config add-credentials https://vrvis.myget.org/F/aardvark


=============================================
Restore/Update Packages from VRVus NuGet Feed
=============================================
(1) open a console window ...
(2) > cd [pro3d]
(3) > git pull
(4) > build restore


==========================
Develop with Visual Studio
==========================
(1) Use src/Aardvark.OpcViewer.sln  to build and develop.

Prerequisites:
* Visual Studio 2015 and F# Tools


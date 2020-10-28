![](http://www.pro3d.space/images/garden.jpg)

**PRo3D**, short for **P**lanetary **Ro**botics **3D** Viewer, is an interactive 3D visualization tool allowing planetary scientists to work with high-resolution 3D reconstructions of the Martian surface.
Additional information can also be found on the [PRo3D Homepage](http://pro3d.space).


# Who uses PRo3D?

PRo3D aims to support planetary scientists in the course of NASA's and ESA's missions to find signs of life on the red planet by exploring high-resolution 3D surface reconstructions from orbiter and rover cameras.

Planetary geology is the most elaborately supported use-case of PRo3D, however we strive to expand our user groups to other use-cases, so we have also developed features for supporting science goals in **landing site selection** and **mission planning**.

# Features

* Geological analysis of 3D digital outcrop models
* Large data visualization
* Overlaying of arbitrary 3D surfaces

# Licensing

PRo3D is **free** for academic use. When used for publications, we kindly ask for [PRo3D Homepage](http://pro3d.space). For commercial use, customization, please consult science@vrvis.at.

# Technology & System Requirements

Current status of the repository: ![Windows](https://github.com/vrvis/PRo3D/workflows/Windows/badge.svg). Mac and windows support is currently under development: ![Linux](https://github.com/vrvis/PRo3D/workflows/Linux/badge.svg)   ![Mac OS](https://github.com/vrvis/PRo3D/workflows/MacOS/badge.svg)

PRo3D is based on the functional-first libraries of the [The Aardvark Platform](https://aardvarkians.com/), available on [github](https://github.com/aardvark-platform). In December, we will finish the final bits for mac os finally making the application fully cross-platform.

_required:_

CPU: Intel i5 or AMD equivalent
GPU: dedicated GPU, NVIDIA Geforce 700s Series or greater
RAM: 8 GB

_recommended:_

CPU: Intel i7 or AMD equivalent
GPU: NVIDIA Geforce 1650GTX or AMD equivalent
RAM: 16GB

_technological constraints:_

OS/Runtime: Windows 10 (64bit, v10.0.17763), .NET Core 3.1 (linux and mac versions are in development)
Graphics: NVIDIA Kepler Architecture (GTX 6*) or greater

PRo3D's performance may vary with the size and type of datasets and the selected quality settings for surface rendering. PRo3D may as well run on machines beneath the required specification. Most of the time, PRo3D also runs on AMD cards, but it is not guaranteed.

# Getting started from pre-built binaries

Demo data and the pre-built application can be found on our release page: [TODO](TODO).

A video-based introduction to PRo3D can be found in the [Getting Started](http://www.pro3d.space/#started) section of [PRo3D.space](http://www.pro3d.space)

# Getting started with from source

* clone
* run `build.cmd`
* `dotnet run PRo3D.Viewer`

# Packages

package | description
:-- | --- |
`pro3d.base` | serialization, cootrafo, c++ interop |
`pro3d.core` | Surfaces, Navigation, Annotations, Grouping, Scene Management, Bookmarks, Viewconfig |
`pro3d.viewer` | View Management / App State, GUI, Docking |

# How to contribute?

* what contributions are wanted?
  * Documentation
  * Feedback and Bug Reports
  * Improvement of existing code
  * Adding new features
* Opening Issues
* Opening Pull Requests

:question: write separate contribution doc

# Embedding in the Aardvark Platform

package | description | repo
:-- | --- | --- |
`pro3d.base` | serialization, cootrafo, c++ interop |
`pro3d.core` | Surfaces, Navigation, Annotations, Grouping, Scene Management, Bookmarks, Viewconfig |
`pro3d.viewer` | View Management / App State, GUI, Docking |

# Code of Conduct

We employ the Contributor Covenant Code of Conduct. Read more [here](./CODE_OF_CONDUCT.md)


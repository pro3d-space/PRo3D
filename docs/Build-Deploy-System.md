# TL;DR

- change RELEASE_NOTES.md
- in a commmand line use: ./build.{cmd|sh} GitHubRelease 

# Release notes

tags and release notes taken from RELEASE_NOTES.md

# Deployment

- build Publish runs dotnet publish and prepares additional info (e.g. updates third party licences)
- Instrument/CooTransformation: lives in separate folder src/InstrumentPlatforms, its build (build CompileInstruments) compiles this one, afterwards, "AddNativeResources" injects the native libraries into the managed dll and "CopyJRWrapper" copies this one to lib/JR.Wrappers.dll which is referenced by PRo3D..

# Resources

all resources should be embedded using dotnet embedded resources to allow "single file deployment"

# Releases on github

- set github_token in your env (used by "GitHubRelease" target)
- tag automatically created by github (given RELEASE_NOTES.MD)
- what happens if tag exists, what happens if release does not exist but the tag - this should be found somewhere here: https://docs.github.com/en/rest/reference/repos#releases

# Title bar

- title bar version is fixed up by "publish" target using string replace

# Known problems

sometimes publish fails with ```Could not open file for writing (binary mode): F:\pro3d\openPro3d\src\PRo3D.Core\obj\Any CPU\Release\netcoreapp3.1\win10-x64\PRo3D.Core.dll``` on my machine. either dotnet problem or with my (often rather full) disk?

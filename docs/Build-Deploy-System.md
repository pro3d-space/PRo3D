# Summary

Releases can be created manually or using github ci actions. Automatic releases should be used for real releases, since it covers builds for all supported platforms (currently win and mac x64).

## Automatic releases

Automatic deployment and release creation is handled via a [github action](https://github.com/pro3d-space/PRo3D/blob/master/.github/workflows/deploy.yml). To trigger the build, perform those steps:
- adapt PRODUCT_RELEASE_NOTES.md
- adapt the version in ./aardium/package.json to reflect the pro3d version. please note that only real version numbers are allowed here (e.g. 4.1.0). This is the version which appears on mac osx in the `About this app` window. Unfortunately this step is still manual...
- commit and push

this will trigger github CI actions
![image](https://user-images.githubusercontent.com/513281/187177791-6657bfc9-c058-4815-85be-9963939fa8a3.png)

and will eventually create a draft release on the github release page:
![image](https://user-images.githubusercontent.com/513281/187177885-d72e1a2a-3175-4d0d-b1df-a7ad9bdbd6bd.png)

Test the builds, rename the draft and use publish to finalize the release.

## Manual release creation

- prepare the github_token env variable https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token
- change PRODUCT_RELEASE_NOTES.md, commit, push
- in a commmand line use: ./build.{cmd|sh} GitHubRelease 

# Release notes

tags and release notes taken from RELEASE_NOTES.md

# Deployment (generall approach)

- `build Publish` runs dotnet publish and prepares additional info (e.g. updates third party licences)
- Instrument/CooTransformation: lives in separate folder src/InstrumentPlatforms, its build (build CompileInstruments) compiles this one, afterwards, "AddNativeResources" injects the native libraries into the managed dll and "CopyJRWrapper" copies this one to lib/JR.Wrappers.dll which is referenced by PRo3D.

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

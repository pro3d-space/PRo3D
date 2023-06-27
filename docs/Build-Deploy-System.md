# Electron based deployment

Earlier pro3d used aardium, a electron package to host the content of pro3d in a self-contained browser. 
In order to simplify the deployment process and align all platforms (e.g. mac requires signing) we switched to a completely electron based deployment in 4.9.3 and up.

# Automatic Releases

## TL;DR

Deployments are triggered by github actions. Currently this is configured in such a way that each commit which touches one of those [files](https://github.com/pro3d-space/PRo3D/blob/bee4f8716e9fcfd94b78112f2d2777867b7685c3/.github/workflows/deploy.yml#L4) triggers a release.

## Details

The `new` build system uses the Build.fsproj and Build.fs/Helpers.fs files for running builds (as opposed to fake runner and build.fsx earlier).

Thus we have those components:
 - Build.fs run by ./build.sh and build.cmd
 - the target "CopyToElectron" patches the version string and copies over the build result into the aardium/bin folders
 - the target "PublishToElectron" performs the build and runs yarn dist in the aardium folder. The rest of deployment/signing/notarization/upload is taken care of by ./aardium/package.json.

.github/workflows/deploy.yml shows the deploy script and is run automatically when pushed into the `autorelease` branch.

## How is pro3d embedded in the electron build?

We simply deploy a pretty empty electron build and start pro3d in server mode (no window) as a process in ./aardium/main.js.
Also we create a seconary which which we pipe in stdout/stdderr using a websocket connection.
Rest is pretty much standard in main.js.
For development use `./build.sh CopyToElectron`, switch into the aardium directory and use for example `yarn install; yarn run start` for testing the application locally.

## Release notes

tags and release notes taken from PRODCT_RELEASE_NOTES.md

## Resources

all resources should be embedded using dotnet embedded resources to allow "single file deployment"

## Title bar

- title bar version is fixed up by "publish" target using string replace

## Known problems

sometimes publish fails with ```Could not open file for writing (binary mode): F:\pro3d\openPro3d\src\PRo3D.Core\obj\Any CPU\Release\netcoreapp3.1\win10-x64\PRo3D.Core.dll``` on my machine. either dotnet problem or with my (often rather full) disk?

# 'Old' manual deploy

- prepare the github_token env variable https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token
- change RELEASE_NOTES.md, commit, push
- in a commmand line use: ./build.{cmd|sh} GitHubRelease 

### Releases on github

- set github_token in your env (used by "GitHubRelease" target)
- tag automatically created by github (given RELEASE_NOTES.MD)
- what happens if tag exists, what happens if release does not exist but the tag - this should be found somewhere here: https://docs.github.com/en/rest/reference/repos#releases

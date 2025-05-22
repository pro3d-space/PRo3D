# Electron based deployment

Our current deployment mechanism is driven by `electron-builder` [see here](https://github.com/pro3d-space/PRo3D/blob/99900d5aa88242e2d340d1c4636994f09e406c79/aardium/package.json#L21).

In order to deploy all supported architectures using github actions is the preferred solution to build and distribute builds.

# 1 -- Releases for the public

## TL;DR

Deployments are triggered by github actions. 
There are two release types:
 - test releases for internal testing, modify [this](https://github.com/pro3d-space/PRo3D/blob/develop/TEST_RELEASE_NOTES.md) file and let the CI build a zip which appears on the github release page as a draft
 - public releases, modify [this](https://github.com/pro3d-space/PRo3D/blob/develop/PRODUCT_RELEASE_NOTES.md) and change the version number [here](https://github.com/pro3d-space/PRo3D/blob/0fc290263430b5c2ff172c18286885a8bf0b73a0/aardium/package.json#L4) and let the CI build a multiplatform build with installer, the result will appear at as a draft in the github release page

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

## Version numbers

when creating releases from within branches suffix the version number with the name of the branch to make the version unique.

## Resources

all resources should be embedded using dotnet embedded resources to allow "single file deployment"

## Title bar

- title bar version is fixed up by "publish" target using string replace

# Manual build and upload to github release

- prepare the github_token env variable https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token, 
- change RELEASE_NOTES.md, commit, push
- in a commmand line use: ```SET GH_TOKEN=... && ./build.{cmd|sh} PublishToElectron```

# 2 -- Internal test releases

## Manual release as a zip file

`build publish` or
```SET GH_TOKEN=... && ./build.{cmd|sh} GitHubRelease```
to just create the release in the bin/publish folder.

## CI

Our electron-based workflow (above) uses click-once installers. 'Old-school' zip-releases still useful for team-internal tests and diagnostics. For this reason in early 2024 we re-introduced zip-deployments and made them CI ready via a [github workflow](https://github.com/pro3d-space/PRo3D/blob/00ace24f078b54582c9553ee39ed8d60b1c7be29/.github/workflows/testrelease.yml#L28)

The `--test` flag uses `TEST_RELEASE_NOTES.md` instead of `PRODUCT_RELEASE_NOTES.md` to quickly create test releases without interrupting the official PRODUCT_RELEASE_NOTES track. plase use a `--testing` suffix for test versions.

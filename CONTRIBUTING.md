### General

The process for contributing to PRo3D is optimized for easy peer reviews by the community:
 * Create an issue for the feature/bug
 * Discuss the feature with the community. High-frequency discussion should happen in our discord channel
 * Implement the feature in a feature branch `features/[issue#]_thename` or `bugs/[issue#]_bugname`
 * Create a PullRequest (PR), ask for contributors to review the PR and merges the PR when done
 * For creating a new release (in develop), change the PRODUCT_RELEASE_NOTES.md / package.json accordingly. For details please look at https://github.com/pro3d-space/PRo3D/blob/main/docs/Build-Deploy-System.md 
 * The CI will trigger a build and create a tag accordingly
 * Please put into the release - the version, humand readable description of the new features/fixes and references to issues etc. (in github choose the release, press  edit button, apply the changes and use "publish release")

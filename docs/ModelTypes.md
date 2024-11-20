# PRo3D's approach to adaptify

TL;DR: when chaning model types (marked as `[<ModelType>]`) manually run adaptify on the project you are working on, add them to the `fsproj` file and add them to source control.
This can be done by, e.g. running `dotnet adaptify --lenses --local --force ./src/PRo3D.Viewer/PRo3D.Viewer.fsproj`.
The local flag tells `adaptify` to really generate the .g file besides the model files.´

There is a script for generating all model types which can be found [here](../adapt.cmd).

## Why we use local adaptify for PRo3D

Since PRo3D codebase is so large, running adaptify as MSBuild task implicitly is just too expensive for interactive editing etc. Thus we decided to move on to `adaptify`'s local mode. This means, we always generate up-to-date model files and add them to the repository.
The has some advantages:
 - fast code completion
 - WYSIWYG development experience
 - files need to be parsed and typechecked only once during build which leads to ~20% faster compile times
 - better integraiton with development tools such as vscode or rider


It has some disadvantages:
 - large change-sets since .g files are tracked
 - extra effort when chaning model types
 - entry barrier



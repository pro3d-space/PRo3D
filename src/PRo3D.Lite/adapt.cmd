@Echo off
Pushd "%~dp0"
dotnet adaptify --lenses --local --verbose --force ./PRo3D.Lite.fsproj
Popd
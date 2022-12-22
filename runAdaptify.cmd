REM dotnet tool update --local adaptify --version 1.2.0-prerelease2
dotnet paket restore
dotnet adaptify --local --lenses --force --addToProject src\**\*.fsproj !src\PRo3D.CorrelationPanels\PRo3D.CorrelationPanels.fsproj
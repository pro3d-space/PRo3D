dotnet paket restore
dotnet adaptify --local --lenses --force --addToProject src\**\*.fsproj !src\PRo3D.CorrelationPanels\PRo3D.CorrelationPanels.fsproj
REM dotnet adaptify --local --force --addToProject src/**/*.fsproj
REM msbuild -t:restore src\PRo3D.sln
REM dotnet adaptify --local --force --addToProject src\PRo3D.Base\PRo3D.Base.fsproj
REM dotnet build src\PRo3D.Base\PRo3D.Base.fsproj
REM dotnet adaptify --local --force --addToProject src\PRo3D.Core\PRo3D.Core.fsproj
REM dotnet build src\PRo3D.Base\PRo3D.Core.fsproj
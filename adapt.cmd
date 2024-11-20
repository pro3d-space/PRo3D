dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.Base/PRo3D.Base.fsproj
REM dotnet build ./src/PRo3D.Base/PRo3D.Base.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.Core/PRo3D.Core.fsproj
REM dotnet build ./src/PRo3D.Core/PRo3D.Core.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.SimulatedViews/PRo3D.SimulatedViews.fsproj
REM dotnet build ./src/PRo3D.SimulatedViews/PRo3D.SimulatedViews.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.Minerva/PRo3D.Minerva.fsproj
REM dotnet build ./src/PRo3D.Minerva/PRo3D.Minerva.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.2D3DLinking/PRo3D.Linking.fsproj
REM dotnet build ./src/PRo3D.2D3DLinking/PRo3D.Linking.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.Lite/PRo3D.Lite.fsproj
REM dotnet build ./src/PRo3D.Lite/PRo3D.Lite.fsproj

REM not used currently
REM dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.CorrelationPanels/PRo3D.CorrelationPanels.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.Snapshots/PRo3D.Snapshots.fsproj
REM dotnet build ./src/PRo3D.Snapshots/PRo3D.Snapshots.fsproj

dotnet adaptify --lenses --local --verbose --force ./src/PRo3D.Viewer/PRo3D.Viewer.fsproj

dotnet build ./src/PRo3D.Viewer/PRo3D.Viewer.fsproj
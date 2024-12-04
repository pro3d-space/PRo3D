// this script should easy working with model types. Running it from your IDE generates all model types for the referred project
open System.IO
#load "../../utilities/RunAdaptifyProcess.fsx" 

let projFileName = "PRo3D.Snapshots.fsproj"
let projFilePath = Path.Combine(__SOURCE_DIRECTORY__,projFileName)
RunAdaptifyProcess.run projFilePath
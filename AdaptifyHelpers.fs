namespace Aardvark.Fake

open System
open System.IO

module MsBuild =
    open Microsoft.Build.Construction

    let patchProject (projectFileName : string) = 
        let p = ProjectRootElement.Open(projectFileName)
        for g in p.ItemGroups do
            for i in g.Items do
                if i.ItemType = "Compile" && Path.GetExtension(i.Include) = ".fs" then
                    // beside .fs file a .g.fs file?
                    //let generatedFilePath = Path.Combine(Path.GetDirectoryName())
                    printfn "%A" i.Include
        ()

    let test () =
        patchProject "C:\Users\steinlechner\Desktop\PRo3D-nomsbuild\src\PRo3D.2D3DLinking\PRo3D.Linking.fsproj"
namespace PRo3D.Navigation2

open Aardvark.UI.Primitives
open Aardvark.Base
open PRo3D.Base



[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module NavigationModel =
    let initial = {
        camera = CameraController.initial          
        navigationMode =  NavigationMode.FreeFly        
        exploreCenter = V3d.Zero // make option        
    }

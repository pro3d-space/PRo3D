namespace PRo3D.Navigation2

open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Core
open Aardvark.Base



[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module NavigationModel =
    
    let initial = {
        camera = CameraController.initial          
        navigationMode =  NavigationMode.FreeFly        
        exploreCenter = V3d.Zero // make option
    }

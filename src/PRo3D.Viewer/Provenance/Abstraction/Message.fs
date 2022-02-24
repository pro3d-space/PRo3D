namespace PRo3D.Provenance.Abstraction

open System
open Aardvark.Base

open FSharp.Data.Adaptive

open Adaptify


open PRo3D.Core
open PRo3D.Core.Surface
open PRo3D.Core.Drawing

open PRo3D.Viewer

type OMessage = PRo3D.Viewer.ViewerAction

// Messages that get captured in the provenance graph; some messages take
// a list of GUIDs to distinguish messages based on which objects they affect. This makes it possible
// to coalesce messages only if they affect the same set of objects. E.g. editing the same annotation over
// multiple messages should only generate one node in the graph. In contrast, we want to coalesce messages concerning
// the removal of an annotation. If we had a parameter with the GUID, the delete messages would be considered to be
// unequal and thus generate multiple nodes.
[<ModelType>]
type Message =
    | AddAnnotation
    | EditAnnotation    of Guid list
    | DeleteAnnotation
    | LoadAnnotations
    | GroupAction
    | EditSurface       of Guid list
    | Unknown 

    override x.ToString () =
        match x with
            | AddAnnotation     -> "A"
            | EditAnnotation _  -> "E"
            | DeleteAnnotation  -> "D"
            | LoadAnnotations   -> "L"
            | GroupAction       -> "G"
            | EditSurface _     -> "S"
            | Unknown           -> ""

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Message =
    
    let create (current : State) (next : State) (msg : OMessage) =
        let f1 = current.annotations.flat
        let f2 = next.annotations.flat
        let c1 = HashMap.count f1
        let c2 = HashMap.count f2

        match msg with        
        | DrawingMessage (AddAnnotations _) ->
            LoadAnnotations
        | SurfaceActions (SurfaceAppAction.SurfacePropertiesMessage (SurfaceProperties.SetPriority _)) 
        | SurfaceActions (SurfaceAppAction.SurfacePropertiesMessage (SurfaceProperties.ToggleVisible _)) 
        | SurfaceActions (SurfaceAppAction.GroupsMessage (GroupsAppAction.ToggleGroup _)) 
        | SurfaceActions (SurfaceAppAction.GroupsMessage (GroupsAppAction.ToggleChildVisibility _)) ->
            EditSurface (Surfaces.difference current.surfaces next.surfaces)
        | _ ->
            if c2 > c1 then
                AddAnnotation                   // One or more annotations were added
            else if c2 < c1 then
                DeleteAnnotation                // One or more annotations were deleted
            else
                let diff = Annotations.difference current.annotations next.annotations

                if not <| List.isEmpty diff then
                    EditAnnotation diff              // The map of annotations changed, i.e. at least one annotation was edited
                else
                    if current <> next then
                        GroupAction             // The annotations remained unchanged, so the grouping must have changed
                    else
                        Unknown                 // No relevant changes detected

    // Returns if two messages of the given type can be coalesced; i.e. can
    // an existing node with the same message be modified instead of adding a new node
    // This is checked in addition to the equivalence of messages as described above.
    let coalesce = function
        | AddAnnotation -> false
        | _ -> true

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module OMessage =

    let isRelevant = function
        | DrawingMessage _
        | AnnotationMessage _ 
        | AnnotationGroupsMessageViewer _ 
        | DrawingMessage (AddAnnotations _)
        | SurfaceActions (SurfaceAppAction.SurfacePropertiesMessage (SurfaceProperties.SetPriority _)) 
        | SurfaceActions (SurfaceAppAction.SurfacePropertiesMessage (SurfaceProperties.ToggleVisible _))
        | SurfaceActions (SurfaceAppAction.GroupsMessage (GroupsAppAction.ToggleGroup _)) 
        | SurfaceActions (SurfaceAppAction.GroupsMessage (GroupsAppAction.ToggleChildVisibility _)) ->
            true
        | _ -> false

    let isCamera = function
        | SetCamera _
        | SetCameraAndFrustum _
        | SetCameraAndFrustum2 _
        | NavigationMessage _ -> true
        | _ -> false
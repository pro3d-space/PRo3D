namespace PRo3D.Provenance.Abstraction

open System

open Aardvark.Base

open FSharp.Data.Adaptive

open Adaptify

open PRo3D.Core
open PRo3D.Core.Surface

type OSurface = Surface

[<ModelType>]
type Surface = 
    { 
        visible : bool
        priority : float 
    }

    static member def = { visible = true; priority = Surface.Init.priority.value }

    // Comparing surfaces is a bit tricky because we don't actually keep track of all surfaces
    // If a surface is missing in an abstraction state, it is assumed to have the default values defined above
    // Two states differ only if they explicitly assign different values to the same surface, or if one assigns non-default values
    // while the other state does not contain the surface (i.e. implicitly assigns default values)
    static member compareWithDefault f a b =
        f (a |> Option.defaultValue Surface.def) (b |> Option.defaultValue Surface.def)

    static member equalWithDefault =
        Surface.compareWithDefault (=)

    static member comparePriorityWithDefault =
        Surface.compareWithDefault (fun a b -> a.priority = b.priority)

type OSurfaces = GroupsModel

[<ModelType; CustomEquality; NoComparison>]
type Surfaces = 
    { flat : HashMap<Guid, Surface> }

    member x.difference' f y =
        HashMap.map2 (fun _ a b -> f a b) x.flat y.flat
        |> HashMap.filter (fun _ v -> not v)
        |> HashMap.keys 
        |> HashSet.toList

    member x.difference y =
        x.difference' Surface.equalWithDefault y

    override x.GetHashCode () =
        hash x.flat

    override x.Equals y =
        match y with 
        | :? Surfaces as y -> y |> x.difference |> List.isEmpty
        | _ -> false

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Surface =

    let create (s : OSurface) : Surface = {
        visible  = s.isVisible
        priority = s.priority.value
    }

    let restore (orig : OSurface) (s : Surface) : OSurface = {
        orig with 
            isVisible = s.visible
            priority = { orig.priority with value = s.priority }
    }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Surfaces =

    let create (s : OSurfaces) = {
        flat = s.flat |> HashMap.map (fun _ l -> l |> Leaf.toSurface |> Surface.create)
    }
        
    let restore (current : OSurfaces) (surfaces : Surfaces) : OSurfaces =
        let f = 
            current.flat 
            |> HashMap.map (fun k s ->
                surfaces.flat 
                |> HashMap.tryFind k
                |> Option.defaultValue Surface.def
                |> Surface.restore (Leaf.toSurface s)
                |> Surfaces
            )

        { current with flat = f }

    let difference (a : Surfaces) (b : Surfaces) =
        a.difference b

    let difference' (f : Surface option -> Surface option -> bool) (a : Surfaces) (b : Surfaces) =
        a.difference' f b
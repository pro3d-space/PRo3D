namespace Aardvark.SceneGraph


open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open Adaptify
open FSharp.Data.Adaptive
open PRo3D.Base
open FShade
open Aardvark.SceneGraph
open Aardvark.SceneGraph.Semantics
open FSharp.Data.Traceable
open Aardvark.Base
open Aardvark.Base.Geometry
open FSharp.Data.Adaptive
open Aardvark.Base.Ag
open Aardvark.SceneGraph
    
type LightViewProjApplicator(LightViewProj : aval<Trafo3d>, sg : ISg) =
    inherit Sg.AbstractApplicator(sg)
    member x.LightViewProj = LightViewProj   
  
module ShadowSem =
    [<Rule>]
    type LightViewProjSem() = 
        member x.LightViewProj(r : LightViewProjApplicator, scope : Ag.Scope) =
            r.Child?LightViewProj <- r.LightViewProj

module ShadowSg =
    open Aardvark.UI
    
    /// applies inherited attribute "LightViewProj" to a given scenegraph.
    let applyLightViewProj (vp : aval<Trafo3d>) (sg : ISg) =
        LightViewProjApplicator(vp,sg) :> ISg |> Sg.noEvents
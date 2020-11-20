namespace Aardvark.SceneGraph

open Aardvark.Base
open Adaptify
open Aardvark.SceneGraph.Semantics
open FSharp.Data.Adaptive
open FShade
    
type LightViewProjApplicator(LightViewProj : aval<Trafo3d>, sg : ISg) =
    inherit Sg.AbstractApplicator(sg)
    member x.LightViewProj = LightViewProj
    
  
//module ShadowSem = //TODO rno review
//    [<Semantic>]
//    type LightViewProjSem() = 
//        member x.LightViewProj(r : LightViewProjApplicator) =
//            r.Child?LightViewProj <- r.LightViewProj

module ShadowSg =
    open Aardvark.UI
    
    /// applies inherited attribute "LightViewProj" to a given scenegraph.
    let applyLightViewProj (vp : aval<Trafo3d>) (sg : ISg) =
        LightViewProjApplicator(vp,sg) |> Sg.noEvents
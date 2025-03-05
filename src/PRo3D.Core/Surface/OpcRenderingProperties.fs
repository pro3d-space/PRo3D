namespace PRo3D.Core

open Aardvark.Base

open FSharp.Data.Adaptive
open System.Collections.Generic
open Aardvark.GeoSpatial.Opc.PatchLod
open Aardvark.GeoSpatial.Opc


module OpcRenderingExtensions =
    open Aardvark.Base.Ag
    open Aardvark.SceneGraph.Semantics

    type Ag.Scope with
        member x.FootprintVP : aval<M44d> = x?FootprintVP

    type Context = 
        { 
            footprintVP : aval<M44d> 
            modelTrafo: aval<Trafo3d>
            texturesScope : obj
            agScope : Ag.Scope
        }

    let captureContext (n : PatchNode) (s : Ag.Scope) =
        let footprintVP = s.FootprintVP
        let secondaryTexture = SecondaryTexture.getSecondary n s
        let modelTrafo = s.ModelTrafo
        {   footprintVP = footprintVP; texturesScope = secondaryTexture; 
            modelTrafo = modelTrafo;
            agScope = s }  :> obj
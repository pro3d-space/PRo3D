namespace PRo3D.Core

open System
open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Rendering.Text
open Aardvark.FontProvider
open Aardvark.SceneGraph

open PRo3D.SPICE

module Markers =

    type Font = GoogleFontProvider<"Roboto Mono">
    let font = Font.Font

    let markers (cam : aval<Camera>) (referenceFrame : aval<string>) (observer : aval<string>) (time : aval<DateTime>) =
        let viewProj = cam |> AVal.map Camera.viewProjTrafo
        let aspect = cam |> AVal.map (fun c -> Frustum.aspect c.frustum)
        let aspectScaling = aspect |> AVal.map (fun aspect -> Trafo3d.Scale(V3d(1.0, aspect, 1.0)))
        let inNdcBox =
            let box = Box3d.FromPoints(V3d(-1,-1,-1),V3d(1,1,1))
            fun (p : V3d) -> box.Contains p

        let transformation = 
            Rendering.fullTrafo referenceFrame (AVal.constant "SUN") "MARS" (Some "IAU_MARS") observer time
            |> AVal.map (fun trafo -> 
                match trafo with
                | Some trafo -> trafo, true
                | _ -> 
                    Log.warn "could not get trafo for body %s" "MARS"
                    Trafo3d.Identity, false
            )

        let elems = 
            MarsTaggedLocations.taggedLocations 
            |> List.choose (fun (name, (lat,lon)) -> 
                match CooTransformation.latLon2Xyz "MARS" (lat, lon, 0.0), CooTransformation.latLon2Xyz "MARS" (lat, lon, 500000.0) with
                | Some p0, Some p1 -> 
                    let text =
                        let contents = 
                            Array.ofList [
                                let p = 
                                    AVal.custom (fun t -> 
                                        let referenceFrame = referenceFrame.GetValue(t)
                                        let time = time.GetValue(t)
                                        let marsToGlobal = CooTransformation.getRotationTrafo "IAU_MARS" referenceFrame time |> Option.get
                                        let bodyPos = marsToGlobal.TransformPos(p1) |> Some
                                        let vp = viewProj.GetValue t
                                        let scale = aspectScaling.GetValue t
                                        match bodyPos with
                                        | None -> Trafo3d.Scale(0.0)
                                        | Some p ->
                                            let ndc = vp.Forward.TransformPosProj (p) 
                                            let scale = if inNdcBox ndc then scale else Trafo3d.Scale(0.0)
                                            Trafo3d.Scale(0.03) * scale * Trafo3d.Translation(ndc.XYZ)
                                    )
                                p, AVal.constant name
                            ]
                        Sg.texts font C4b.DarkOrange (ASet.ofArray contents)

                    let global2Local = Trafo3d.Translation(p0)
                    let line = 
                        [|
                            Line3d(p0, p1).Transformed(global2Local.Backward)
                        |]
                    let line = 
                        Sg.lines' C4b.DarkOrange line
                        |> Sg.trafo' global2Local
                        |> Sg.trafo (transformation |> AVal.map fst)
                        //|> Sg.depthTest' DepthTest.None
            
                    let sg = 
                        line
                        |> Sg.shader {
                            do! DefaultSurfaces.stableTrafo
                            do! DefaultSurfaces.thickLine
                        }
                        |> Sg.uniform' "LineWidth" 1.5
                        |> Sg.uniform' "PointSize" 8.0
                    
                    Some (sg, text)

                | _ -> None
            )

        let texts = 
            elems
            |> List.map snd
            |> Sg.ofList
            |> Sg.viewTrafo' Trafo3d.Identity 
            |> Sg.projTrafo' Trafo3d.Identity

        let lines = 
            elems
            |> List.map fst
            |> Sg.ofList

        Sg.ofList [texts; lines]
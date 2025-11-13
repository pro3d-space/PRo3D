namespace PRo3D.SPICE

open System

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.Extensions
open PRo3D.Extensions.FSharp
open PRo3D.Core

module InstrumentImages = 

    open Aardvark.Rendering

    type Extrinsics = 
        | Plain of CameraView

    type Intrinsics = 
        | Plain of Frustum

    type ImageData = 
        | FilePath of string

    type ProjectedImage =
        {
            intrinsics : Intrinsics
            extrinsics : Extrinsics
            image      : Option<ImageData>
        }

    type CameraFocus = 
        | FocusBody of focusedBody : string

    type CameraSource =
        | InBody of body : string

    type Intrinsics with
        member x.ProjTrafo = 
            match x with
            | Intrinsics.Plain frustum -> Frustum.projTrafo frustum

type InstrumentProjection = 
    {
        instrumentReferenceFrame : string
        target : InstrumentImages.CameraFocus
        cameraSource : InstrumentImages.CameraSource
        instrumentName : string
        supportBody : string
        time : DateTime
        boresightAdjustment : Option<Trafo3d>
    }

module InstrumentProjection =

    let getLookAt (viewerBody : string) (observer : string) (referenceFrame : string) (supportBody : string) (time : DateTime) =
        let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
        match afc1Pos with    
        | Some targetState -> 
            let rot = targetState.rot
            let t = Trafo3d.FromBasis(-rot.C1, rot.C0, rot.C2, targetState.pos)
            CameraView.ofTrafo t.Inverse |> Some 
            CameraView.lookAt targetState.pos V3d.OOO V3d.OOI |> Some
        | _ -> 
            None

    let projectOnto (referenceFrame : string) (observer : string) (instruments : Map<string, Frustum>) (p : InstrumentProjection) = 
        let bodyToWorld = CooTransformation.getRotationTrafo referenceFrame p.instrumentReferenceFrame p.time
        match bodyToWorld, p.target, p.cameraSource, Map.tryFind p.instrumentName instruments with
        | Some bodyToWorld, InstrumentImages.FocusBody target, InstrumentImages.InBody source, Some frustum -> 
            match getLookAt source observer p.instrumentReferenceFrame p.supportBody p.time with
            | Some view ->
                let boresightAdjustedView = p.boresightAdjustment |> Option.defaultValue Trafo3d.Identity
                bodyToWorld * boresightAdjustedView * CameraView.viewTrafo view * (Frustum.projTrafo frustum) |> Some
            | None -> None
        | _ -> None

    let getLookAtQuat (viewerBody : string) (observer : string) (referenceFrame : string) 
                      (supportBody : string) (time : DateTime) (position : V3d) (sc_quat : QuaternionD) =
        let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
        match afc1Pos with    
        | Some targetState ->
            let pos = targetState.pos
            let rot = Rot3d(sc_quat.Conjugated)
            let frame = M33d.Rotation rot
            let t = Trafo3d.FromBasis(-frame.C0, -frame.C1, -frame.C2, V3d.Zero)
            let u = CameraView.ofTrafo t
            let z = CameraView.withLocation position u
            Some z
        | _ -> 
            None

    let specialTrafos = 
        Map.ofList [
            "HERA_AFC-2", Trafo3d.FromOrthoNormalBasis(V3d.OIO, V3d.IOO, V3d.OOI)
            "HERA_AFC-1", Trafo3d.FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)
            "HERA_HSH", Trafo3d.FromOrthoNormalBasis(-V3d.OIO, -V3d.IOO, V3d.OOI)
        ] 

    let projectOntoQuat (referenceFrame : string) (observer : string) (instruments : Map<string, Frustum>) 
                        (p : InstrumentProjection) (position : V3d) (sc_quat : QuaternionD) = 
        let toSpaceCraft = CooTransformation.getRotationTrafo referenceFrame "J2000" p.time
        match p.target, p.cameraSource, Map.tryFind p.instrumentName instruments, toSpaceCraft with
        | InstrumentImages.FocusBody target, InstrumentImages.InBody source, Some frustum, Some toSpaceCraft -> 
            match getLookAtQuat source observer referenceFrame p.supportBody p.time position sc_quat, getLookAt source observer referenceFrame p.supportBody p.time with
            | Some view, Some refold ->
                toSpaceCraft * CameraView.viewTrafo view * specialTrafos[p.instrumentName] * (Frustum.projTrafo frustum) |> Some
            | _ -> None
        | _ -> None

    // maps fits names to spice names
    let instrumentNames = 
        Map.ofList [
            "AFC1", "HERA_AFC-1"
            "HSH", "HERA_HSH"
            "AFC2", "HERA_AFC-2"
        ]

    let instrument2SpiceName (fitsName : string) = 
        Map.tryFind fitsName instrumentNames
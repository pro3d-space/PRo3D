namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core.Gis


module ProjectedImages =


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

    type Intrinsics with
        member x.ProjTrafo = 
            match x with
            | Intrinsics.Plain frustum -> Frustum.projTrafo frustum

    //type ProjectionInfo = 
    //    {
    //        worldReferenceSystem : string
    //        observer : string
    //        sourceReferenceFrame : string
    //        target : CameraFocus
    //        cameraSource : CameraSource
    //        instrument : Intrinsics
    //        supportBody : string
    //    }

    let getLookAt (viewerBody : string) (observer : string) (referenceFrame : string) (supportBody : string) (time : DateTime) =
        let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
        match afc1Pos with    
        | Some targetState -> 
            let rot = targetState.rot
            let t = Trafo3d.FromBasis(-rot.C1, rot.C0, rot.C2, targetState.pos)
            CameraView.ofTrafo t.Inverse |> Some 
        | _ -> 
            None

    let projectOnto (referenceFrame : string) (observer : string) (instruments : Map<string, Frustum>) (p : InstrumentProjection) = 
        let bodyToWorld = Trafo3d.Identity |> Some // CooTransformation.getRotationTrafo p.instrumentReferenceFrame referenceFrame p.time
        match bodyToWorld, p.target, p.cameraSource, Map.tryFind p.instrumentName instruments with
        | Some bodyToWorld, InstrumentImages.FocusBody target, InstrumentImages.InBody source, Some frustum -> 
            match getLookAt source observer referenceFrame p.supportBody p.time with
            | Some view ->
                //  r.Value * 
                CameraView.viewTrafo view * (Frustum.projTrafo frustum) |> Some
            | None -> None
        | _ -> None


    let splitTimes (startTime : DateTime) (endTime : DateTime) (nrOfShots : int) =
        let shots = (endTime - startTime) / TimeSpan.FromMinutes(2) |> ceil |> int
        let interval = (endTime - startTime) / float shots
        [| 0 .. shots |] |> Array.map (fun i -> startTime + interval * float i) 

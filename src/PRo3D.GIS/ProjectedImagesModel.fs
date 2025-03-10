namespace PRo3D.Core

open System
open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE


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

    type CameraFocus = 
        | FocusBody of focusedBody : string

    type CameraSource =
        | InBody of body : string

    type Intrinsics with
        member x.ProjTrafo = 
            match x with
            | Intrinsics.Plain frustum -> Frustum.projTrafo frustum

    let getLookAt (viewerBody : string) (observer : string) (referenceFrame : string) (supportBody : string) (time : DateTime) =
        let afc1Pos = CooTransformation.getRelState viewerBody supportBody observer time referenceFrame
        match afc1Pos with    
        | Some targetState -> 
            let rot = targetState.rot
            let t = Trafo3d.FromBasis(-rot.C1, rot.C0, rot.C2, targetState.pos)
            CameraView.ofTrafo t.Inverse |> Some 
        | _ -> 
            None

    type ProjectionInfo = 
        {
            worldReferenceSystem : string
            observer : string
            sourceReferenceFrame : string
            target : CameraFocus
            cameraSource : CameraSource
            instrument : Intrinsics
            supportBody : string
        }

    let projectOnto (p : ProjectionInfo) (time : DateTime) = 
        let bodyToWorld = CooTransformation.getRotationTrafo p.sourceReferenceFrame p.worldReferenceSystem time
        match bodyToWorld, p.target, p.cameraSource  with
        | Some bodyToWorld, FocusBody target, InBody source-> 
            match getLookAt source p.observer p.worldReferenceSystem p.supportBody time with
            | Some view ->
                bodyToWorld * CameraView.viewTrafo view * p.instrument.ProjTrafo |> Some
            | None -> None
        | None, _, _ -> None


    let splitTimes (startTime : DateTime) (endTime : DateTime) (nrOfShots : int) =
        let shots = (endTime - startTime) / TimeSpan.FromMinutes(2) |> ceil |> int
        let interval = (endTime - startTime) / float shots
        [| 0 .. shots |] |> Array.map (fun i -> startTime + interval * float i) 

    let computeProjections (projectionInfo : ProjectionInfo) (times : array<DateTime>) = 
        times |> Array.choose (projectOnto projectionInfo)
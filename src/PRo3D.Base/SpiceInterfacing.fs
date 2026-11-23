#nowarn "9"
namespace PRo3D.SPICE

open System
open FSharp.NativeInterop
open Aardvark.Base

open PRo3D.Extensions
open PRo3D.Extensions.FSharp.CooTransformation

module CooTransformation =

    /// THE lock for every call into the native SPICE library (CSPICE is not
    /// thread-safe). It is the PRo3D.SPICE package's own lock, not one of ours: the
    /// package's F# helpers (PRo3D.Extensions.FSharp.CooTransformation.getRelState /
    /// getRotationTrafo) lock that object, and any file that opens
    /// PRo3D.Extensions.FSharp binds to them (InstrumentProjection did: the projector,
    /// imageAxes). With a lock of our own, those and this module's calls ran in
    /// parallel -- a STAC search on its worker against rendering -- and CSPICE died
    /// with SPICE(BADSUBSCRIPT) in trcpkg. One object serializes both.
    let l : obj = PRo3D.Extensions.FSharp.CooTransformation.l

    let getRelState (pcTargetBody : string) (pcSupportBody : string) (pcObserverBody : string) 
                    (pcObserverDateTime : DateTime) (pcOutputReferenceFrame : string) =

        lock l (fun _ -> 
            let p : double[] = Array.zeroCreate 3
            let m : double[] = Array.zeroCreate 9
            let pdPosVec = fixed &p[0]
            let pdRotMat = fixed &m[0]
            let r = 
                CooTransformation.GetRelState(pcTargetBody, pcSupportBody, pcObserverBody, Time.toUtcFormat pcObserverDateTime, 
                                              pcOutputReferenceFrame, NativePtr.toNativeInt pdPosVec, NativePtr.toNativeInt pdRotMat)
            if r <> 0 then 
                None
            else 
                // CooTransformation stores row 
                Some { pos = V3d(p[0],p[1],p[2]); vel = V3d.Zero; rot = M33d(m).Transposed }
        )


    let getRotationTrafo (fromFrame : string) (toFrame : string) (time : DateTime) =
        lock l (fun _ ->
            let m : double[] = Array.zeroCreate 9
            let pdMat = fixed &m[0]
            let r = CooTransformation.GetPositionTransformationMatrix(fromFrame, toFrame, Time.toUtcFormat time, pdMat)
            let rot = M33d(m)
            if r = 0 && rot.Determinant > 0.95 then
                let forward = M44d(rot)
                Trafo3d(forward, forward.Inverse) |> Some
            else
                None
        )

    let latLon2Xyz (planet : string) (lat : float, lon : float, alt : float) =
        lock l (fun _ -> 
            let mutable x, y, z = 0.0, 0.0, 0.0
            if CooTransformation.LatLonAlt2Xyz(planet, lat, -lon, alt, &x, &y, &z) = 0 then
                V3d(x, y, z) |> Some
            else
                None
        )

    let xyzToLatLon (planet : string) (p : V3d) =
        lock l (fun _ -> 
            let mutable lat, lon, alt = 0.0, 0.0, 0.0
            if CooTransformation.Xyz2LatLonAlt(planet, p.X, p.Y, p.Z, &lat, &lon, &alt) = 0 then
                (lat, lon, alt) |> Some
            else
                None
        )
namespace PRo3D.Core

open System
open System.IO
open System.Globalization

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D.Base.Annotation

open FSharp.Data.Adaptive

// SBMT structure files are tab-separated text catalogs of geological
// annotations exported from the Small Body Mapping Tool. Each file holds
// one structure type, declared by a `# type,<kind>` header. See
// plans/sbmtImport.md for format details and frame discussion.
//
// v1: points only. Other kinds dispatch to Unsupported.
module SbmtImporter =

    type StructureType =
        | Point
        | Ellipse
        | Circle
        | Line
        | Polyline
        | Polygon
        | Unsupported of raw : string

    let private parseKindToken (token : string) =
        match token.Trim().ToLowerInvariant() with
        | "point"    -> Point
        | "ellipse"  -> Ellipse
        | "circle"   -> Circle
        | "line"     -> Line
        | "polyline" -> Polyline
        | "polygon"  -> Polygon
        | other      -> Unsupported other

    // The header looks like `# type,point`. Comma after `type` is the
    // delimiter; surrounding whitespace varies.
    let detectStructureType (path : string) : StructureType option =
        File.ReadLines(path)
        |> Seq.tryPick (fun raw ->
            let line = raw.TrimStart()
            if line.StartsWith("#") then
                let body = line.TrimStart('#').Trim()
                if body.StartsWith("type", StringComparison.OrdinalIgnoreCase) then
                    let idx = body.IndexOf(',')
                    if idx > 0 then
                        Some (parseKindToken (body.Substring(idx + 1)))
                    else None
                else None
            else None)

    let private isDataLine (raw : string) =
        let l = raw.Trim()
        l.Length > 0 && not (l.StartsWith("#"))

    let private parseColor (token : string) =
        let parts = token.Split(',')
        if parts.Length >= 3 then
            let f i = Byte.Parse(parts.[i].Trim(), CultureInfo.InvariantCulture)
            C4b(f 0, f 1, f 2, 255uy)
        else
            C4b.Magenta

    let private stripQuotes (token : string) =
        let t = token.Trim()
        if t.Length >= 2 && t.StartsWith("\"") && t.EndsWith("\"") then
            t.Substring(1, t.Length - 2)
        else
            t

    let private parseFloat (token : string) =
        Double.Parse(token.Trim(), CultureInfo.InvariantCulture)

    // Build an Annotation record for a single SBMT point row.
    //
    // trafo  : applied to the cartesian position AFTER km->m conversion.
    //          v1 callers pass Trafo3d.Identity to keep points in the
    //          source frame (DARTSOC / DIMORPHOS_SHM for DART data).
    // _frame : reference-frame label entered by the user (plumbed but not
    //          yet stored on the annotation — see Open TODO in
    //          plans/sbmtImport.md, "Reference-system field storage").
    let parsePointLine
        (trafo : Trafo3d)
        (_frame : string)
        (raw : string)
        : Annotation option =

        if not (isDataLine raw) then None
        else

        let parts = raw.Split('\t')
        if parts.Length < 17 then None
        else

        let xKm = parseFloat parts.[2]
        let yKm = parseFloat parts.[3]
        let zKm = parseFloat parts.[4]
        let posMeters = V3d(xKm, yKm, zKm) * 1000.0
        let pos = trafo.Forward.TransformPos posMeters

        let color = parseColor parts.[15]
        let label = stripQuotes parts.[16]

        // Mirrors MeasurementsImporter.getAnnotation initialiser. referenceSystem
        // is None until the storage decision is made (see plan TODO).
        Some {
            version          = Annotation.current
            key              = Guid.NewGuid()
            geometry         = Geometry.Point
            projection       = Projection.Linear
            semantic         = Semantic.Horizon0
            points           = IndexList.single pos
            segments         = IndexList.empty
            color            = { c = color }
            thickness        = Annotation.Initial.thickness
            results          = None
            dnsResults       = None
            ellipticResults  = None
            modelTrafo       = Trafo3d.Identity
            visible          = true
            showDns          = false
            text             = label
            textsize         = Annotation.Initial.textSize
            showText         = true
            surfaceName      = ""
            view             = FreeFlyController.initial.view
            semanticId       = SemanticId ""
            semanticType     = SemanticType.Undefined
            crossSectionClipping = false
            crossSectionRefPoint = None
            manualDipAngle   = Annotation.initialManualDipAngle
            manualDipAzimuth = Annotation.initialmanualDipAzimuth
            bookmarkId       = None
            referenceSystem  = None
        }

    // Sample count for ellipse boundary. PRo3D's interactive construction
    // (EllipseAnnotation.constructAndSampleFromPlane) uses 200. 60 is a
    // reasonable middle ground: the silhouette is smooth at typical zoom,
    // and at this density 4,800 ellipses still parse + merge in ~1s.
    let private ellipseSamples = 60

    // Constructs the local east direction in the tangent plane at C.
    // Uses Z as the polar reference. Degenerates if C is parallel to Z;
    // in that case (a near-pole ellipse) falls back to the world X axis.
    let private localEast (radialUp : V3d) =
        let raw = Vec.cross V3d.ZAxis radialUp
        let len = raw.Length
        if len > 1e-9 then raw / len
        else
            // Pole singularity: pick any direction perpendicular to radialUp.
            let alt = Vec.cross V3d.XAxis radialUp
            alt.Normalized

    // Build an Annotation record for a single SBMT ellipse row.
    //
    // SBMT specifies the ellipse via center + diameter (= 2 * semi-major) +
    // flattening (= b/a, so semi-minor = semi-major * flattening) + regularAngle
    // (= angle from local east to major axis, in the tangent plane).
    //
    // v2 approximation: the tangent plane is taken perpendicular to the radial
    // direction C/|C|, i.e. the body's circumscribing-sphere normal. For
    // boulder catalogs (ellipse size << body curvature scale) this is visually
    // adequate. A future iteration can ray-cast the loaded OBJ at C to get the
    // real surface normal -- see plans/sbmtImport.md "Precise ellipse 'up'
    // via surface intersection".
    let parseEllipseLine
        (trafo : Trafo3d)
        (_frame : string)
        (raw : string)
        : Annotation option =

        if not (isDataLine raw) then None
        else

        let parts = raw.Split('\t')
        if parts.Length < 18 then None
        else

        let xKm = parseFloat parts.[2]
        let yKm = parseFloat parts.[3]
        let zKm = parseFloat parts.[4]
        let diameterKm = parseFloat parts.[12]
        let flattening = parseFloat parts.[13]
        let regularAngleDeg = parseFloat parts.[14]
        let color = parseColor parts.[15]
        let label = stripQuotes parts.[17]

        let centerMeters = V3d(xKm, yKm, zKm) * 1000.0
        let center = trafo.Forward.TransformPos centerMeters

        let radialUp =
            let len = center.Length
            if len < 1e-9 then V3d.ZAxis  // pathological: center at origin
            else center / len

        let east = localEast radialUp
        let north = Vec.cross radialUp east |> Vec.normalize

        // Rotate east by regularAngle inside the tangent plane to obtain the
        // major-axis direction. SBMT convention: angle measured from the
        // longitude tangent (east here) towards the major axis.
        let rot = regularAngleDeg * Constant.RadiansPerDegree
        let cosA = cos rot
        let sinA = sin rot
        let majorDir = east * cosA + north * sinA
        let minorDir = east * (-sinA) + north * cosA

        // diameter is a major-axis diameter; semi-major = half that.
        let a = diameterKm * 1000.0 * 0.5
        let b = a * flattening

        let semiMajor = majorDir * a
        let semiMinor = minorDir * b

        // Sample the closed boundary. We emit ellipseSamples points (no
        // duplicate closing point); PRo3D renders these as a closed curve.
        let samples =
            Array.init ellipseSamples (fun i ->
                let t = Constant.PiTimesTwo * float i / float ellipseSamples
                center + semiMajor * cos t + semiMinor * sin t)

        Some {
            version          = Annotation.current
            key              = Guid.NewGuid()
            geometry         = Geometry.AxisEllipse
            projection       = Projection.Linear
            semantic         = Semantic.Horizon0
            points           = IndexList.ofArray samples
            segments         = IndexList.empty
            color            = { c = color }
            thickness        = Annotation.Initial.thickness
            results          = None
            dnsResults       = None
            ellipticResults  = None
            modelTrafo       = Trafo3d.Identity
            visible          = true
            showDns          = false
            text             = label
            textsize         = Annotation.Initial.textSize
            showText         = true
            surfaceName      = ""
            view             = FreeFlyController.initial.view
            semanticId       = SemanticId ""
            semanticType     = SemanticType.Undefined
            crossSectionClipping = false
            crossSectionRefPoint = None
            manualDipAngle   = Annotation.initialManualDipAngle
            manualDipAzimuth = Annotation.initialmanualDipAzimuth
            bookmarkId       = None
            referenceSystem  = None
        }

    let startImporter
        (trafo : Trafo3d)
        (referenceFrame : string)
        (path : string)
        : IndexList<Annotation> =

        let kind =
            detectStructureType path
            |> Option.defaultWith (fun () ->
                failwithf "[SbmtImporter] no `# type,<kind>` header in %s" path)

        match kind with
        | Point ->
            File.ReadLines(path)
            |> Seq.choose (parsePointLine trafo referenceFrame)
            |> IndexList.ofSeq
        | Ellipse | Circle ->
            // Circle is an ellipse with flattening = 1; same column layout.
            File.ReadLines(path)
            |> Seq.choose (parseEllipseLine trafo referenceFrame)
            |> IndexList.ofSeq
        | Line | Polyline | Polygon ->
            Log.warn "[SbmtImporter] line/polyline/polygon import is planned (v3) but not yet implemented: %s" path
            IndexList.empty
        | Unsupported raw ->
            Log.warn "[SbmtImporter] unsupported structure type '%s' in %s" raw path
            IndexList.empty

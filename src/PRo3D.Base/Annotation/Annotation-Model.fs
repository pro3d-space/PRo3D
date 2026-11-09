namespace PRo3D.Base.Annotation

open System
open MBrace.FsPickler
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Rendering
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open PRo3D.Base.Gis
open Chiron

open Adaptify
open Aardvark.Geometry

#nowarn "0686"

type Projection =
| Linear = 0
| Viewpoint = 1
| Sky = 2

module Projection =
    /// Reads a persisted projection value. Legacy scenes may carry `3`, the removed
    /// "Bookmark" projection (never implemented past Nov 2021); those load as `Linear`.
    let ofInt (i : int) : Projection =
        (if i = 3 then 0 else i) |> enum<Projection>

type Geometry =
| Point         = 0
| Line          = 1
| Polyline      = 2
| Polygon       = 3
| DnS           = 4
| TT            = 5
| Ellipse       = 6
| AxisEllipse   = 7
| Axis4PEllipse = 8

module Geometry =

    /// Whether an annotation's `points` are still the control points the user clicked, and so can
    /// be edited one vertex at a time.
    ///
    /// The ellipse tools are excluded: once the ellipse is constructed, `getFinishedAnnotation`
    /// replaces their `points` with the sampled outline, so there are no control points left to
    /// move. A three-point ellipse is edited by its axis ends instead, see `EllipseEdit`.
    /// DnS and TT are excluded because their points carry a fitted plane's meaning rather
    /// than a free polyline's.
    let isVertexEditable (geometry : Geometry) =
        match geometry with
        | Geometry.Point | Geometry.Line | Geometry.Polyline | Geometry.Polygon -> true
        | _ -> false

    /// Whether a point can be added to an annotation of this geometry (#639). A line that gets a
    /// third point becomes a polyline - exports, profiles and geological maps treat the two alike.
    let canInsertVertex (geometry : Geometry) =
        match geometry with
        | Geometry.Line | Geometry.Polyline | Geometry.Polygon -> true
        | _ -> false

    /// The projection modes that produce a well-defined annotation for this geometry.
    ///
    /// The ellipse tools are fitted in a plane through the picked points and are only
    /// meaningful under `Sky` (see docs/EllipseAnnotations.md); every other tool supports
    /// all projection modes. `addPoint` in Drawing-App.fs has no branch for the excluded
    /// combinations and would `failwith` on them.
    ///
    /// The head of the list is the default: `SetGeometry` selects it when the current
    /// projection is not in the new geometry's allowed set.
    let allowedProjections (geometry : Geometry) : list<Projection> =
        match geometry with
        | Geometry.Ellipse | Geometry.AxisEllipse | Geometry.Axis4PEllipse -> [ Projection.Sky ]
        | _ -> [ Projection.Linear; Projection.Viewpoint; Projection.Sky ]

    /// Geometries whose results only make sense with a real reference body selected
    /// (`Planet.None` gives an arbitrary world-axis frame: dip/strike/thickness azimuths
    /// are measured from world +X, not true north). The ellipse tools additionally rely on
    /// the Sky surface-drape, which has no valid scale without a body and produces an
    /// empty outline -> a zero-point annotation that crashes result calculation.
    /// The annotation toolbar greys these out while `Planet.None` is the reference system.
    let needsReferenceBody (geometry : Geometry) =
        match geometry with
        | Geometry.DnS | Geometry.TT
        | Geometry.Ellipse | Geometry.AxisEllipse | Geometry.Axis4PEllipse -> true
        | _ -> false

type Semantic = 
| Horizon0 = 0 
| Horizon1 = 1 
| Horizon2 = 2 
| Horizon3 = 3 
| Horizon4 = 4 
| Crossbed = 5 
| GrainSize = 6 
| None = 7

[<ModelType>]
type Segment = {
    startPoint : V3d
    endPoint   : V3d
    
    points : IndexList<V3d> 
}
with
    static member FromJson ( _ : Segment) =
        json {
        
            let! startPoint = Json.read "startPoint"
            let! endPoint = Json.read "endPoint"

            let! points = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"

            return {
                startPoint = startPoint |> V3d.Parse
                endPoint   = endPoint |> V3d.Parse
                points     = points |> IndexList.ofList
            }
        }

    static member ToJson ( x : Segment) =
        json {
            do! Json.write "startPoint" (x.startPoint.ToString())
            do! Json.write "endPoint" (x.endPoint.ToString())
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (x.points |> IndexList.toList)
        }

type Style = {
    color : C4b
    thickness : NumericInput
} with
    static member color_ =
        (fun b -> b.color), (fun c (b : Style) -> { b with color = c })
    static member thickness_ =
        (fun b -> b.thickness), (fun value b -> { b with thickness = value })

[<ModelType>]
type Statistics = {
    version      : int
    average      : float
    min          : float
    max          : float
    stdev        : float
    sumOfSquares : float
}
with 
    //version modules  
    static member current = 0
    static member private readV0 = 
        json {
            let! average      = Json.readFloat "average"
            let! min          = Json.readFloat "min"
            let! max          = Json.readFloat "max"
            let! stdev        = Json.readFloat "stdev"
            let! sumOfSquares = Json.readFloat "sumOfSquares"
            
            return {
                version      = Statistics.current
                average      = average
                min          = min
                max          = max
                stdev        = stdev
                sumOfSquares = sumOfSquares
            }
        }

    static member FromJson(_ : Statistics) = 
        json {
            let! v = Json.read "version"
            match v with            
            | 0 -> return! Statistics.readV0
            | _ -> return! v |> sprintf "don't know version %A  of Statistics" |> Json.error
        }
    static member ToJson (x : Statistics) =
        json {              
            do! Json.write      "version"      Statistics.current
            do! Json.writeFloat "sumOfSquares" x.sumOfSquares
            do! Json.writeFloat "stdev"        x.stdev
            do! Json.writeFloat "max"          x.max
            do! Json.writeFloat "min"          x.min
            do! Json.writeFloat "average"      x.average
        }

module Statistics =
    
    //initial
    let initial = 
        {
            version      = Statistics.current
            average      = Double.NaN
            min          = Double.NaN
            max          = Double.NaN
            stdev        = Double.NaN
            sumOfSquares = Double.NaN
        }
    
[<ModelType>]
type DipAndStrikeResults = {
    version         : int
    plane           : Plane3d
    dipAngle        : float
    dipDirection    : V3d
    strikeDirection : V3d
    dipAzimuth      : float
    strikeAzimuth   : float
    centerOfMass    : V3d
    error           : Statistics
    regressionInfo  : option<RegressionInfo3d>
}
with 
    static member current = 1
    static member private readV0 = 
        json {
            let! plane           = Json.read "plane"
            let! dipAngle        = Json.read "dipAngle"
            let! dipDirection    = Json.read "dipDirection"
            let! strikeDirection = Json.read "strikeDirection"
            let! dipAzimuth      = Json.read "dipAzimuth"
            let! strikeAzimuth   = Json.read "strikeAzimuth"
            let! centerOfMass    = Json.read "centerOfMass"
            let! error           = Json.read "error"
            
            return {
                version         = DipAndStrikeResults.current
                plane           = plane |> Json.parsePlane3d //plane |> Plane3d.Parse
                dipAngle        = dipAngle
                dipDirection    = dipDirection |> V3d.Parse
                strikeDirection = strikeDirection |> V3d.Parse
                dipAzimuth      = dipAzimuth
                strikeAzimuth   = strikeAzimuth
                centerOfMass    = centerOfMass |> V3d.Parse
                error           = error
                regressionInfo  = None
            }
        }

    static member private readV1 = 
        json {
            let! plane           = Json.read "plane"
            let! dipAngle        = Json.read "dipAngle"
            let! dipDirection    = Json.read "dipDirection"
            let! strikeDirection = Json.read "strikeDirection"
            let! dipAzimuth      = Json.read "dipAzimuth"
            let! strikeAzimuth   = Json.read "strikeAzimuth"
            let! centerOfMass    = Json.read "centerOfMass"
            let! error           = Json.read "error"
            let! regressionInfo  = Json.read "regressionInfo"
            
            return {
                version         = DipAndStrikeResults.current
                plane           = plane |> Json.parsePlane3d //plane |> Plane3d.Parse
                dipAngle        = dipAngle
                dipDirection    = dipDirection |> V3d.Parse
                strikeDirection = strikeDirection |> V3d.Parse
                dipAzimuth      = dipAzimuth
                strikeAzimuth   = strikeAzimuth
                centerOfMass    = centerOfMass |> V3d.Parse
                error           = error
                regressionInfo  = regressionInfo
            }
        }
    
    static member FromJson(_ : DipAndStrikeResults) = 
        json {
            let! v = Json.read "version"
            match v with            
              | 0 -> return! DipAndStrikeResults.readV0
              | 1 -> return! DipAndStrikeResults.readV1
              | _ -> return! v |> sprintf "don't know version %A  of DipAndStrikeResults" |> Json.error
        }
    static member ToJson (x : DipAndStrikeResults) =
        json {
            do! Json.write "version"          x.version                    
            do! Json.write "plane"            (x.plane.ToString())
            do! Json.write "dipAngle"         x.dipAngle          
            do! Json.write "dipDirection"     (x.dipDirection.ToString())
            do! Json.write "strikeDirection"  (x.strikeDirection.ToString())        
            do! Json.write "dipAzimuth"       x.dipAzimuth     
            do! Json.write "strikeAzimuth"    x.strikeAzimuth  
            do! Json.write "centerOfMass"     (x.centerOfMass.ToString())
            do! Json.write "error"            x.error
            do! Json.write "regressionInfo"   x.regressionInfo
        }

    static member initial =
        {
            version         = DipAndStrikeResults.current
            plane           = Plane3d.Invalid      
            dipAngle        = Double.NaN  
            dipDirection    = V3d.NaN
            strikeDirection = V3d.NaN
            dipAzimuth      = Double.NaN  
            strikeAzimuth   = Double.NaN  
            centerOfMass    = V3d.NaN  
            error           = Statistics.initial
            regressionInfo  = None
        }  


        
[<ModelType>]
type AnnotationResults = {
    version           : int
    height            : float
    heightDelta       : float
    avgAltitude       : float
    length            : float
    wayLength         : float
    bearing           : float
    slope             : float
    trueThickness     : float
    verticalThickness : float
    area              : float
}
with 
    static member current = 3
    static member private readV0 =
        json {      
            let! height      = Json.readFloat "height"     
            let! heightDelta = Json.readFloat "heightDelta"
            let! avgAltitude = Json.readFloat "avgAltitude"
            let! length      = Json.readFloat "length"     
            let! wayLength   = Json.readFloat "wayLength"  
            let! bearing     = Json.readFloat "bearing"    
            let! slope       = Json.readFloat "slope"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length     
                wayLength         = wayLength  
                bearing           = bearing    
                slope             = slope            
                trueThickness     = Double.NaN
                verticalThickness = Double.NaN
                area              = Double.NaN
            }
        }

    static member private readV1 =
        json {      
            let! height         = Json.readFloat "height"     
            let! heightDelta    = Json.readFloat "heightDelta"
            let! avgAltitude    = Json.readFloat "avgAltitude"
            let! length         = Json.readFloat "length"     
            let! wayLength      = Json.readFloat "wayLength"  
            let! bearing        = Json.readFloat "bearing"    
            let! slope          = Json.readFloat "slope"
            let! trueThickness  = Json.readFloat "trueThickness"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length
                wayLength         = wayLength  
                bearing           = bearing
                slope             = slope
                trueThickness     = trueThickness
                verticalThickness = Double.NaN
                area              = Double.NaN
            }
        }

    static member private readV2 =
        json {      
            let! height             = Json.readFloat "height"     
            let! heightDelta        = Json.readFloat "heightDelta"
            let! avgAltitude        = Json.readFloat "avgAltitude"
            let! length             = Json.readFloat "length"     
            let! wayLength          = Json.readFloat "wayLength"  
            let! bearing            = Json.readFloat "bearing"    
            let! slope              = Json.readFloat "slope"
            let! trueThickness      = Json.readFloat "trueThickness"
            let! verticalThickness  = Json.readFloat "verticalThickness"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length
                wayLength         = wayLength  
                bearing           = bearing
                slope             = slope
                trueThickness     = trueThickness
                verticalThickness = verticalThickness
                area              = Double.NaN
            }
        }

    static member private readV3 = 
        json {      
            let! height             = Json.readFloat "height"     
            let! heightDelta        = Json.readFloat "heightDelta"
            let! avgAltitude        = Json.readFloat "avgAltitude"
            let! length             = Json.readFloat "length"     
            let! wayLength          = Json.readFloat "wayLength"  
            let! bearing            = Json.readFloat "bearing"    
            let! slope              = Json.readFloat "slope"
            let! trueThickness      = Json.readFloat "trueThickness"
            let! verticalThickness  = Json.readFloat "verticalThickness"
            let! area               = Json.readFloat "area"
            
            return {
                version           = AnnotationResults.current    
                height            = height     
                heightDelta       = heightDelta
                avgAltitude       = avgAltitude
                length            = length
                wayLength         = wayLength  
                bearing           = bearing
                slope             = slope
                trueThickness     = trueThickness
                verticalThickness = verticalThickness
                area              = area
            }
        }

    static member FromJson(_: AnnotationResults) =
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! AnnotationResults.readV0
            | 1 -> return! AnnotationResults.readV1
            | 2 -> return! AnnotationResults.readV2
            | 3 -> return! AnnotationResults.readV3
            | _ -> return! v |> sprintf "don't know version %A  of AnnotationResults" |> Json.error
        }
    
    static member ToJson (x : AnnotationResults) =
        json {
            do! Json.write      "version"           x.version
            do! Json.writeFloat "height"            x.height      
            do! Json.writeFloat "heightDelta"       x.heightDelta       
            do! Json.writeFloat "avgAltitude"       x.avgAltitude 
            do! Json.writeFloat "length"            x.length            
            do! Json.writeFloat "wayLength"         x.wayLength       
            do! Json.writeFloat "bearing"           x.bearing     
            do! Json.writeFloat "slope"             x.slope       
            do! Json.writeFloat "trueThickness"     x.trueThickness
            do! Json.writeFloat "verticalThickness" x.verticalThickness
            do! Json.writeFloat "area"              x.area
        }


/// The lon/lat ellipse exactly as every PRo3D release writes and reads it under the
/// annotation's `"ellipseResults"` key. The schema must never change: releases before
/// #644 require `center`, `major` and `minor` and fail to load the whole scene
/// otherwise. Only ever written when there is a lon/lat ellipse to write.
type GeographicalEllipseJson =
    {
        geographicalEllipse      : Ellipse2d
        geographicalEllipseAssym : Option<Ellipse2d>
    }
    with
        static let version = 0

        static member private readV0 =
            json {
                let! center             = Json.read "center"
                let! major              = Json.read "major"
                let! minor              = Json.read "minor"

                let! center2            = Json.tryRead "centerAssim"
                let! major2             = Json.tryRead "majorAssim"
                let! minor2             = Json.tryRead "minorAssim"

                let assymEllipse =
                    match center2, major2, minor2 with
                    | Some c, Some ma, Some mi ->
                        Some(Ellipse2d(V2d.Parse(c), V2d.Parse(ma), V2d.Parse(mi)))
                    | _ ->
                        None

                return {
                    geographicalEllipse      = Ellipse2d(V2d.Parse(center), V2d.Parse(major), V2d.Parse(minor))
                    geographicalEllipseAssym = assymEllipse
                }
            }

        static member FromJson(_: GeographicalEllipseJson) =
            json {
                let! v = Json.read "version"
                match v with
                | 0 -> return! GeographicalEllipseJson.readV0
                | _ -> return! v |> sprintf "don't know version %A  of AnnotationResults" |> Json.error
            }

        static member ToJson (x : GeographicalEllipseJson) =
            json {
                do! Json.write   "version"  version
                do! Json.write   "center"   (string x.geographicalEllipse.Center)
                do! Json.write   "major"    (string x.geographicalEllipse.Axis0)
                do! Json.write   "minor"    (string x.geographicalEllipse.Axis1)

                if x.geographicalEllipseAssym.IsSome then
                    do! Json.write "centerAssim" (string x.geographicalEllipseAssym.Value.Center)
                    do! Json.write "majorAssim"  (string x.geographicalEllipseAssym.Value.Axis0)
                    do! Json.write "minorAssim"  (string x.geographicalEllipseAssym.Value.Axis1)
            }

/// The metric ellipse under the annotation's `"ellipseShape"` key, new with #644. A new
/// key rather than new fields in `"ellipseResults"`, so that older releases, which do
/// not know it, simply skip it and still load the scene.
type EllipseShapeJson =
    {
        center           : V3d
        semiMajorAxis    : V3d
        semiMinorAxis    : V3d
        majorAxisAzimuth : float
    }
    with
        static let version = 0

        static member private readV0 =
            json {
                let! center           = Json.read "center"
                let! semiMajorAxis    = Json.read "semiMajorAxis"
                let! semiMinorAxis    = Json.read "semiMinorAxis"
                let! majorAxisAzimuth = Json.readFloat "majorAxisAzimuth"
                return {
                    center           = V3d.Parse center
                    semiMajorAxis    = V3d.Parse semiMajorAxis
                    semiMinorAxis    = V3d.Parse semiMinorAxis
                    majorAxisAzimuth = majorAxisAzimuth
                }
            }

        static member FromJson(_: EllipseShapeJson) =
            json {
                let! v = Json.read "version"
                match v with
                | 0 -> return! EllipseShapeJson.readV0
                | _ -> return! v |> sprintf "don't know version %A of EllipseShape" |> Json.error
            }

        static member ToJson (x : EllipseShapeJson) =
            json {
                do! Json.write      "version"          version
                do! Json.write      "center"           (string x.center)
                do! Json.write      "semiMajorAxis"    (string x.semiMajorAxis)
                do! Json.write      "semiMinorAxis"    (string x.semiMinorAxis)
                do! Json.writeFloat "majorAxisAzimuth" x.majorAxisAzimuth
            }

/// An ellipse annotation's shape, fixed when it is constructed (drawn or imported).
///
/// The metric part (`center`, `semiMajorAxis`, `semiMinorAxis`) lives in the same
/// body-fixed world space as the annotation's points, in metres. `majorAxisAzimuth` is
/// measured at construction, like dip and strike, so readers need no reference system.
/// A result read from a file written before these fields existed carries NaN in them.
///
/// In memory one record; on disk two keys of the annotation, see `ofJson` / `toJson`.
type EllipticAnnotationResult =
    {
        /// lon/lat (degrees) ellipse of the dormant geographic construction; only the
        /// GeoJSON writer reads it. `None` for ellipses fitted on a plane or imported.
        geographicalEllipse      : Option<Ellipse2d>
        geographicalEllipseAssym : Option<Ellipse2d>
        /// m, body-fixed
        center                   : V3d
        /// m, vector along the long axis; never shorter than `semiMinorAxis`
        semiMajorAxis            : V3d
        /// m, vector along the short axis, perpendicular to `semiMajorAxis`
        semiMinorAxis            : V3d
        /// deg, clockwise from local north at `center`, axial [0, 180). NaN when the long
        /// axis is (near) vertical or the ellipse is a circle.
        majorAxisAzimuth         : float
    }

module EllipticAnnotationResult =

    /// The in-memory result from the annotation's `"ellipseResults"` and `"ellipseShape"`
    /// keys; `None` when neither is there.
    let ofJson (geographical : Option<GeographicalEllipseJson>) (shape : Option<EllipseShapeJson>) =
        match geographical, shape with
        | None, None -> None
        | _ ->
            Some {
                geographicalEllipse      = geographical |> Option.map (fun g -> g.geographicalEllipse)
                geographicalEllipseAssym = geographical |> Option.bind (fun g -> g.geographicalEllipseAssym)
                center                   = shape |> Option.map (fun s -> s.center) |> Option.defaultValue V3d.NaN
                semiMajorAxis            = shape |> Option.map (fun s -> s.semiMajorAxis) |> Option.defaultValue V3d.NaN
                semiMinorAxis            = shape |> Option.map (fun s -> s.semiMinorAxis) |> Option.defaultValue V3d.NaN
                majorAxisAzimuth         = shape |> Option.map (fun s -> s.majorAxisAzimuth) |> Option.defaultValue Double.NaN
            }

    /// What goes under `"ellipseResults"` (the unchanged lon/lat schema, only when there
    /// is one) and under `"ellipseShape"` (only when the metric shape is known).
    let toJson (e : EllipticAnnotationResult) =
        let geographical =
            e.geographicalEllipse |> Option.map (fun g ->
                { geographicalEllipse = g; geographicalEllipseAssym = e.geographicalEllipseAssym })
        let shape =
            if e.center.AnyNaN || e.semiMajorAxis.AnyNaN || e.semiMinorAxis.AnyNaN then None
            else
                Some { center = e.center; semiMajorAxis = e.semiMajorAxis
                       semiMinorAxis = e.semiMinorAxis; majorAxisAzimuth = e.majorAxisAzimuth }
        geographical, shape


/// Editing a finished three-point ellipse by its four axis ends (docs/EllipseAnnotations.md).
///
/// The construction plane is never re-fitted. It is the plane through the stored `center`,
/// spanned by the stored semi-axes - the plane the ellipse was fitted in when it was drawn.
/// A dropped handle is projected orthogonally onto it, and the ellipse is rebuilt there, so
/// the shape only changes by what the user moved: there is no drift from edit to edit.
///
/// Handle order: 0 = center + semiMajorAxis, 1 = center - semiMajorAxis,
/// 2 = center + semiMinorAxis, 3 = center - semiMinorAxis. A rebuild may swap which axis is
/// the long one; handles are re-derived from the stored result on every grab, so that is fine.
module EllipseEdit =

    let private finite (v : V3d) = not v.AnyNaN && not v.AnyInfinity

    /// center, semi-major, semi-minor and the plane normal, when the stored shape can define
    /// a plane (no NaN from an old file, neither axis degenerate)
    let tryFrame (r : EllipticAnnotationResult) : Option<V3d * V3d * V3d * V3d> =
        let c, a, b = r.center, r.semiMajorAxis, r.semiMinorAxis
        if not (finite c && finite a && finite b) then None
        else
            let n = Vec.cross a b
            let scale = a.Length * b.Length
            if scale <= 0.0 || n.Length <= 1e-9 * scale then None
            else Some (c, a, b, n.Normalized)

    /// the construction plane of a stored ellipse
    let tryPlane (r : EllipticAnnotationResult) : Option<Plane3d> =
        tryFrame r |> Option.map (fun (c, _, _, n) -> Plane3d(n, c))

    /// Handle index of the centre, which moves the whole ellipse (0 and 1 are the ends of the
    /// major axis, 2 and 3 of the minor).
    [<Literal>]
    let CentreHandle = 4

    /// The four axis ends and the centre on the construction plane, in handle order.
    let tryPlaneHandles (r : EllipticAnnotationResult) : Option<V3d[]> =
        tryFrame r |> Option.map (fun (c, a, b, _) -> [| c + a; c - a; c + b; c - b; c |])

    /// Whether an annotation can be edited this way: a three-point ellipse drawn with the Sky
    /// projection, whose stored shape defines a plane. Imported ellipses (Linear, undraped) and
    /// ellipses saved before the shape was stored are left alone.
    let isEditable (geometry : Geometry) (projection : Projection) (r : Option<EllipticAnnotationResult>) =
        geometry = Geometry.AxisEllipse
        && projection = Projection.Sky
        && (match r with Some r -> (tryFrame r).IsSome | None -> false)

    /// Where the centre handle is: the middle of the draped outline (the mean of its points, the
    /// closing duplicate left out), so it sits where the ellipse is seen. The plane's centre can
    /// be metres off the terrain the outline is stamped onto - the plane is frozen, and an end
    /// dragged onto higher ground or a move takes the outline away from it - and in an oblique
    /// view a handle there looked far from its ellipse. The plane centre when there is no outline.
    let outlineCentre (r : EllipticAnnotationResult) (outline : V3d[]) : V3d =
        let n =
            if outline.Length > 1 && outline.[0] = outline.[outline.Length - 1] then outline.Length - 1
            else outline.Length
        if n = 0 then r.center
        else
            let mutable sum = V3d.Zero
            for i in 0 .. n - 1 do sum <- sum + outline.[i]
            sum / float n

    /// The handles where they are drawn and grabbed: each axis end replaced by the closest
    /// point of the draped outline, so the handle sits on the terrain the outline sits on,
    /// not on the (possibly buried or floating) plane. Falls back to the plane point when the
    /// outline is empty. 4 x outline length distance tests; the outline has 200 points.
    /// The centre is `outlineCentre`.
    let handlesOnOutline (r : EllipticAnnotationResult) (outline : V3d[]) : V3d[] =
        match tryPlaneHandles r with
        | None -> [||]
        | Some handles ->
            if outline.Length = 0 then handles
            else
                let nearest (h : V3d) =
                    let mutable best = outline.[0]
                    let mutable bestD = (h - best).LengthSquared
                    for i in 1 .. outline.Length - 1 do
                        let d = (h - outline.[i]).LengthSquared
                        if d < bestD then
                            best <- outline.[i]
                            bestD <- d
                    best
                handles |> Array.mapi (fun index h -> if index = CentreHandle then outlineCentre r outline else nearest h)

    /// The three construction points (major end, major end, minor end - the clicks of the
    /// AxisEllipse tool) on the construction plane after handle `index` is dropped at `drop`.
    ///
    /// Dragging a major end keeps the opposite end and the minor length; dragging a minor end
    /// keeps the major axis and sets only the minor length (the drop's distance from the major
    /// axis); dragging the centre moves the whole ellipse within the plane by as much as the
    /// handle was moved, both ends projected onto it: from `outlineCentre` (where the handle is
    /// drawn and grabbed) to the drop. A drop where the handle is, is no move. `None` for an
    /// unknown handle or a drop that collapses an axis.
    let tryMoveHandle (r : EllipticAnnotationResult) (outline : V3d[]) (index : int) (drop : V3d) : Option<V3d[]> =
        match tryFrame r with
        | None -> None
        | Some (c, a, b, n) ->
            let toPlane (p : V3d) = p - n * Vec.dot (p - c) n
            let onPlane = toPlane drop
            match index with
            | 0 | 1 ->
                let fixedEnd = if index = 0 then c - a else c + a
                let center = (fixedEnd + onPlane) * 0.5
                let major = onPlane - center
                if major.Length <= 1e-9 * a.Length then None
                else
                    let minorDir =
                        let d = (Vec.cross n major).Normalized
                        if Vec.dot d b < 0.0 then -d else d
                    Some [| center - major; center + major; center + minorDir * b.Length |]
            | 2 | 3 ->
                let aDir = a.Normalized
                let v = onPlane - c
                let perpendicular = v - aDir * Vec.dot v aDir
                if perpendicular.Length <= 1e-9 * a.Length then None
                else Some [| c - a; c + a; c + perpendicular |]
            | CentreHandle ->
                let centre = c + (onPlane - toPlane (outlineCentre r outline))
                Some [| centre - a; centre + a; centre + b |]
            | _ -> None

    /// An edited ellipse re-fitted to the terrain, as the tool fits a drawn one: the plane
    /// through `draped`, the three construction points of the edit (`tryMoveHandle`: moved,
    /// widened or shifted on the old plane) stamped onto the terrain, which play the part of
    /// the three clicks. Unlike a redraw the axis lengths are the ones the edit asked for,
    /// exactly: the major axis turned as little as possible into the new plane (its direction
    /// projected onto it), the minor axis at right angles to it on the same side. The centre is where the sky ray through the
    /// moved centre - `up`, the direction the Sky projection drapes along - meets the new plane,
    /// so on sloped terrain it stays under the cursor (along the plane normal it would slide
    /// sideways by the height step times the slope). So a move changes where the ellipse is and
    /// how it is tilted - dip and strike of the new spot - but not its axes or axis ratio.
    ///
    /// Returns the new plane and the construction points on it. `None` when the draped points
    /// span no plane, the new plane stands at right angles to the old one, or it is vertical.
    let tryRefit (r : EllipticAnnotationResult) (up : V3d) (moved : V3d[]) (draped : V3d[]) : Option<Plane3d * V3d[]> =
        match tryFrame r, moved, draped with
        | Some (_, _, _, n), [| m0; m1; m2 |], [| p0; p1; p2 |] ->
            // the axes the edit asked for, on the old plane: a major end may have moved, a minor
            // end set a new width, or the whole ellipse shifted
            let movedCentre = (m0 + m1) * 0.5
            let a = m1 - movedCentre
            let b = m2 - movedCentre
            let normal = Vec.cross (p1 - p0) (p2 - p0)
            let scale = (p1 - p0).Length * (p2 - p0).Length
            if scale <= 0.0 || normal.Length <= 1e-9 * scale then None
            else
                // on the side of the old normal, so the minor axis keeps its side too
                let normal = normal.Normalized
                let normal = if Vec.dot normal n < 0.0 then -normal else normal
                let plane = Plane3d(normal, p0)
                let u = up.Normalized
                let along = Vec.dot u normal
                let majorDir = a - normal * Vec.dot a normal
                if majorDir.Length <= 1e-6 * a.Length || abs along < 1e-3 || u.AnyNaN then None
                else
                    let major = majorDir.Normalized * a.Length
                    let minorDir =
                        let d = (Vec.cross normal major).Normalized
                        if Vec.dot d b < 0.0 then -d else d
                    let centre = movedCentre - u * (plane.Height movedCentre / along)
                    Some (plane, [| centre - major; centre + major; centre + minorDir * b.Length |])
        | _ -> None

    /// Where the outline lies once the centre is dropped at `drop`, before it is draped again:
    /// the draped outline shifted within the plane by the move `tryMoveHandle` makes. The preview
    /// of a centre drag; `None` when the stored shape defines no plane.
    let tryMovedOutline (r : EllipticAnnotationResult) (outline : V3d[]) (drop : V3d) : Option<V3d[]> =
        tryFrame r |> Option.map (fun (c, _, _, n) ->
            let toPlane (p : V3d) = p - n * Vec.dot (p - c) n
            let shift = toPlane drop - toPlane (outlineCentre r outline)
            outline |> Array.map (fun p -> p + shift))


module AnnotationResults =
    
    let initial = 
        {
            version           = AnnotationResults.current
            height            = Double.NaN
            heightDelta       = Double.NaN
            avgAltitude       = Double.NaN
            length            = Double.NaN
            wayLength         = Double.NaN
            bearing           = Double.NaN
            slope             = Double.NaN
            trueThickness     = Double.NaN
            verticalThickness = Double.NaN
            area              = Double.NaN
        }  

type SemanticId = SemanticId of string
type SemanticType = Metric = 0 | Angular = 1 | Hierarchical = 2 | Undefined = 3

/// Where a generated profile came from (supporting profiles, #833). Fixed when the profile
/// is generated, like `majorAxisAzimuth` at ellipse construction: editing the profile
/// afterwards does not update it. Saved under the annotation's own `"profileOf"` key, so
/// releases that do not know it skip it.
type ProfileLink =
    {
        /// key of the ellipse or polyline the profile was generated from
        parent    : Guid
        /// 0, 1, 2, ... in generation order
        index     : int
        /// "N-S", "NE-SW", "E-W", "NW-SE" (compass), "major", "major+45", "minor",
        /// "major+135" (major axis), "" for polyline profiles
        direction : string
        /// deg, start -> end, clockwise from local north, 0-360
        azimuth   : float
        /// m along the parent polyline; None for ellipse profiles
        station   : Option<float>
    }
    with
        static let version = 0

        static member private readV0 =
            json {
                let! parent    = Json.read "parent"
                let! index     = Json.read "index"
                let! direction = Json.read "direction"
                let! azimuth   = Json.readFloat "azimuth"
                let! (station : Option<float>) = Json.tryRead "station"
                return {
                    parent    = Guid.Parse (parent : string)
                    index     = index
                    direction = direction
                    azimuth   = azimuth
                    station   = station
                }
            }

        static member FromJson(_ : ProfileLink) =
            json {
                let! v = Json.read "version"
                match v with
                | 0 -> return! ProfileLink.readV0
                | _ -> return! v |> sprintf "don't know version %A of ProfileLink" |> Json.error
            }

        static member ToJson (x : ProfileLink) =
            json {
                do! Json.write      "version"   version
                do! Json.write      "parent"    (x.parent.ToString())
                do! Json.write      "index"     x.index
                do! Json.write      "direction" x.direction
                do! Json.writeFloat "azimuth"   x.azimuth
                match x.station with
                | Some s -> do! Json.writeFloat "station" s
                | None   -> ()
            }

/// The shape model an annotation was drawn on (#887, docs/AnnotationMetadata.md). Captured
/// from the surface when the annotation is finished and kept with it, so replacing the OPC
/// later does not change what the annotation reports.
type ShapeModelRef =
    {
        /// the surface's OPC product, e.g. FITS PRODNAME or the opcx file name
        product   : string
        /// name and version of the SPC model (FITS DATASRCV), when the OPC carries a FITS header
        modelName : Option<string>
        /// product version: FITS MAP_VER, or the `vNNN` of an SPC file name
        version   : Option<string>
        /// where `version` came from: "fits-header", "file-name" or "none"
        source    : string
        /// who made the model: as the product states it, else the archive's citation
        /// (ShapeModelCredits)
        credit    : Option<string>
    }
    with
        static let version = 0

        static member private readV0 =
            json {
                let! product   = Json.read "product"
                let! modelName = Json.tryRead "modelName"
                let! ver       = Json.tryRead "modelVersion"
                let! source    = Json.tryRead "source"
                let! credit    = Json.tryRead "credit"
                return {
                    product   = product
                    modelName = modelName
                    version   = ver
                    source    = source |> Option.defaultValue "none"
                    credit    = credit
                }
            }

        static member FromJson(_ : ShapeModelRef) =
            json {
                let! v = Json.read "version"
                match v with
                | 0 -> return! ShapeModelRef.readV0
                | _ -> return! v |> sprintf "don't know version %A of ShapeModelRef" |> Json.error
            }

        static member ToJson (x : ShapeModelRef) =
            json {
                do! Json.write "version" version
                do! Json.write "product" x.product
                match x.modelName with
                | Some n -> do! Json.write "modelName" n
                | None   -> ()
                match x.version with
                | Some v -> do! Json.write "modelVersion" v
                | None   -> ()
                do! Json.write "source" x.source
                match x.credit with
                | Some c -> do! Json.write "credit" c
                | None   -> ()
            }

/// Who made a shape model, credited wherever the model is shown (#887,
/// docs/AnnotationMetadata.md): the published citation of the archive the model comes from,
/// for products the producer's sidecar does not credit itself. Only citations checked against
/// the archive go in here.
module ShapeModelCredits =

    /// A product family (regex over the product name, e.g. dimorphos_g_0243mm_spc_dtm_0000n00000_v004,
    /// or over the SPC model name, FITS DATASRCV) and the citation of its archive.
    type Citation = { products : string; citation : string }

    let citations : list<Citation> =
        [
            // PDS SBN, urn:nasa:pds:dart_shapemodel:data_derived_dimorphos_model_v004 -- the
            // global DTM and the local models of the final pre-impact Dimorphos model (SPC
            // model dalyErnst_dimorphos230530). Checked 2026-11-06.
            {
                products = @"^dimorphos_[gl]_.*_spc_.*_v004$|^dalyErnst_dimorphos230530$"
                citation =
                    "Daly, T., Barnouin, O., Ernst, C., Nair, H., Espiritu, R., Waller, D. (2023). "
                    + "Derived data products for DART shapemodel: dimorphos_model_v004. "
                    + "NASA Planetary Data System. doi:10.26007/0nss-vd15"
            }
        ]

    /// The citation of the first family any of `names` belongs to.
    let tryCredit (names : list<string>) : Option<string> =
        citations
        |> List.tryPick (fun c ->
            let matches (n : string) =
                not (String.IsNullOrWhiteSpace n)
                && Text.RegularExpressions.Regex.IsMatch(n.Trim(), c.products, Text.RegularExpressions.RegexOptions.IgnoreCase)
            if names |> List.exists matches then Some c.citation else None)

/// A projected image that was painted under an annotation's points while it was traced
/// (#887): per point, the top image of the projection stack that covers it and faces it.
type ProjectedImageRef =
    {
        /// image file name, without directory
        image      : string
        instrument : string
        /// observation time, UTC, ISO 8601
        time       : string
    }
    with
        static let version = 0

        static member private readV0 =
            json {
                let! image      = Json.read "image"
                let! instrument = Json.tryRead "instrument"
                let! time       = Json.tryRead "time"
                return {
                    image      = image
                    instrument = instrument |> Option.defaultValue ""
                    time       = time |> Option.defaultValue ""
                }
            }

        static member FromJson(_ : ProjectedImageRef) =
            json {
                let! v = Json.read "version"
                match v with
                | 0 -> return! ProjectedImageRef.readV0
                | _ -> return! v |> sprintf "don't know version %A of ProjectedImageRef" |> Json.error
            }

        static member ToJson (x : ProjectedImageRef) =
            json {
                do! Json.write "version"    version
                do! Json.write "image"      x.image
                do! Json.write "instrument" x.instrument
                do! Json.write "time"       x.time
            }

/// Provenance and quality of an annotation (#887, docs/AnnotationMetadata.md). Saved under
/// the annotation's own keys "confidence", "comment", "shapeModel" and "projectedImagesUsed",
/// which releases that do not know them skip.
type AnnotationMetadata =
    {
        /// 1 (lowest) to 4 (highest), from the identification criteria; None = not rated
        confidence          : Option<int>
        comment             : string
        shapeModel          : Option<ShapeModelRef>
        /// in order of first appearance along the points, each image once
        projectedImagesUsed : list<ProjectedImageRef>
    }

module AnnotationMetadata =
    let empty =
        {
            confidence          = None
            comment             = ""
            shapeModel          = None
            projectedImagesUsed = []
        }

    let minConfidence = 1
    let maxConfidence = 4

    /// None outside 1..4
    let validConfidence (c : int) =
        if c >= minConfidence && c <= maxConfidence then Some c else None

    /// e.g. "dalyErnst_dimorphos230530 004 (dimorphos_g_0243mm_spc_dtm_0000n00000_v004)"
    let describeShapeModel (sm : ShapeModelRef) =
        let name = sm.modelName |> Option.defaultValue sm.product
        let version = sm.version |> Option.map (fun v -> " " + v) |> Option.defaultValue ""
        let product = if sm.modelName.IsSome then sprintf " (%s)" sm.product else ""
        let source = if sm.source = "file-name" then ", version from file name" else ""
        name + version + product + source

[<ModelType>]
type Annotation = {
    version        : int
    
    [<NonAdaptive>]
    key            : Guid
                   
    modelTrafo     : Trafo3d

    referenceSystem : Option<SpiceReferenceSystem>
                   
    geometry       : Geometry
    projection     : Projection         
    bookmarkId     : option<System.Guid>
    semantic       : Semantic
                   
    points         : IndexList<V3d>
    segments       : IndexList<Segment>
                   
    color          : ColorInput
    thickness      : NumericInput
                   
    results        : Option<AnnotationResults>
    dnsResults     : Option<DipAndStrikeResults>
    ellipticResults : Option<EllipticAnnotationResult>
                   
    visible          : bool
    showDns          : bool
    text             : string
    textsize         : NumericInput
    showText         : bool
    manualDipAngle   : NumericInput
    manualDipAzimuth : NumericInput
                 
    surfaceName    : string
    view           : CameraView
                   
    semanticId     : SemanticId
    semanticType   : SemanticType

    crossSectionClipping : bool
    crossSectionRefPoint : Option<V3d>

    /// fills the interior of closed geometries (polygon, ellipses); ignored for open ones
    showFill   : bool
    fillColor  : ColorInput
    fillAlpha  : NumericInput

    /// The geological unit a label point assigns to the map unit it lies in (#843,
    /// docs/GeologicalMap.md): a GeoUnit.code. None for every other annotation.
    geoUnit    : Option<string>
    /// set on profiles generated from another annotation (supporting profiles, #833)
    profileOf  : Option<ProfileLink>
    /// confidence, comment, shape model and projected images used (#887)
    metadata   : AnnotationMetadata
}
with
    static member current = 5
    static member initialManualDipAngle = {
        value   = Double.NaN
        min     = 0.0
        max     = 90.0
        step    = 0.1
        format  = "{0:0.0}"
    }

    static member initialmanualDipAzimuth = {
        value   = Double.NaN
        min     = 0.0
        max     = 360.0
        step    = 0.1
        format  = "{0:0.0}"
    }

    // lives on the type rather than in module Annotation.Initial because the version readers
    // below need it, and the module is defined after the type
    static member initialFillAlpha = {
        value   = 0.35
        min     = 0.0
        max     = 1.0
        step    = 0.05
        format  = "{0:0.00}"
    }
        
    static member private readV0 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            
            let! points       = Json.read "points"
            let! segments     = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible      = Json.read "visible"
            let! showDns      = Json.read "showDns"
            
            let! text         = Json.read "text"
            
            let! textSize     = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"            
            
            let! surfaceName  = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> Projection.ofInt
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> Serialization.jsonSerializer.UnPickleOfString
                segments         = segments      |> Serialization.jsonSerializer.UnPickleOfString
                color            = color
                thickness        = thickness      
                results          = results    
                dnsResults       = dnsResults         
                visible          = visible 
                showDns          = showDns   
                text             = text      
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = SemanticId ""
                semanticType     = SemanticType.Undefined
                manualDipAngle   = Annotation.initialManualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = None
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
                geoUnit              = None
                profileOf            = None
                metadata             = AnnotationMetadata.empty
            }
        }

    static member private readV1 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            
            let! points   = Json.read "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> Projection.ofInt
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> Serialization.jsonSerializer.UnPickleOfString
                segments         = segments      |> Serialization.jsonSerializer.UnPickleOfString
                color            = color
                thickness        = thickness      
                results          = results    
                dnsResults       = dnsResults         
                visible          = visible 
                showDns          = showDns   
                text             = text      
                textsize         = textSize         
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView 
                semanticId       = semanticId |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = Annotation.initialManualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = None
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
                geoUnit              = None
                profileOf            = None
                metadata             = AnnotationMetadata.empty
            }
        }

    static member private readV2 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> Projection.ofInt
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = Annotation.initialManualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = None
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
                geoUnit              = None
                profileOf            = None
                metadata             = AnnotationMetadata.empty
            }
        }

    static member private readV3 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! bookmarkId   = Json.tryRead "bookmarkId"
            let! semantic     = Json.read "semantic"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"

            let! manualDipAngle = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAngle"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> Projection.ofInt
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = manualDipAngle
                manualDipAzimuth = Annotation.initialmanualDipAzimuth
                bookmarkId       = bookmarkId
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
                geoUnit              = None
                profileOf            = None
                metadata             = AnnotationMetadata.empty
            }
        }

    static member private readV4 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            let! bookmarkId   = Json.tryRead "bookmarkId"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
    
            let! manualDipAngle = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAngle"
            let! manualDipAzimuth = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAzimuth"
            
            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse        
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> Projection.ofInt
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = true
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = manualDipAngle
                manualDipAzimuth = manualDipAzimuth
                bookmarkId       = bookmarkId
                referenceSystem  = None
                ellipticResults  = None
                crossSectionClipping = false
                crossSectionRefPoint = None
                showFill             = false
                fillColor            = color
                fillAlpha            = Annotation.initialFillAlpha
                geoUnit              = None
                profileOf            = None
                metadata             = AnnotationMetadata.empty
            }
        }

    static member private readV5 =
        json {
            let! key          = Json.read "key"
            let! modelTrafo   = Json.read "modelTrafo" //|> Trafo3d.Parse
            let! geometry     = Json.read "geometry"
            let! projection   = Json.read "projection"
            let! semantic     = Json.read "semantic"
            let! bookmarkId   = Json.tryRead "bookmarkId"
            
            let! points   = Json.readWith Ext.fromJson<list<V3d>,Ext> "points"
            let! segments = Json.read "segments"
            
            let! color        = Json.readWith Ext.fromJson<ColorInput,Ext> "color"
            let! thickness    = Json.readWith Ext.fromJson<NumericInput,Ext> "thickness"
            
            let! results      = Json.read "results"
            let! dnsResults   = Json.read "dnsResults"
            
            let! visible  = Json.read "visible"
            let! showDns  = Json.read "showDns"
            let! text     = Json.read "text"
            let! textSize = Json.readWith Ext.fromJson<NumericInput,Ext> "textsize"
            let! showText = Json.read "showText"
            
            let! surfaceName = Json.read "surfaceName"
            
            let! (cameraView : list<string>) = Json.read "view"
            
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])
            
            let! semanticId    = Json.read "semanticId"
            let! semanticType  = Json.read "semanticType"
    
            let! manualDipAngle = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAngle"
            let! manualDipAzimuth = Json.readWith Ext.fromJson<NumericInput,Ext> "manualDipAzimuth"

            let! geographicalEllipse = Json.tryRead "ellipseResults"
            let! ellipseShape = Json.tryRead "ellipseShape"
            let ellipseProperties = EllipticAnnotationResult.ofJson geographicalEllipse ellipseShape

            let! crossSectionClipping = Json.tryRead "crossSectionClipping"
            let! crossSectionRefPoint = Json.tryRead "crossSectionRefPoint"
            let crossSectionRefPoint : Option<V3d> =
                crossSectionRefPoint |> Option.map V3d.Parse

            // optional, no version bump - same approach as crossSectionClipping above.
            // primitives rather than ColorInput/NumericInput, so the input records can be
            // rebuilt with current min/max/step instead of pinning stale bounds in the file.
            let! showFill  = Json.tryRead "showFill"
            let! fillColor = Json.tryRead "fillColor"
            let! fillAlpha = Json.tryRead "fillAlpha"
            // optional, no version bump (#843)
            let! geoUnit   = Json.tryRead "geoUnit"

            // optional, no version bump: older releases skip the key (#833). Read raw and
            // deserialised inside a try: a link from a later version, or a damaged one, is
            // dropped to None rather than failing the whole scene - it is derived data
            let! (profileOfJson : Option<Chiron.Json>) = Json.tryRead "profileOf"
            let profileOf : Option<ProfileLink> =
                profileOfJson
                |> Option.bind (fun j ->
                    try Some (Json.deserialize j)
                    with e ->
                        Log.warn "[Annotation] profileOf could not be read, dropped: %s" e.Message
                        None)

            // optional, no version bump (#887): older releases skip these keys. The nested
            // records are read raw and deserialised inside a try like profileOf above, so a
            // damaged entry is dropped instead of failing the scene
            // all four read raw: a typed tryRead fails the annotation, and with it the scene,
            // when a key holds the wrong type
            let! (confidenceJson : Option<Chiron.Json>) = Json.tryRead "confidence"
            let! (commentJson : Option<Chiron.Json>) = Json.tryRead "comment"
            let! (shapeModelJson : Option<Chiron.Json>) = Json.tryRead "shapeModel"
            let! (imagesJson : Option<Chiron.Json>) = Json.tryRead "projectedImagesUsed"
            let confidence =
                match confidenceJson with
                | Some (Chiron.Number n) when n = Math.Round n && n >= 1M && n <= 4M -> Some (int n)
                | Some j -> Log.warn "[Annotation] confidence %A is not 1-4, dropped" j; None
                | None -> None
            let comment =
                match commentJson with
                | Some (Chiron.String s) -> Some s
                | Some j -> Log.warn "[Annotation] comment %A is not text, dropped" j; None
                | None -> None
            let shapeModel : Option<ShapeModelRef> =
                shapeModelJson
                |> Option.bind (fun j ->
                    try Some (Json.deserialize j)
                    with e ->
                        Log.warn "[Annotation] shapeModel could not be read, dropped: %s" e.Message
                        None)
            let projectedImagesUsed : list<ProjectedImageRef> =
                let entries =
                    match imagesJson with
                    | Some (Chiron.Array entries) -> entries
                    | Some j -> Log.warn "[Annotation] projectedImagesUsed %A is not a list, dropped" j; []
                    | None -> []
                entries
                |> List.choose (fun j ->
                    try Some (Json.deserialize j)
                    with e ->
                        Log.warn "[Annotation] projected image entry could not be read, dropped: %s" e.Message
                        None)
            let metadata =
                {
                    confidence          = confidence
                    comment             = comment |> Option.defaultValue ""
                    shapeModel          = shapeModel
                    projectedImagesUsed = projectedImagesUsed
                }

            return {
                version          = Annotation.current
                key              = key           |> Guid.Parse
                modelTrafo       = modelTrafo    |> Trafo3d.Parse
                geometry         = geometry      |> enum<Geometry>
                projection       = projection    |> Projection.ofInt
                semantic         = semantic      |> enum<Semantic>
                points           = points        |> IndexList.ofList
                segments         = segments      |> IndexList.ofList
                color            = color
                thickness        = thickness
                results          = results
                dnsResults       = dnsResults
                visible          = visible
                showDns          = showDns
                text             = text
                textsize         = textSize
                showText         = showText
                surfaceName      = surfaceName
                view             = cameraView
                semanticId       = semanticId   |> SemanticId
                semanticType     = semanticType |> enum<SemanticType>
                manualDipAngle   = manualDipAngle
                manualDipAzimuth = manualDipAzimuth
                bookmarkId       = bookmarkId
                referenceSystem  = None
                ellipticResults  = ellipseProperties
                crossSectionClipping = crossSectionClipping |> Option.defaultValue false
                crossSectionRefPoint = crossSectionRefPoint
                showFill             = showFill |> Option.defaultValue false
                fillColor            =
                    fillColor
                    |> Option.map (fun (s : string) -> { c = C4b.Parse s })
                    |> Option.defaultValue color
                fillAlpha            =
                    { Annotation.initialFillAlpha with
                        value = fillAlpha |> Option.defaultValue Annotation.initialFillAlpha.value }
                geoUnit              = geoUnit
                profileOf            = profileOf
                metadata             = metadata
            }
        }

    static member FromJson(_:Annotation) =
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Annotation.readV0
            | 1 -> return! Annotation.readV1
            | 2 -> return! Annotation.readV2
            | 3 -> return! Annotation.readV3
            | 4 -> return! Annotation.readV4
            | 5 -> return! Annotation.readV5
            | _ -> return! v |> sprintf "don't know version %A of Annotation" |> Json.error
        }
    
    static member ToJson (x : Annotation) =
        json {
            do! Json.write "version"    x.version
            do! Json.write "key"        (x.key.ToString())
            do! Json.write "modelTrafo" (x.modelTrafo.ToString())
            do! Json.write "geometry"   (x.geometry |> int)
            do! Json.write "projection" (x.projection |> int)
            if x.bookmarkId.IsSome then
                do! Json.write "bookmarkId" (x.bookmarkId.Value.ToString())
            do! Json.write "semantic"   (x.semantic |> int)
            do! Json.writeWith (Ext.toJson<list<V3d>,Ext>) "points" (x.points |> IndexList.toList)        
            do! Json.write "segments"   (x.segments |> IndexList.toList)
            do! Json.writeWith (Ext.toJson<ColorInput,Ext>) "color" x.color
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "thickness" x.thickness
            do! Json.write "results"     x.results
            do! Json.write "dnsResults"  x.dnsResults
            do! Json.write "visible"     x.visible
            do! Json.write "showDns"     x.showDns
            do! Json.write "text"        x.text
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "textsize" x.textsize
            do! Json.write "showText"    x.showText
            do! Json.write "surfaceName" x.surfaceName
            
            let camView = x.view
            let camView = 
                [camView.Sky; camView.Location; camView.Forward; camView.Up ; camView.Right] 
                |> List.map(fun x -> x.ToString())

            do! Json.write "view" camView
            do! Json.write "semanticId" (x.semanticId.ToString())
            do! Json.write "semanticType" (x.semanticType |> int)

            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "manualDipAngle" (x.manualDipAngle)
            do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "manualDipAzimuth" (x.manualDipAzimuth)    
            
            // "ellipseResults" keeps the schema every release reads; the metric shape goes
            // under its own key, which older releases skip (see GeographicalEllipseJson)
            match x.ellipticResults |> Option.map EllipticAnnotationResult.toJson with
            | None -> ()
            | Some (geographical, shape) ->
                match geographical with
                | Some g -> do! Json.write "ellipseResults" g
                | None -> ()
                match shape with
                | Some sh -> do! Json.write "ellipseShape" sh
                | None -> ()

            do! Json.write "crossSectionClipping" x.crossSectionClipping
            match x.crossSectionRefPoint with
            | Some rp -> do! Json.write "crossSectionRefPoint" (rp.ToString())
            | None -> ()

            // written unconditionally: gating on showFill would discard a configured fill
            // colour and alpha as soon as the user switches the fill off and saves
            do! Json.write "showFill"  x.showFill
            do! Json.write "fillColor" (x.fillColor.c.ToString())
            do! Json.write "fillAlpha" x.fillAlpha.value
            match x.geoUnit with
            | Some u -> do! Json.write "geoUnit" u
            | None -> ()

            match x.profileOf with
            | Some link -> do! Json.write "profileOf" link
            | None      -> ()

            // #887: written only when set, so unrated annotations keep their old shape
            match x.metadata.confidence with
            | Some c -> do! Json.write "confidence" c
            | None   -> ()
            if not (String.IsNullOrEmpty x.metadata.comment) then
                do! Json.write "comment" x.metadata.comment
            match x.metadata.shapeModel with
            | Some sm -> do! Json.write "shapeModel" sm
            | None    -> ()
            if not x.metadata.projectedImagesUsed.IsEmpty then
                do! Json.write "projectedImagesUsed" x.metadata.projectedImagesUsed
        }

module Annotation =
         
    module Initial =
        let samplingAmount = {
            value   = 1.0
            min     = 0.001
            max     = 1000.0
            step    = 0.001
            format  = "{0:0.000}"
        }

        let thickness = {
            value   = 3.0
            min     = 1.0
            max     = 8.0
            step    = 1.0
            format  = "{0:0}"
        }
        
        let textSize = {
            value   = 0.05
            min     = 0.01
            max     = 5.0
            step    = 0.01
            format  = "{0:0.00}"
        }        
          
    let thickness = [1.0; 2.0; 3.0; 4.0; 5.0; 1.0; 1.0]
    let color = 
        [
            new C4b(241,238,246); 
            new C4b(189,201,225); 
            new C4b(116,169,207); 
            new C4b(43,140,190); 
            new C4b(4,90,141); 
            new C4b(241,163,64); 
            new C4b(153,142,195) 
        ]
    
    let make 
        (projection : Projection) 
        (bookmarkId : Option<Guid>)
        (geometry : Geometry) 
        (referenceSystem : Option<SpiceReferenceSystem>)
        (color : ColorInput) 
        (thickness : NumericInput) 
        (surfName : string) 
        : Annotation  =

        {
            version          = Annotation.current
            key              = Guid.NewGuid()
            geometry         = geometry
            semantic         = Semantic.None
            points           = IndexList.Empty
            segments         = IndexList.Empty //[]
            color            = color
            thickness        = thickness
            results          = None
            dnsResults       = None
            projection       = projection
            visible          = true
            text             = ""
            textsize         = Initial.textSize
            showText         = true
            modelTrafo       = Trafo3d.Identity
            showDns          = 
                match geometry with 
                | Geometry.DnS | Geometry.TT -> true                 
                | _ -> false
            surfaceName      = surfName
            view             = FreeFlyController.initial.view
            semanticId       = SemanticId ""
            semanticType     = SemanticType.Undefined
            manualDipAngle   = Annotation.initialManualDipAngle
            manualDipAzimuth = Annotation.initialmanualDipAzimuth 
            bookmarkId       = bookmarkId
            referenceSystem  = referenceSystem
            ellipticResults  = None
            crossSectionClipping = false
            crossSectionRefPoint = None
            showFill             = false
            fillColor            = color
            fillAlpha            = Annotation.initialFillAlpha
            geoUnit              = None
            profileOf            = None
            metadata             = AnnotationMetadata.empty
        }

    let initial =
        make Projection.Viewpoint None Geometry.Polyline None { c = C4b.Magenta } Initial.thickness ""

    /// Whether an annotation's segments form a closed ring. `closePolyline` appends one segment
    /// joining the last point back to the first, so a ring has as many segments as points, an
    /// open chain one fewer. Derived from the counts rather than from `geometry`, because that
    /// is the invariant the segment indices rest on.
    let hasClosingSegment (pointCount : int) (segmentCount : int) =
        segmentCount >= pointCount

    /// Whether segment `i` (`startPoint` -> `endPoint`) is a ring's closing segment as
    /// `closePolyline` stores it: first -> last (#821), so it is read backwards to walk the ring.
    /// The endpoints are checked too: a closing segment stored last -> first is left as it is.
    let isReversedClosingSegment (points : IndexList<V3d>) (segmentCount : int) (i : int) (startPoint : V3d) (endPoint : V3d) =
        hasClosingSegment points.Count segmentCount && i = segmentCount - 1
        && IndexList.tryFirst points = Some startPoint && IndexList.tryLast points = Some endPoint

    /// The sampled line in drawing order: each segment's start, samples and end - a ring's
    /// closing segment backwards, so the line runs last point -> first point.
    let retrievePoints (a : Annotation) =
        if a.segments.Count = 0 then
            a.points |> IndexList.toList
        else
            a.segments
            |> IndexList.toList
            |> List.indexed
            |> List.collect (fun (i, x) ->
                let ps = [ yield x.startPoint; yield! x.points |> IndexList.toSeq; yield x.endPoint ]
                if isReversedClosingSegment a.points a.segments.Count i x.startPoint x.endPoint then List.rev ps else ps)
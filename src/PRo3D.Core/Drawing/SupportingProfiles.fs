namespace PRo3D.Core.Drawing

open System

open Aardvark.Base
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Base.Annotation

/// Supporting profiles (#833, docs/SupportingProfiles.md): Sky-projected lines generated from a
/// polyline (perpendicular, every `spacing` metres) or an ellipse (4 through the centre, rim to
/// rim). Geometry only; `DrawingApp` turns the specs into annotations.
///
/// All or nothing: one profile that cannot be built fails the whole generation, because a crater
/// with three of its four profiles is not something anybody asked for.
module SupportingProfiles =

    /// One profile to create: its two ends, both on the surface, and where it came from.
    type ProfileSpec = {
        start : V3d
        stop  : V3d
        link  : ProfileLink
    }

    /// How the profiles of a polyline are placed along it.
    type Placement =
        /// one every so many metres
        | Spacing of float
        /// this many, equally spaced over the whole polyline
        | Count of int

    /// Compass profiles in generation order, with the bearing from the centre to the start end.
    let compass = [| "N-S", 0.0; "NE-SW", 45.0; "E-W", 90.0; "NW-SE", 315.0 |]

    /// Major-axis profiles in generation order, with the angle from the major axis, clockwise
    /// seen from above, in the ellipse's own plane.
    let majorAxis = [| "major", 0.0; "major+45", 45.0; "minor", 90.0; "major+135", 135.0 |]

    /// Bearing of the horizontal part of `dir`, clockwise from north, [0, 360). NaN when `dir` is
    /// vertical or north is undefined.
    let azimuth (up : V3d) (north : V3d) (dir : V3d) =
        let u = up.Normalized
        let h = dir - u * Vec.dot dir u
        let n = (north - u * Vec.dot north u).Normalized
        let e = Vec.cross n u
        if dir.Length = 0.0 || h.Length < 1e-9 * dir.Length || n.AnyNaN then
            Double.NaN
        else
            let a = atan2 (Vec.dot h e) (Vec.dot h n) * Constant.DegreesPerRadian
            let a = if a < 0.0 then a + 360.0 else a
            // due north comes out a hair either side of 0 (1e-16 or 359.99999...); both are 0
            if a >= 360.0 - 1e-9 || a < 1e-9 then 0.0 else a

    /// Arc-length positions of the polyline profiles: 0, d, 2d, ... and the end itself, unless
    /// the last multiple of `d` already is the end.
    let stations (spacing : float) (total : float) : float[] =
        if spacing <= 0.0 || total <= 0.0 || Double.IsNaN spacing || Double.IsNaN total then
            [||]
        else
            let n = int (floor (total / spacing + 1e-9))
            let regular = Array.init (n + 1) (fun k -> float k * spacing)
            if total - float n * spacing > 1e-9 * max 1.0 total then Array.append regular [| total |]
            else regular

    /// Arc-length positions of the profiles of a polyline `total` metres long.
    ///
    /// - `Spacing d`: 0, d, 2d, ... and the end itself (`stations`); `atStart` / `atEnd`
    ///   decide whether the profiles at 0 and at the end are kept.
    /// - `Count n`: `n` profiles with equal gaps. An excluded end still counts as a station
    ///   without a profile, so the gaps stay equal: both ends `0 .. total` in `n - 1` gaps,
    ///   one end `n` gaps, neither `n + 1` gaps.
    let stationsFor (placement : Placement) (atStart : bool) (atEnd : bool) (total : float) : Result<float[], string> =
        if not (total > 0.0) then
            Result.Error "the polyline has no length"
        else
            match placement with
            | Spacing d when not (d > 0.0) ->
                Result.Error "the spacing must be positive"
            | Spacing d ->
                let all = stations d total
                let last = all.Length - 1
                all
                |> Array.indexed
                |> Array.choose (fun (i, s) ->
                    if (i = 0 && not atStart) || (i = last && not atEnd) then None else Some s)
                |> Result.Ok
            | Count n when n < 1 ->
                Result.Error "the count must be at least 1"
            | Count n when n < 2 && atStart && atEnd ->
                Result.Error "with profiles at both ends the count must be at least 2"
            | Count n ->
                let gaps, first =
                    match atStart, atEnd with
                    | true,  true  -> n - 1, 0
                    | true,  false -> n,     0
                    | false, true  -> n,     1
                    | false, false -> n + 1, 1
                let gap = total / float gaps
                Array.init n (fun k ->
                    // the last profile with the end included sits exactly on it
                    if atEnd && k = n - 1 then total else float (first + k) * gap)
                |> Result.Ok

    /// The segment a station lies on and where on it, `0 <= t <= 1`. A station exactly on a
    /// vertex belongs to the segment leaving it; the end of the line to the last segment.
    /// Zero-length segments (a point clicked twice) are skipped.
    let private locate (cumulative : float[]) (s : float) : Option<int * float> =
        let mutable found = None
        for j in 0 .. cumulative.Length - 2 do
            let len = cumulative.[j + 1] - cumulative.[j]
            if len > 0.0 && cumulative.[j] <= s then found <- Some j
        found |> Option.map (fun j ->
            let len = cumulative.[j + 1] - cumulative.[j]
            j, max 0.0 (min 1.0 ((s - cumulative.[j]) / len)))

    let private allOrNothing (results : Result<ProfileSpec, string>[]) : Result<list<ProfileSpec>, string> =
        match results |> Array.tryPick (function Result.Error e -> Some e | _ -> None) with
        | Some e -> Result.Error e
        | None   -> results |> Array.choose (function Result.Ok s -> Some s | _ -> None) |> Array.toList |> Result.Ok

    /// Profiles perpendicular to a polyline, placed along the line's 3D length (along its
    /// clicked points) as `stationsFor` says, `length` long and centred on the line. Each crosses the
    /// segment it lies on at a right angle in the horizontal plane and starts on its left,
    /// looking along the line. Both ends are dropped onto the surface with `project`.
    let forPolyline
        (planet  : Planet) (up : V3d) (north : V3d)
        (project : V3d -> Option<V3d>)
        (placement : Placement) (atStart : bool) (atEnd : bool) (length : float)
        (parent  : Annotation) : Result<list<ProfileSpec>, string> =

        let points = parent.points |> IndexList.toArray
        if points.Length < 2 then
            Result.Error "the polyline needs at least two points"
        elif not (length > 0.0) then
            Result.Error "the profile length must be positive"
        else
            let cumulative = Array.zeroCreate points.Length
            for i in 1 .. points.Length - 1 do
                cumulative.[i] <- cumulative.[i - 1] + Vec.distance points.[i - 1] points.[i]
            let total = cumulative.[points.Length - 1]
            match stationsFor placement atStart atEnd total with
            | Result.Error e -> Result.Error e
            | Result.Ok [||] ->
                Result.Error (sprintf "no profile is left on this %.2f m polyline with these settings" total)
            | Result.Ok stations ->
                stations
                |> Array.mapi (fun k s ->
                    match locate cumulative s with
                    | None -> Result.Error (sprintf "profile %d: no segment at %.2f m" k s)
                    | Some (j, t) ->
                        let a, b = points.[j], points.[j + 1]
                        let p = a + (b - a) * t
                        let u, n = Calculations.localFrame planet up north p
                        let left = Vec.cross u (b - a)
                        if left.Length < 1e-9 * (b - a).Length then
                            Result.Error (sprintf "profile %d (at %.2f m): the polyline runs vertically there" k s)
                        else
                            let left = left.Normalized
                            let half = length * 0.5
                            match project (p + left * half), project (p - left * half) with
                            | Some s0, Some s1 ->
                                Result.Ok {
                                    start = s0
                                    stop  = s1
                                    link  = { parent = parent.key; index = k; direction = ""
                                              azimuth = azimuth u n left; station = Some s } }
                            | _ ->
                                Result.Error (sprintf "profile %d (at %.2f m): an end misses the surface" k s))
                |> allOrNothing

    /// Where the ray from the centre along `dir` (in the ellipse plane) leaves the outline:
    /// `outline2d` is the outline in plane coordinates around the centre, `outline` the same
    /// points on the surface. The hit is interpolated between the two surface points, so the
    /// profile ends exactly on the drawn rim.
    let rimHit (outline2d : V2d[]) (outline : V3d[]) (dir : V2d) : Option<V3d> =
        let n = outline2d.Length
        let mutable best = None
        for i in 0 .. n - 1 do
            let i1 = (i + 1) % n
            let p0 = outline2d.[i]
            let e  = outline2d.[i1] - p0
            // t * dir = p0 + s * e
            let det = e.X * dir.Y - dir.X * e.Y
            if abs det > 1e-12 then
                let t = (e.X * p0.Y - p0.X * e.Y) / det
                let s = (dir.X * p0.Y - dir.Y * p0.X) / det
                if t > 0.0 && s >= 0.0 && s <= 1.0 then
                    match best with
                    | Some (bt, _) when bt <= t -> ()
                    | _ -> best <- Some (t, outline.[i] + (outline.[i1] - outline.[i]) * s)
        best |> Option.map snd

    /// Four profiles through an ellipse's centre, rim to rim (see `compass` and `majorAxis`).
    /// Reads the shape stored at construction (`ellipticResults`) for the centre and plane, and
    /// the draped outline (`points`) for the rim, so asymmetric four-point ellipses end on their
    /// actual outline too.
    let forEllipse
        (planet : Planet) (up : V3d) (north : V3d)
        (mode   : EllipseProfileMode)
        (parent : Annotation) : Result<list<ProfileSpec>, string> =

        let outline = parent.points |> IndexList.toArray
        match parent.ellipticResults with
        | None ->
            Result.Error "the ellipse has no stored shape (drawn with an older release?)"
        | Some e when e.center.AnyNaN || e.semiMajorAxis.AnyNaN || e.semiMinorAxis.AnyNaN
                      || e.semiMajorAxis.Length = 0.0 || e.semiMinorAxis.Length = 0.0 ->
            Result.Error "the ellipse has no stored shape (drawn with an older release?)"
        | Some _ when outline.Length < 3 ->
            Result.Error "the ellipse has no outline"
        | Some _ when mode = EllipseProfileMode.Compass && planet = Planet.None ->
            Result.Error "compass directions need a reference body"
        | Some e ->
            let c = e.center
            let u, n = Calculations.localFrame planet up north c
            let ax0 = e.semiMajorAxis.Normalized
            let ax1 = e.semiMinorAxis.Normalized
            let normal = Vec.cross ax0 ax1
            // the normal on the sky side, so "clockwise" means seen from above
            let normal = if Vec.dot normal u < 0.0 then -normal else normal
            if Vec.dot normal u < 1e-3 then
                Result.Error "the ellipse stands vertically; it has no horizontal directions"
            else
                let to2d (dir : V3d) = V2d(Vec.dot dir ax0, Vec.dot dir ax1)
                let outline2d = outline |> Array.map (fun q -> to2d (q - c))

                let directions =
                    match mode with
                    | EllipseProfileMode.MajorAxis ->
                        // start at the end of the major axis lying more to the north
                        let a0 = if Vec.dot ax0 n < 0.0 then -ax0 else ax0
                        let cw = Vec.cross a0 normal
                        majorAxis |> Array.map (fun (label, deg) ->
                            let phi = deg * Constant.RadiansPerDegree
                            label, a0 * cos phi + cw * sin phi, None)
                    | _ ->
                        // the in-plane direction straight above/below the compass bearing:
                        // its horizontal part points exactly at the bearing
                        let hn = (n - u * Vec.dot n u).Normalized
                        let he = Vec.cross hn u
                        compass |> Array.map (fun (label, deg) ->
                            let phi = deg * Constant.RadiansPerDegree
                            let h = hn * cos phi + he * sin phi
                            // the bearing is exact by construction; store it as asked, not as
                            // re-measured with floating-point noise (45.000000000000014)
                            label, h - u * (Vec.dot h normal / Vec.dot u normal), Some deg)

                directions
                |> Array.mapi (fun k (label, dir, bearing) ->
                    let d2 = (to2d dir).Normalized
                    match rimHit outline2d outline d2, rimHit outline2d outline -d2 with
                    | Some s0, Some s1 ->
                        Result.Ok {
                            start = s0
                            stop  = s1
                            link  = { parent = parent.key; index = k; direction = label
                                      azimuth = bearing |> Option.defaultWith (fun () -> azimuth u n dir)
                                      station = None } }
                    | _ ->
                        Result.Error (sprintf "profile %s: the outline does not enclose the centre" label))
                |> allOrNothing

    let isPolyline (g : Geometry) = g = Geometry.Polyline || g = Geometry.Line

    let isEllipse (g : Geometry) =
        g = Geometry.AxisEllipse || g = Geometry.Axis4PEllipse || g = Geometry.Ellipse

    /// `p<parent label>_<n>`; the first 8 characters of the key when the parent has no label.
    let name (parent : Annotation) (index : int) =
        let label =
            if String.IsNullOrWhiteSpace parent.text then parent.key.ToString().Substring(0, 8)
            else parent.text.Trim()
        sprintf "p%s_%d" label index

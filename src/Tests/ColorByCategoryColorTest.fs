namespace ColorByCategoryColor

open System

open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

open Expecto

open PRo3D.Base
open PRo3D.Base.Annotation

/// Cyclic attributes are colored on a hue wheel whose period depends on whether the quantity
/// is directional or axial — see `ColorByCategory.cyclicPeriod`.
module Tests =

    /// ramp settings are irrelevant for the cyclic attributes, but `colorOfValue` needs a
    /// complete record; the bounds double as the numeric case's inputs
    let private settings (attr : ColorCategoryAttribute) : ColorByCategory.Settings =
        {
            attribute      = attr
            lower          = 0.0
            upper          = 100.0
            interval       = 5.0
            lowerColor     = C4b.Blue
            upperColor     = C4b.Red
            invert         = false
            categoryColors = HashMap.empty
            noValue        = C4b.Gray
            attributeKind   = ColorAttributeKind.AnnotationMeasurement
            surfaceColoring = SurfaceColoringMode.Annotation
            surfaceLayer    = ""
            surfaceSamples  = SurfaceSampleStore.empty
        }

    /// settings wired for `SurfaceAttribute` mode with a matching sample store
    let private surfaceSettings (layer : string) (coloring : SurfaceColoringMode)
                                (entries : list<Guid * float[]>) : ColorByCategory.Settings =
        let mean (vs : float[]) =
            let finite = vs |> Array.filter (fun v -> not (Double.IsNaN v))
            if finite.Length = 0 then nan else Array.average finite
        { settings ColorCategoryAttribute.AnnotationType with
            attributeKind   = ColorAttributeKind.SurfaceAttribute
            surfaceColoring = coloring
            surfaceLayer    = layer
            surfaceSamples  =
                { layer = layer
                  stamp = 0
                  entries =
                    entries
                    |> List.map (fun (k, vs) -> k, ({ values = vs; mean = mean vs } : SurfaceSampleEntry))
                    |> HashMap.ofList } }

    let private colorOf (attr : ColorCategoryAttribute) (degrees : float) =
        ColorByCategory.colorOfValue (settings attr) degrees

    /// An annotation of `geometry` carrying dip and strike results. `getFinishedAnnotation`
    /// fits a plane to every geometry, so this is the state a plain polyline really ends up
    /// in — not a contrived one.
    let private withDns (geometry : Geometry) =
        let a =
            Annotation.make
                Projection.Linear None geometry None
                ({ c = C4b.White } : ColorInput) Annotation.Initial.thickness "surface"
        { a with
            dnsResults =
                Some { DipAndStrikeResults.initial with
                        dipAngle      = 30.0
                        dipAzimuth    = 120.0
                        strikeAzimuth = 30.0 } }

    let private dnsAttributes =
        [ ColorCategoryAttribute.DipAngle
          ColorCategoryAttribute.DipAzimuth
          ColorCategoryAttribute.StrikeAzimuth ]

    /// same as `colorOf`, with an explicit sector width
    let private colorOfAt (interval : float) (attr : ColorCategoryAttribute) (degrees : float) =
        ColorByCategory.colorOfValue { settings attr with interval = interval } degrees

    let tests () =
        testList "ColorByCategory cyclic coloring" [

            // a strike line and a polyline's chord have no preferred end, so opposite
            // readings of the same orientation have to come out the same color
            test "axial attributes wrap at 180 degrees" {
                for attr in [ ColorCategoryAttribute.Bearing; ColorCategoryAttribute.StrikeAzimuth ] do
                    Expect.equal
                        (colorOf attr 10.0) (colorOf attr 190.0)
                        (sprintf "%A: 10 deg and 190 deg describe the same orientation" attr)
                    Expect.equal
                        (colorOf attr 0.0) (colorOf attr 180.0)
                        (sprintf "%A: 0 deg and 180 deg describe the same orientation" attr)
            }

            // a dip azimuth says which way the plane dips, so the two are genuinely opposite
            test "dip azimuth keeps the full circle" {
                Expect.notEqual
                    (colorOf ColorCategoryAttribute.DipAzimuth 10.0)
                    (colorOf ColorCategoryAttribute.DipAzimuth 190.0)
                    "opposite dip directions must not share a color"

                Expect.equal
                    (colorOf ColorCategoryAttribute.DipAzimuth 0.0)
                    (colorOf ColorCategoryAttribute.DipAzimuth 360.0)
                    "0 deg and 360 deg are the same direction"
            }

            test "negative angles normalise into the period" {
                Expect.equal
                    (colorOf ColorCategoryAttribute.Bearing -10.0)
                    (colorOf ColorCategoryAttribute.Bearing 170.0)
                    "-10 deg should fold to 170 deg"

                Expect.equal
                    (colorOf ColorCategoryAttribute.DipAzimuth -10.0)
                    (colorOf ColorCategoryAttribute.DipAzimuth 350.0)
                    "-10 deg should fold to 350 deg"
            }

            test "attributes without a value fall back to the no-value color" {
                for attr in [ ColorCategoryAttribute.Bearing
                              ColorCategoryAttribute.DipAzimuth
                              ColorCategoryAttribute.StrikeAzimuth
                              ColorCategoryAttribute.Slope ] do
                    Expect.equal
                        (colorOf attr Double.NaN) C4b.Gray
                        (sprintf "%A: NaN must use the no-value color" attr)
                    Expect.equal
                        (colorOf attr Double.PositiveInfinity) C4b.Gray
                        (sprintf "%A: infinity must use the no-value color" attr)
            }

            test "only azimuths are cyclic" {
                Expect.equal (ColorByCategory.cyclicPeriod ColorCategoryAttribute.DipAzimuth) (Some 360.0)
                    "dip azimuth is directional"
                Expect.equal (ColorByCategory.cyclicPeriod ColorCategoryAttribute.Bearing) (Some 180.0)
                    "bearing is axial"
                Expect.equal (ColorByCategory.cyclicPeriod ColorCategoryAttribute.StrikeAzimuth) (Some 180.0)
                    "strike azimuth is axial"

                // bounded inclinations, not wrapping quantities
                Expect.isNone (ColorByCategory.cyclicPeriod ColorCategoryAttribute.Slope)
                    "slope is an inclination, not an azimuth"
                Expect.isNone (ColorByCategory.cyclicPeriod ColorCategoryAttribute.DipAngle)
                    "dip angle is an inclination, not an azimuth"
                Expect.isNone (ColorByCategory.cyclicPeriod ColorCategoryAttribute.SurfaceName)
                    "categorical attributes are not cyclic"
            }

            // A plane is fitted to every geometry on finish, so `dnsResults` being present
            // does not mean the annotation is a dip and strike measurement. Reading it
            // regardless colored polylines and polygons, and the coloring then changed by
            // itself the first time the reference system was edited, because that path drops
            // the results of everything that is not DnS or TT.
            test "dip and strike attributes ignore geometries that only carry a fitted plane" {
                for attr in dnsAttributes do
                    for geometry in [ Geometry.Polyline; Geometry.Polygon; Geometry.Line
                                      Geometry.Point; Geometry.AxisEllipse ] do
                        Expect.isTrue
                            (Double.IsNaN (ColorByCategory.valueOf attr (withDns geometry)))
                            (sprintf "%A on a %A must have no value" attr geometry)
            }

            test "dip and strike attributes read DnS and TT annotations" {
                for geometry in [ Geometry.DnS; Geometry.TT ] do
                    let a = withDns geometry
                    Expect.equal (ColorByCategory.valueOf ColorCategoryAttribute.DipAngle a) 30.0
                        (sprintf "%A should report its dip angle" geometry)
                    Expect.equal (ColorByCategory.valueOf ColorCategoryAttribute.DipAzimuth a) 120.0
                        (sprintf "%A should report its dip azimuth" geometry)
                    Expect.equal (ColorByCategory.valueOf ColorCategoryAttribute.StrikeAzimuth a) 30.0
                        (sprintf "%A should report its strike azimuth" geometry)
            }

            // the wheel is a classified scale like the numeric bar, not a continuous blend
            test "angles within one sector share a color" {
                // 30 deg sectors over the full circle
                Expect.equal
                    (colorOfAt 30.0 ColorCategoryAttribute.DipAzimuth 10.0)
                    (colorOfAt 30.0 ColorCategoryAttribute.DipAzimuth 29.0)
                    "10 and 29 deg are both in the first sector"
                Expect.notEqual
                    (colorOfAt 30.0 ColorCategoryAttribute.DipAzimuth 29.0)
                    (colorOfAt 30.0 ColorCategoryAttribute.DipAzimuth 31.0)
                    "29 and 31 deg straddle a sector boundary"

                // and over the half circle the axial attributes use
                Expect.equal
                    (colorOfAt 15.0 ColorCategoryAttribute.Bearing 100.0)
                    (colorOfAt 15.0 ColorCategoryAttribute.Bearing 104.0)
                    "100 and 104 deg are both in the same sector"
            }

            test "the sector count is snapped so the wheel closes at zero" {
                // 360/50 is 7.2, which would leave a partial sector straddling the wrap
                Expect.equal (ColorByCategory.cyclicSectors 360.0 50.0) 7
                    "an interval that does not divide the period is rounded to whole sectors"
                Expect.equal (ColorByCategory.cyclicSectors 180.0 15.0) 12
                    "an interval that does divide it is used as-is"

                // the snap must not collapse the first and last sector onto one hue
                Expect.notEqual
                    (colorOfAt 50.0 ColorCategoryAttribute.DipAzimuth 0.0)
                    (colorOfAt 50.0 ColorCategoryAttribute.DipAzimuth 359.0)
                    "the last sector must stay distinct from the first"
            }

            test "a degenerate interval leaves one sector rather than dividing by zero" {
                for interval in [ 0.0; -5.0; Double.NaN; Double.PositiveInfinity ] do
                    Expect.equal (ColorByCategory.cyclicSectors 360.0 interval) 1
                        (sprintf "interval %f should fall back to a single sector" interval)

                // one sector means one flat color across the whole wheel, not a crash
                Expect.equal
                    (colorOfAt 0.0 ColorCategoryAttribute.DipAzimuth 10.0)
                    (colorOfAt 0.0 ColorCategoryAttribute.DipAzimuth 200.0)
                    "a single sector colors every angle the same"
            }

            // ---- surface attribute coloring ----

            test "surface mean color maps a finite mean onto the ramp, misses fall back" {
                let k = Guid.NewGuid()
                let s = surfaceSettings "elevation" SurfaceColoringMode.Annotation [ k, [| 0.0; 100.0 |] ]
                Expect.notEqual (ColorByCategory.surfaceMeanColor s k) s.noValue
                    "a finite mean must produce a ramp color"
                Expect.equal (ColorByCategory.surfaceMeanColor s (Guid.NewGuid())) s.noValue
                    "an annotation with no sampled entry is no-value"
                let allNaN = surfaceSettings "elevation" SurfaceColoringMode.Annotation [ k, [| nan; nan |] ]
                Expect.equal (ColorByCategory.surfaceMeanColor allNaN k) allNaN.noValue
                    "all-NaN samples mean the whole annotation is no-value"
            }

            test "surface coloring ignores a store left over from another layer" {
                let k = Guid.NewGuid()
                let s = surfaceSettings "elevation" SurfaceColoringMode.Annotation [ k, [| 10.0 |] ]
                let stale = { s with surfaceLayer = "roughness" }   // store still tagged "elevation"
                Expect.equal (ColorByCategory.surfaceMeanColor stale k) stale.noValue
                    "a layer switch invalidates the cached samples"
                Expect.equal (ColorByCategory.surfacePointColors stale k 1) [| stale.noValue |]
                    "same for the per-point colors"
            }

            test "surface point colors: one per point; NaN and count mismatch are no-value" {
                let k = Guid.NewGuid()
                let s = surfaceSettings "elevation" SurfaceColoringMode.Pointwise [ k, [| 0.0; nan; 100.0 |] ]
                let cols = ColorByCategory.surfacePointColors s k 3
                Expect.equal cols.Length 3 "one color per control point"
                Expect.equal cols.[1] s.noValue "a missed point is no-value"
                Expect.notEqual cols.[0] s.noValue "a hit point is a ramp color"
                Expect.equal (ColorByCategory.surfacePointColors s k 2) [| s.noValue; s.noValue |]
                    "a point count that no longer matches the samples is all no-value"
            }

            test "resample stamp is order independent and reacts to every input" {
                let a, b = Guid.NewGuid(), Guid.NewGuid()
                let planet = Planet.Mars
                let p1 = [ a, [| V3d(1.0, 2.0, 3.0) |]; b, [| V3d.Zero |] ]
                let p2 = [ b, [| V3d.Zero |]; a, [| V3d(1.0, 2.0, 3.0) |] ]
                Expect.equal (ColorByCategory.stampOf "l" planet p1) (ColorByCategory.stampOf "l" planet p2)
                    "annotation order must not change the stamp"
                Expect.notEqual (ColorByCategory.stampOf "l" planet p1)
                    (ColorByCategory.stampOf "l" planet [ a, [| V3d(1.0, 2.0, 3.5) |]; b, [| V3d.Zero |] ])
                    "a moved point changes the stamp"
                Expect.notEqual (ColorByCategory.stampOf "l" planet p1)
                    (ColorByCategory.stampOf "l" planet [ a, [| V3d(1.0, 2.0, 3.0); V3d.Zero |]; b, [| V3d.Zero |] ])
                    "an added point changes the stamp"
                Expect.notEqual (ColorByCategory.stampOf "l" planet p1)
                    (ColorByCategory.stampOf "l" Planet.Dimorphos p1)
                    "a reference-frame change (planet) changes the stamp - it changes the sampling direction at every point"
                Expect.notEqual (ColorByCategory.stampOf "l" planet p1) (ColorByCategory.stampOf "m" planet p1)
                    "a different layer changes the stamp"
            }
        ]

namespace ColorByCategoryColor

open System

open Aardvark.Base
open Aardvark.UI
open FSharp.Data.Adaptive

open Expecto

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
        }

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
        ]

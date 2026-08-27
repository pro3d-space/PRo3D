namespace ColorByCategoryColor

open System

open Aardvark.Base
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
        ]

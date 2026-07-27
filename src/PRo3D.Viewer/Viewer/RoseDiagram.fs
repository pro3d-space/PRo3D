namespace PRo3D.Viewer

open System
open Aardvark.UI
open Aardvark.UI.Operators

/// A small, self-contained rose diagram (16-sector polar histogram) for the Bulk Edit tab.
/// Given a list of azimuths in degrees (0 = north, clockwise), it renders an SVG rose whose
/// sector *area* encodes the count of measurements falling into that 22.5 deg bin (equal-area
/// scaling), plus an outer ring, a red circular-mean direction line and a sample-count label.
/// It is a pure function of the angle list, so it can be rebuilt live whenever the selection or
/// the type toggles change. Note: F# printf (%f) formats with invariant culture, so the SVG
/// path/coordinate strings are always '.'-decimal regardless of the machine locale.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RoseDiagram =

    let private binCount = 16
    let private binAngle = 360.0 / float binCount   // 22.5 deg
    let private binHalf  = binAngle / 2.0           // 11.25 deg
    let private radius   = 50.0                      // outer radius in svg units

    let private deg2rad (d : float) = d * Math.PI / 180.0

    /// screen point at compass azimuth (0 = north/up, clockwise) and radius r.
    /// svg y points down, so north (0 deg) maps to (0, -r).
    let private pointAt (r : float) (azimuthDeg : float) =
        let a = deg2rad azimuthDeg
        (r * sin a, -(r * cos a))

    let private norm360 (a : float) = ((a % 360.0) + 360.0) % 360.0

    let private binOf (az : float) =
        int (floor ((norm360 az + binHalf) / binAngle)) % binCount

    let view (angles : float list) : DomNode<'msg> =
        // bin the azimuths
        let counts = Array.zeroCreate binCount
        angles |> List.iter (fun az -> let b = binOf az in counts.[b] <- counts.[b] + 1)
        let total = List.length angles
        let maxCount = counts |> Array.fold max 1

        // one filled wedge per non-empty bin, radius by equal-area scaling (area proportional to count)
        let wedges =
            [ for i in 0 .. binCount - 1 do
                if counts.[i] > 0 then
                    let r = radius * sqrt (float counts.[i] / float maxCount)
                    let center = float i * binAngle
                    let (x0, y0) = pointAt r (center - binHalf)
                    let (x1, y1) = pointAt r (center + binHalf)
                    let d = sprintf "M 0 0 L %f %f A %f %f 0 0 1 %f %f Z" x0 y0 r r x1 y1
                    yield Svg.path [
                        "d"            => d
                        "fill"         => "#3b7dd8"
                        "fill-opacity" => "0.85"
                        "stroke"       => "#1c3f6e"
                        "stroke-width" => "0.6"
                    ] ]

        // outer bounding ring
        let ring =
            Svg.circle [
                "cx" => "0"; "cy" => "0"; "r" => sprintf "%f" radius
                "fill" => "none"; "stroke" => "#888"; "stroke-width" => "0.8"
            ]

        // red circular-mean direction line
        let meanLine =
            let sumSin = angles |> List.sumBy (fun a -> sin (deg2rad a))
            let sumCos = angles |> List.sumBy (fun a -> cos (deg2rad a))
            let meanDeg = (atan2 sumSin sumCos) * 180.0 / Math.PI
            let (mx, my) = pointAt radius meanDeg
            Svg.line [
                "x1" => "0"; "y1" => "0"; "x2" => sprintf "%f" mx; "y2" => sprintf "%f" my
                "stroke" => "#d0021b"; "stroke-width" => "1.4"
            ]

        // north indicator + sample count
        let northLabel =
            Svg.text [ "x" => "0"; "y" => sprintf "%f" (-(radius + 4.0)); "text-anchor" => "middle";
                       "font-size" => "9"; "fill" => "#cccccc" ] "N"
        let countLabel =
            Svg.text [ "x" => "0"; "y" => sprintf "%f" (radius + 13.0); "text-anchor" => "middle";
                       "font-size" => "9"; "fill" => "#ffffff" ] (sprintf "n = %d" total)

        let children = wedges @ [ ring; meanLine; northLabel; countLabel ]

        let svgAttributes =
            [ "width"               => "160px"
              "height"              => "175px"
              "viewBox"             => "-62 -60 124 135"
              "preserveAspectRatio" => "xMidYMid meet"
              "style"               => "display:block; margin: 4px auto" ]

        Svg.svg svgAttributes children

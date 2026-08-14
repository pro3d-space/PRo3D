namespace PRo3D.Base.Geometry

open System
open System.IO
open Aardvark.Base
open PRo3D.Base.Geometry.RegionOps

/// Text serialisation for region fixtures, so an interesting case found by clicking in the
/// geometry lab can be dropped into src/Tests/data/regions and replayed by the suite.
///
/// Plain text rather than a serialiser: these files are read by humans reviewing *why* a case was
/// interesting, and they diff meaningfully in git. One contour per line, blank line between shapes.
///
/// Lives in PRo3D.Base rather than the lab so the writer (the lab) and the reader (the tests)
/// share one definition of the format.
module RegionFixture =

    [<Literal>]
    let Extension = ".region"

    let private formatContour (pts : V2d[]) =
        pts
        |> Array.map (fun p -> String.Format(Globalization.CultureInfo.InvariantCulture, "{0:0.000000},{1:0.000000}", p.X, p.Y))
        |> String.concat " "

    /// One block per shape; within a block the first line is the outer contour and any further
    /// lines are holes.
    let write (regions : List<Region>) (note : string) =
        let sb = Text.StringBuilder()
        if not (String.IsNullOrWhiteSpace note) then
            sb.AppendLine("# " + note) |> ignore
        for r in regions do
            for ring in outerRings r do
                sb.AppendLine(formatContour (ring |> Array.map (fun p -> V2d(p.X, p.Y)))) |> ignore
            for hole in holes r do
                sb.AppendLine("hole " + formatContour (hole |> Array.map (fun p -> V2d(p.X, p.Y)))) |> ignore
            sb.AppendLine() |> ignore
        sb.ToString()

    let private parseContour (line : string) =
        line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun tok ->
            match tok.Split(',') with
            | [| x; y |] ->
                match Double.TryParse(x, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture),
                      Double.TryParse(y, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | (true, x), (true, y) -> Some (V2d(x, y))
                | _ -> None
            | _ -> None)

    /// Outer contours only - a hole is reproduced by cutting it back out, which is what the
    /// operations under test are for, so fixtures record only what was drawn.
    let read (text : string) : List<Region> =
        text.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#") && not (l.StartsWith "hole"))
        |> Array.choose (fun l -> parseContour l |> ofRing2d)
        |> Array.toList

    let save (directory : string) (name : string) (regions : List<Region>) (note : string) =
        Directory.CreateDirectory directory |> ignore
        let path = Path.Combine(directory, name + Extension)
        File.WriteAllText(path, write regions note)
        path

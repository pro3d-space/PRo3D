namespace PRo3D.GeometryLab

open System
open System.IO
open Aardvark.Base
open PRo3D.Base.Geometry.RegionOps

/// Text serialisation for lab shapes, so an interesting case found by clicking can be dropped into
/// src/Tests/data/regions and replayed by the suite.
///
/// Plain text rather than a serialiser: these files are read by humans reviewing *why* a case was
/// interesting, and they diff meaningfully in git. One contour per line, blank line between shapes.
module Fixture =

    [<Literal>]
    let Extension = ".region"

    let private formatContour (pts : V2d[]) =
        pts
        |> Array.map (fun p -> sprintf "%.6f,%.6f" p.X p.Y)
        |> String.concat " "

    /// One block per shape; within a block the first line is the outer contour and any further
    /// lines are holes.
    let write (regions : Region list) (note : string) =
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
        |> Array.map (fun tok ->
            let parts = tok.Split(',')
            V2d(Double.Parse(parts.[0], Globalization.CultureInfo.InvariantCulture),
                Double.Parse(parts.[1], Globalization.CultureInfo.InvariantCulture)))

    /// Outer contours only - a hole is reproduced by cutting it back out, which is what the
    /// operations under test are for, so fixtures record only what was drawn.
    let read (text : string) : Region list =
        text.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#") && not (l.StartsWith "hole"))
        |> Array.choose (fun l -> parseContour l |> ofRing2d)
        |> Array.toList

    let save (directory : string) (name : string) (regions : Region list) (note : string) =
        Directory.CreateDirectory directory |> ignore
        let path = Path.Combine(directory, name + Extension)
        File.WriteAllText(path, write regions note)
        path

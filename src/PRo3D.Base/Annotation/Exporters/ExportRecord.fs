namespace PRo3D.Base.Annotation

open System
open System.Globalization
open System.Text

open Aardvark.Base
open Thoth.Json.Net

open PRo3D.Base

/// A single exportable value. Keeping the value domain explicit (instead of
/// stringifying at the source) lets the CSV writer stay culture-invariant and
/// lets the GeoJSON writer emit proper JSON types rather than quoted numbers.
type ExportValue =
    | VText  of string
    | VNum   of float
    | VInt   of int
    | VBool  of bool
    /// multi-channel value (e.g. an RGB texture sample); one CSV cell, a JSON array
    | VNums  of float[]
    | VMissing

/// Container-agnostic geometry. The CSV writer ignores it; the GeoJSON writer
/// turns it into the `geometry` member of a Feature.
type ExportGeometry =
    | GPoint of V3d
    | GLine  of list<V3d>
    /// closed ring (the writer appends the first coordinate again if needed)
    | GRing  of list<V3d>

/// One output record — one CSV row, or one GeoJSON Feature.
/// `fields` is ordered and, within a single export, always follows the schema
/// produced by `AnnotationExport.schemaOf`.
type ExportRecord = {
    fields   : list<string * ExportValue>
    geometry : Option<ExportGeometry>
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ExportValue =

    let ofOptionalFloat (v : Option<float>) =
        match v with
        | Some x -> VNum x
        | None   -> VMissing

    /// NaN is how the annotation results represent "not computed"; it must not
    /// leak into the output as the literal text "NaN".
    let ofFloat (v : float) =
        if Double.IsNaN v || Double.IsInfinity v then VMissing else VNum v

    /// Default (no format specifier) is the shortest round-trippable form on
    /// modern .NET; "G" caps at 15 significant digits, which silently loses
    /// sub-millimetre precision on planetary-scale coordinates.
    let private invariant (v : float) = v.ToString(CultureInfo.InvariantCulture)

    /// Plain textual form used by the CSV writer (before quoting).
    let toCsvString (v : ExportValue) =
        match v with
        | VText t   -> t
        | VNum n    -> invariant n
        | VInt i    -> i.ToString(CultureInfo.InvariantCulture)
        | VBool b   -> if b then "true" else "false"
        | VNums ns  -> ns |> Array.map invariant |> String.concat ";"
        | VMissing  -> ""

    let toJson (v : ExportValue) =
        match v with
        | VText t   -> Encode.string t
        | VNum n    -> Encode.float n
        | VInt i    -> Encode.int i
        | VBool b   -> Encode.bool b
        | VNums ns  -> Encode.list (ns |> Array.toList |> List.map Encode.float)
        | VMissing  -> Encode.nil

module ExportWriters =

    // ---------------------------------------------------------------- CSV ---

    /// RFC 4180: quote when the cell contains a separator, a quote or a newline;
    /// escape embedded quotes by doubling them.
    let private escapeCsv (separator : string) (s : string) =
        let needsQuoting =
            s.Contains separator || s.Contains "\"" || s.Contains "\n" || s.Contains "\r"
        if needsQuoting then "\"" + s.Replace("\"", "\"\"") + "\"" else s

    /// Writes `records` as CSV. Column order and presence come from `schema`,
    /// not from the records — a record missing a column yields an empty cell,
    /// so a heterogeneous set of annotations still produces a rectangular table.
    ///
    /// Always InvariantCulture: the previous reflective writer used the current
    /// culture, which corrupted the output on decimal-comma systems.
    let writeCsv (path : string) (schema : list<string>) (records : seq<ExportRecord>) : unit =
        let separator = ","
        let sb = StringBuilder()

        schema
        |> List.map (escapeCsv separator)
        |> String.concat separator
        |> sb.AppendLine
        |> ignore

        let mutable count = 0
        for record in records do
            let lookup = record.fields |> Map.ofList
            schema
            |> List.map (fun column ->
                lookup
                |> Map.tryFind column
                |> Option.map ExportValue.toCsvString
                |> Option.defaultValue ""
                |> escapeCsv separator)
            |> String.concat separator
            |> sb.AppendLine
            |> ignore
            count <- count + 1

        IO.File.WriteAllText(path, sb.ToString())
        Log.line "[AnnotationExport] wrote %d rows / %d columns to %s" count schema.Length path

    // ------------------------------------------------------------ GeoJSON ---

    /// GeoJSON position. Coordinates are emitted in the spec order
    /// [x, y, z] / [longitude, latitude, altitude] — note that the exporters
    /// this replaces emitted latitude first, which no spec-compliant reader
    /// (QGIS included) interprets correctly.
    let private position (p : V3d) =
        Encode.list [ Encode.float p.X; Encode.float p.Y; Encode.float p.Z ]

    let private geometry (g : ExportGeometry) =
        match g with
        | GPoint p ->
            Encode.object [
                "type", Encode.string "Point"
                "coordinates", position p
            ]
        | GLine points ->
            Encode.object [
                "type", Encode.string "LineString"
                "coordinates", Encode.list (points |> List.map position)
            ]
        | GRing points ->
            let closed =
                match points, List.tryLast points with
                | first :: _, Some last when first <> last -> points @ [ first ]
                | _ -> points
            Encode.object [
                "type", Encode.string "Polygon"
                "coordinates", Encode.list [ Encode.list (closed |> List.map position) ]
            ]

    let private feature (record : ExportRecord) =
        Encode.object [
            "type", Encode.string "Feature"
            "geometry",
                (match record.geometry with
                 | Some g -> geometry g
                 | None   -> Encode.nil)
            "properties",
                Encode.object (record.fields |> List.map (fun (k, v) -> k, ExportValue.toJson v))
        ]

    /// Writes a spec-shaped `FeatureCollection`. `planet` (when the export is
    /// geographic) is carried as a collection-level property, as the QGIS
    /// exporter did.
    let writeGeoJson (path : string) (planet : Option<string>) (records : seq<ExportRecord>) : unit =
        let features = records |> Seq.map feature |> Seq.toList

        let collection =
            Encode.object [
                yield "type", Encode.string "FeatureCollection"
                yield "features", Encode.list features
                match planet with
                | Some p -> yield "properties", Encode.object [ "planet", Encode.string p ]
                | None   -> ()
            ]

        collection.ToString() |> Serialization.writeToFile path
        Log.line "[AnnotationExport] wrote %d features to %s" features.Length path

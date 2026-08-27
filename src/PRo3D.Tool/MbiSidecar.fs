module PRo3D.Tool.MbiSidecar

open System
open System.Globalization
open System.IO
open System.Text.Json.Nodes

open Aardvark.Base
open Aardvark.Rendering

open PRo3D.SPICE
open PRo3D.Core
open PRo3D.ImageMapping

// An .mbi.json sidecar written from a camera this tool actually rendered with, so that the
// rendered PNG can be imported into the PRo3D viewer and projected straight back onto the
// same body. If the projection chain is right the image lands exactly on the terrain it was
// rendered from -- which is the point: it turns "does the projection match?" from an
// eyeball question into a closed loop.
//
// This is also the executable statement of the sidecar convention PRo3D reads, which the
// HERA COP synthetic delivery gets wrong in three independent ways (see
// docs/COP-sidecar-issues.md, issues 6-8):
//
//   SC_QUAT0..3  (w, x, y, z) of the quaternion whose rotation matrix takes vectors from
//                the SPACECRAFT frame to J2000. Equivalently: its transpose takes the
//                target direction onto the instrument's +Z. The COP delivery writes the
//                conjugate.
//   TRG_POSX/Y/Z target MINUS spacecraft, in km, J2000 axes -- a vector FROM the camera
//                TO the body being projected onto, not the camera's location, and centred
//                on that body rather than on the system primary.
//
// Everything here is derived from the render camera by inverting the viewer's own chain
// (InstrumentProjection.projectOntoQuat), and then checked against the viewer's own reader
// before the file is accepted -- see `verify`.

/// FITS INSTRUME name for a SPICE instrument frame ("HERA_AFC-1" -> "AFC1"), the inverse of
/// InstrumentProjection.instrumentNames. Written into the sidecar because that is the name
/// the reader maps back through `instrument2SpiceName`.
let private fitsInstrumentName (spiceFrame : string) : Option<string> =
    InstrumentProjection.instrumentNames
    |> Map.toSeq
    |> Seq.tryPick (fun (fits, spice) -> if spice = spiceFrame then Some fits else None)

/// The observation geometry a sidecar has to carry, in the units and frame the sidecar
/// declares them in.
type Observation =
    {
        /// target minus spacecraft, km, J2000 axes
        targetPos : V3d
        /// spacecraft -> J2000, (w, x, y, z)
        scQuat    : QuaternionD
    }

/// Invert the viewer's projector chain to recover what a sidecar would have to say for the
/// viewer to reproduce `view`.
///
/// The viewer composes  toJ2000 * viewTrafo(quat, position) * specialTrafo * projTrafo
/// (InstrumentProjection.projectOntoQuat). This render used `view * proj` in the body-fixed
/// frame with the same instrument frustum, so the middle factor is pinned:
///
///     toJ2000 * V * special = view    =>    V = toJ2000⁻¹ * view * special⁻¹
///
/// `getLookAtQuat` builds V's rotation as FromBasis(-C0, -C1, -C2) over the columns of the
/// CONJUGATE quaternion's matrix, i.e. exactly -Aᵀ for A = the quaternion's own rotation
/// matrix -- so A falls out as -(V's rotation)ᵀ, and the location is V's own.
///
/// `frame` is the body-fixed frame the render happened in, `instrument` the SPICE
/// instrument frame (for the mounting trafo).
let deriveObservation (frame : string) (instrument : string) (time : DateTime)
                      (view : Trafo3d) : Result<Observation, string> =
    match CooTransformation.getRotationTrafo frame "J2000" time with
    | None ->
        Result.Error (sprintf "no rotation from %s to J2000 at %s (kernel coverage?)" frame (time.ToString "o"))
    | Some toJ2000 ->
        match Map.tryFind instrument InstrumentProjection.specialTrafos with
        | None ->
            Result.Error (sprintf "no instrument mounting trafo known for '%s' (see InstrumentProjection.specialTrafos)" instrument)
        | Some special ->
            let v = toJ2000.Inverse * view * special.Inverse
            let r = v.Forward.UpperLeftM33()
            // A = -Rᵀ; build it column-wise from R's rows, which is the same thing
            let a = M33d.FromCols(-r.R0, -r.R1, -r.R2)
            let q : QuaternionD = Rot3d.op_Explicit (Rot3d.FromM33d(a, 1e-6))
            // the camera's own location, in J2000; the sidecar declares the opposite vector
            let location = v.Backward.TransformPos V3d.Zero
            if not (location.ToArray() |> Array.forall Double.IsFinite) then
                Result.Error "the recovered camera location is not finite"
            else
                Ok { targetPos = -location / 1000.0; scQuat = q }

// ---------------------------------------------------------------------------------------
// writing
// ---------------------------------------------------------------------------------------

let private header (value : JsonNode) (comment : string) =
    let o = JsonObject()
    o.["value"] <- value
    o.["comment"] <- JsonValue.Create comment
    o :> JsonNode

let private num (v : float) = JsonValue.Create v :> JsonNode
let private str (v : string) = JsonValue.Create v :> JsonNode

/// Everything the sidecar needs that does not come out of the camera.
type Context =
    {
        /// SPICE instrument frame, e.g. "HERA_AFC-1"
        instrument : string
        /// body the image was rendered of, written to TARGET and used as the projection target
        body       : string
        /// body-fixed frame the render happened in
        frame      : string
        time       : DateTime
        /// metakernel file the render used, named so a reader can reload the same one
        kernel     : string
        size       : V2i
    }

/// Sun and Earth position in km, J2000, relative to the target body.
///
/// SUN_POS is not decoration: the reader uses its magnitude to decide whether a sidecar's
/// positions are km or metres (the COP delivery mislabels metres as km), so writing a
/// plausible one is what keeps our own output on the km path.
let private ephemeris (ctx : Context) =
    let posOf (b : string) =
        CooTransformation.getRelState b "EARTH" ctx.body ctx.time "J2000"
        |> Option.map (fun st -> st.pos / 1000.0)
    let sun =
        match posOf "SUN" with
        | Some p when p.Length > 0.0 -> p
        | _ ->
            Log.warn "[mbi] no sun ephemeris for %s at %s -- writing a nominal 1 AU vector so the reader's unit detection still works"
                ctx.body (ctx.time.ToString "o")
            V3d(1.495978707e8, 0.0, 0.0)
    // EARTPOS is required by the reader but unused by the projection; the COP delivery
    // writes zeros and they parse fine, so a miss here is not worth failing over.
    let earth = posOf "EARTH" |> Option.defaultValue V3d.Zero
    sun, earth

/// Write `<image>.mbi.json` next to a rendered image, describing the camera it was rendered
/// with. `view` is the render's view trafo in the body-fixed frame.
///
/// Returns the sidecar path, or why the geometry could not be expressed.
let write (ctx : Context) (imagePath : string) (view : Trafo3d) : Result<string, string> =
    match fitsInstrumentName ctx.instrument with
    | None ->
        Result.Error (sprintf "no FITS INSTRUME name known for the SPICE frame '%s' -- the viewer could not map it back" ctx.instrument)
    | Some instrume ->
        match deriveObservation ctx.frame ctx.instrument ctx.time view with
        | Result.Error e -> Result.Error e
        | Ok obs ->
            let sun, earth = ephemeris ctx
            let imageName = Path.GetFileName imagePath

            let h = JsonObject()
            h.["SIMPLE"]   <- header (JsonValue.Create true) "conforms to mbi standard"
            h.["BITPIX"]   <- header (JsonValue.Create 8) ""
            h.["NAXIS"]    <- header (JsonValue.Create 2) ""
            h.["NAXIS1"]   <- header (JsonValue.Create ctx.size.X) ""
            h.["NAXIS2"]   <- header (JsonValue.Create ctx.size.Y) ""
            h.["INSTRUME"] <- header (str instrume) ""
            h.["ORIGIN"]   <- header (str "pro3d-tool simulate-image") "simulated instrument image"
            h.["FILENAME"] <- header (str imageName) ""
            // ISO-8601 with a Z: parseDate reads it with AssumeUniversal, so an explicit
            // zone is what keeps the epoch from drifting by the local offset
            h.["DATE"]     <- header (str (DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))) "File creation time UTC"
            h.["DATE-OBS"] <- header (str (ctx.time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))) "Observation time UTC"
            h.["TARGET"]   <- header (str ctx.body) "Observation target"
            h.["SPICE_MK"] <- header (str (Path.GetFileNameWithoutExtension ctx.kernel)) "SPICE metakernel"
            h.["SUN_POSX"] <- header (num sun.X) "Sun position vector X [km]"
            h.["SUN_POSY"] <- header (num sun.Y) "Sun position vector Y [km]"
            h.["SUN_POSZ"] <- header (num sun.Z) "Sun position vector Z [km]"
            h.["EARTPOSX"] <- header (num earth.X) "Earth position vector X [km]"
            h.["EARTPOSY"] <- header (num earth.Y) "Earth position vector Y [km]"
            h.["EARTPOSZ"] <- header (num earth.Z) "Earth position vector Z [km]"
            h.["TRG_POSX"] <- header (num obs.targetPos.X) "Target position vector X [km] (target minus spacecraft)"
            h.["TRG_POSY"] <- header (num obs.targetPos.Y) "Target position vector Y [km] (target minus spacecraft)"
            h.["TRG_POSZ"] <- header (num obs.targetPos.Z) "Target position vector Z [km] (target minus spacecraft)"
            h.["TRG_DIST"] <- header (num obs.targetPos.Length) "Target distance [km]"
            h.["SC_QUAT0"] <- header (num obs.scQuat.W)   "Spacecraft quaternion w (spacecraft -> J2000)"
            h.["SC_QUAT1"] <- header (num obs.scQuat.V.X) "Spacecraft quaternion x (spacecraft -> J2000)"
            h.["SC_QUAT2"] <- header (num obs.scQuat.V.Y) "Spacecraft quaternion y (spacecraft -> J2000)"
            h.["SC_QUAT3"] <- header (num obs.scQuat.V.Z) "Spacecraft quaternion z (spacecraft -> J2000)"

            let headers = JsonArray()
            headers.Add h

            // The reader indexes a directory's sidecars by the band file names they declare
            // and only falls back to the <image base>.mbi.json naming convention when a
            // sidecar declares none -- so declare it, and use the conventional name too.
            let band = JsonObject()
            band.["file_path"] <- str imageName
            let bands = JsonArray()
            bands.Add band

            let root = JsonObject()
            root.["instrument"] <- str instrume
            root.["fits_hdu_headers"] <- headers
            root.["bands"] <- bands

            let path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath imagePath),
                                    Path.GetFileNameWithoutExtension imagePath + ".mbi.json")
            try
                File.WriteAllText(path, root.ToJsonString(Text.Json.JsonSerializerOptions(WriteIndented = true)))
                Ok path
            with e ->
                Result.Error (sprintf "could not write %s: %s" path e.Message)

/// The statistics sidecar (`<image>.json`). Optional for the projection itself, but it is
/// the only place the image's pixel dimensions are declared in a form the readers use, so
/// without it `unproject` cannot turn a pixel into a ray.
///
/// The statistics describe the values a CONSUMER sees, not the bytes on disk: an 8-bit
/// image is sampled as normalized [0,1] by the projection shader, and the viewer seeds an
/// image's display range straight from `image_statistics` — declaring 0..255 there would
/// stretch the colour map over a range a hundred times wider than the data and paint the
/// projection as one flat saturated blob. (Without this sidecar the viewer already treats
/// plain images as float over [0,1]; writing it must not change that.)
let writeStatistics (ctx : Context) (imagePath : string) (image : PixImage<byte>) : Result<string, string> =
    let channel = image.GetChannel Col.Channel.Gray
    let mutable mn = Double.MaxValue
    let mutable mx = Double.MinValue
    let mutable sum = 0.0
    for y in 0 .. image.Size.Y - 1 do
        for x in 0 .. image.Size.X - 1 do
            let v = float channel.[x, y] / 255.0
            if v < mn then mn <- v
            if v > mx then mx <- v
            sum <- sum + v
    let n = float (image.Size.X * image.Size.Y)

    let stats = JsonObject()
    stats.["minimum"] <- num mn
    stats.["maximum"] <- num mx
    stats.["mean"] <- num (sum / n)
    stats.["median"] <- num (sum / n)
    stats.["standard_deviation"] <- num 0.0
    stats.["variance"] <- num 0.0
    let statsArray = JsonArray()
    statsArray.Add stats

    let info = JsonObject()
    info.["schema_id"] <- str "https://www.joanneum.at/jim/product_information.schema.json"
    info.["schema_version"] <- JsonValue.Create 7262
    info.["product_type"] <- str "simulated image"
    info.["product_state"] <- str "generated"
    info.["creator_id"] <- str "pro3d-tool simulate-image"
    info.["creation_datetime"] <- str (DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))

    let root = JsonObject()
    root.["product_information"] <- info
    root.["image_width"] <- JsonValue.Create image.Size.X
    root.["image_height"] <- JsonValue.Create image.Size.Y
    root.["channels"] <- JsonValue.Create 1
    // "float" for the same reason the statistics are normalized: it names how the value
    // is consumed, and it is what the viewer assumes for a plain image without a sidecar
    root.["data_type"] <- str "float"
    root.["file_md5"] <- str ""
    root.["image_statistics"] <- statsArray
    root.["mission_name"] <- str "HERA"
    root.["camera_system"] <- str (fitsInstrumentName ctx.instrument |> Option.defaultValue ctx.instrument)

    let path = Path.GetFullPath imagePath + ".json"
    try
        File.WriteAllText(path, root.ToJsonString(Text.Json.JsonSerializerOptions(WriteIndented = true)))
        Ok path
    with e ->
        Result.Error (sprintf "could not write %s: %s" path e.Message)

// ---------------------------------------------------------------------------------------
// verification
// ---------------------------------------------------------------------------------------

/// How far the viewer's own reader lands from the camera the image was rendered with.
type Residual =
    {
        /// largest absolute element difference between the two world->clip matrices
        matrix : float
        /// angle between the two boresights, degrees
        boresight : float
        /// worst reprojection error of the frustum's corner directions, in pixels
        pixels : float
    }

/// Read the sidecar back through the viewer's own path and measure how far the projector it
/// produces is from the camera the image was actually rendered with.
///
/// This is what makes the sidecar trustworthy rather than merely plausible: it is the same
/// `Visualization.projectDirect` the viewer calls, on the file as written, so a convention
/// slip shows up here instead of as a puzzling offset on screen.
let verify (ctx : Context) (imagePath : string) (rendered : Trafo3d) (observer : string) : Result<Residual, string> =
    let metadata = InstrumentMetadata.tryParseMetadataForImagePath imagePath
    match metadata with
    | None, _ -> Result.Error "the sidecar we just wrote does not parse as mbi metadata"
    | Some _, _ ->

    match PRo3D.InstrumentProjection.Visualization.projectDirect
              observer ctx.frame metadata ctx.body None ProjectionMethod.MbiBased with
    | None -> Result.Error "the viewer's projectDirect returned nothing for the sidecar we just wrote"
    | Some (readBack : Trafo3d) ->

    let a = rendered.Forward.ToArray()
    let b = readBack.Forward.ToArray()
    let matrix = Array.map2 (fun (x : float) y -> abs (x - y)) a b |> Array.max

    // Compare where the two projectors actually look rather than trusting the matrices: an
    // overall scale on a homogeneous matrix is invisible on screen but huge element-wise.
    let dirOf (t : Trafo3d) (ndc : V2d) =
        let near = t.Backward.TransformPosProj(V3d(ndc.X, ndc.Y, -1.0))
        let far = t.Backward.TransformPosProj(V3d(ndc.X, ndc.Y, 1.0))
        (far - near) |> Vec.normalize

    let angleDeg (u : V3d) (v : V3d) =
        acos (clamp -1.0 1.0 (Vec.dot u v)) * Constant.DegreesPerRadian

    let boresight = angleDeg (dirOf rendered V2d.Zero) (dirOf readBack V2d.Zero)

    // corner directions carry roll and fov; express the miss in pixels so it is comparable
    // with what a user would see
    let corners = [ V2d(-1.0, -1.0); V2d(1.0, -1.0); V2d(-1.0, 1.0); V2d(1.0, 1.0) ]
    let pixelsPerDegree = float ctx.size.Y / (2.0 * atan (1.0 / rendered.Forward.M11) * Constant.DegreesPerRadian)
    let pixels =
        corners
        |> List.map (fun c -> angleDeg (dirOf rendered c) (dirOf readBack c) * pixelsPerDegree)
        |> List.max

    Ok { matrix = matrix; boresight = boresight; pixels = pixels }

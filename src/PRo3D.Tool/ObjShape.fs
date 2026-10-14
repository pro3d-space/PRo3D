module PRo3D.Tool.ObjShape

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.IO.Compression

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.SceneGraph
open FSharp.Data.Adaptive

open PRo3D.Core   // Sg.ProjectedImages

// Rendering a body from a Wavefront OBJ instead of an OPC.
//
// Why this exists at all: AFC is 1020x1020 at 93.7 urad/px, so at 5 km a detector pixel
// covers 0.48 m while the posts of the Dimorphos OPC are 1.96 m apart. One post spans
// about 4x4 pixels -- frames rendered from that OPC are SHAPE-limited, not sensor-limited,
// and do not resolve what AFC would actually see. The shape model the SPICE kernels ship
// (g_00243mm_spc_obj_dimo_..., 0.24 m facets) is twice as fine as a pixel.
//
// The mesh path is simpler than the OPC path, not harder: positions are already
// body-fixed, there is no hierarchy, no LOD tree and no patch-local frame, so ModelTrafo
// stays identity and a render is complete in its first frame. `stableTrafo`'s
// double-precision MVP is composed on the CPU either way, and at 180 m float32 vertices
// resolve ~10 microns, far below anything here.
//
// What the mesh does NOT bring is a texture. The `.png` shipped beside each `.bds` in the
// kernel set is a preview render, not a map -- kernels/dsk/aareadme.txt calls it "an image
// example in png format for convenience" -- and the OBJ carries no `vt`. So an untextured
// OBJ serves the constant-albedo variants only, and the verbs refuse the textured ones by
// name rather than falling back and emitting a `delit` frame with no texture in it.

/// A triangle mesh in the body-fixed frame, metres.
type Mesh =
    {
        /// vertex positions, already multiplied by the caller's scale
        positions : V3f[]
        /// texture coordinates with the V axis flipped into the renderer's convention
        /// (OBJ counts V up from the bottom); empty when the file carries none
        texCoords : V2f[]
        /// triangle list
        index : int[]
        /// body-fixed bounds in metres
        bbox : Box3d
        /// the extent the FILE declared, before scaling -- so a wrong `--obj-scale` is
        /// visible in the log rather than surfacing later as a body that renders empty
        sourceExtent : V3d
        /// 1.0 when the faces wind inward and the shader must flip the generated normal;
        /// the mesh equivalent of NormalWinding.estimate for OPC patches
        normalFlip : float
        source : string
    }

// ---------------------------------------------------------------------------------
// Parsing.
//
// The delivered Dimorphos file is 175 MB of ASCII with 1.58 M `v` and 3.15 M `f` lines and
// nothing else, so the loop below is essentially the whole cost of the OBJ path. It reads
// spans out of the line rather than splitting it, which keeps the allocation to one string
// per line instead of five.
//
// `.obj.gz` is read transparently: 175 MB of 15-digit decimals compresses to 39 MB, which
// is the difference between a file a repository can hold and one GitHub refuses outright.

let private openText (path : string) : TextReader =
    let fs = File.OpenRead path :> Stream
    let s =
        if path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
        then new GZipStream(fs, CompressionMode.Decompress) :> Stream
        else fs
    new StreamReader(s, Text.Encoding.ASCII, false, 1 <<< 20) :> TextReader

let inline private isSpace (c : char) = c = ' ' || c = '\t' || c = '\r'

/// Start of the next token at or after `i`, or the length of the line.
let inline private skipSpace (s : string) (i : int) =
    let mutable a = i
    while a < s.Length && isSpace s.[a] do a <- a + 1
    a

/// End of the token starting at `a` (exclusive).
let inline private tokenEnd (s : string) (a : int) =
    let mutable b = a
    while b < s.Length && not (isSpace s.[b]) do b <- b + 1
    b

/// NOT inline, and deliberately so: a `ReadOnlySpan` is a byref-like type, which F# will
/// not let live inside the `try` block the read loop runs in. Keeping the span behind a
/// real call boundary is what allows the allocation-free parse there.
let private parseFloat (s : string) (a : int) (b : int) =
    Double.Parse(s.AsSpan(a, b - a), NumberStyles.Float, CultureInfo.InvariantCulture)

/// Decimal integer in s.[a..b-1], or ValueNone when it is not one. `.` and `e` are
/// rejected rather than truncated: a float where an index belongs is a corrupt file, not
/// a rounding opportunity.
let inline private parseIndex (s : string) (a : int) (b : int) =
    let mutable i = a
    let mutable sign = 1
    if i < b && (s.[i] = '-' || s.[i] = '+') then
        if s.[i] = '-' then sign <- -1
        i <- i + 1
    let mutable v = 0
    let mutable ok = i < b
    while ok && i < b do
        let c = s.[i]
        if c >= '0' && c <= '9' then
            v <- v * 10 + (int c - int '0')
            i <- i + 1
        else ok <- false
    if ok then ValueSome (sign * v) else ValueNone

/// OBJ indices are 1-based, and a negative one counts back from however many have been
/// defined so far. Returns a 0-based index, or ValueNone when it does not resolve.
let inline private resolveIndex (raw : int) (count : int) =
    let i = if raw > 0 then raw - 1 elif raw < 0 then count + raw else -1
    if i >= 0 && i < count then ValueSome i else ValueNone

/// Read a mesh. `scale` multiplies every coordinate: the shape models shipped with the
/// SPICE kernels are in kilometres, and everything downstream of here is in metres.
let read (path : string) (scale : float) : Result<Mesh, string> =
    if not (File.Exists path) then Result.Error (sprintf "OBJ not found: %s" path)
    elif not (Double.IsFinite scale) || scale <= 0.0 then
        Result.Error (sprintf "--obj-scale must be positive and finite (got %g)" scale)
    else

    let sw = System.Diagnostics.Stopwatch.StartNew()
    Log.line "[obj] reading %s" path

    // vertices and texture coordinates as the file declares them
    let rawPos = ResizeArray<V3f>()
    let rawTc = ResizeArray<V2f>()
    // the triangle list; when the file indexes texture coordinates separately from
    // positions these are indices into `outPos`/`outTc` instead of into `rawPos`
    let index = ResizeArray<int>()
    let outPos = ResizeArray<V3f>()
    let outTc = ResizeArray<V2f>()
    let remap = Dictionary<int64, int>()

    // Bounds in the FILE's units, so the log can state what was read and what it became.
    let mutable lo = V3d(infinity, infinity, infinity)
    let mutable hi = V3d(-infinity, -infinity, -infinity)

    /// One face corner -> an index into the final vertex arrays. `useTc` is a parameter
    /// rather than a captured flag because F# closures cannot capture a mutable local --
    /// and it is decided once, at the first face (see the loop).
    let corner (useTc : bool) (line : string) (a : int) (b : int) : ValueOption<int> =
        // `v`, `v/vt`, `v//vn` or `v/vt/vn`
        let mutable slash1 = -1
        let mutable slash2 = -1
        let mutable i = a
        while i < b do
            if line.[i] = '/' then
                if slash1 < 0 then slash1 <- i elif slash2 < 0 then slash2 <- i
            i <- i + 1
        let pEnd = if slash1 < 0 then b else slash1
        match parseIndex line a pEnd |> ValueOption.bind (fun r -> resolveIndex r rawPos.Count) with
        | ValueNone -> ValueNone
        | ValueSome pi ->
            if not useTc then ValueSome pi
            else
                let ti =
                    if slash1 < 0 then ValueNone
                    else
                        let tEnd = if slash2 < 0 then b else slash2
                        if tEnd <= slash1 + 1 then ValueNone
                        else
                            parseIndex line (slash1 + 1) tEnd
                            |> ValueOption.bind (fun r -> resolveIndex r rawTc.Count)
                // A corner with no texture coordinate keeps a slot of its own rather than
                // borrowing another corner's, so a partly textured mesh does not smear one
                // UV across its untextured faces.
                let t = match ti with ValueSome t -> t | ValueNone -> Int32.MaxValue
                let key = (int64 pi <<< 32) ||| int64 t
                match remap.TryGetValue key with
                | true, k -> ValueSome k
                | _ ->
                    let k = outPos.Count
                    outPos.Add rawPos.[pi]
                    outTc.Add (match ti with ValueSome t -> rawTc.[t] | ValueNone -> V2f.Zero)
                    remap.[key] <- k
                    ValueSome k

    // Decided at the first face line. The OBJ format requires every `v`/`vt` a face refers
    // to to be defined before it, which is what makes one pass enough: by the time the
    // first face arrives it is already known whether the file has texture coordinates, and
    // therefore whether corners need de-duplicating at all.
    let mutable sawFace = false
    let mutable useTc = false
    let mutable badFaces = 0
    let mutable polygons = 0

    let parsed =
      try
        use reader = openText path
        let mutable line = reader.ReadLine()
        while not (isNull line) do
            let a0 = skipSpace line 0
            if a0 < line.Length && line.[a0] <> '#' then
                let k0 = line.[a0]
                let k1 = if a0 + 1 < line.Length then line.[a0 + 1] else ' '
                if k0 = 'v' && isSpace k1 then
                    let a = skipSpace line (a0 + 1)
                    let b = tokenEnd line a
                    let c = skipSpace line b
                    let d = tokenEnd line c
                    let e = skipSpace line d
                    let f = tokenEnd line e
                    if b > a && d > c && f > e then
                        let x = parseFloat line a b
                        let y = parseFloat line c d
                        let z = parseFloat line e f
                        lo <- V3d(min lo.X x, min lo.Y y, min lo.Z z)
                        hi <- V3d(max hi.X x, max hi.Y y, max hi.Z z)
                        rawPos.Add (V3f(float32 (x * scale), float32 (y * scale), float32 (z * scale)))
                elif k0 = 'v' && k1 = 't' then
                    let a = skipSpace line (a0 + 2)
                    let b = tokenEnd line a
                    let c = skipSpace line b
                    let d = tokenEnd line c
                    if b > a && d > c then
                        let u = parseFloat line a b
                        let v = parseFloat line c d
                        // OBJ counts V up from the bottom of the image, the sampler counts
                        // it down from the top. PRo3D's own OBJ loader flips it in the same
                        // place (SurfaceApp.patchUVConvention).
                        rawTc.Add (V2f(float32 u, float32 (1.0 - v)))
                elif k0 = 'f' && isSpace k1 then
                    if not sawFace then
                        sawFace <- true
                        useTc <- rawTc.Count > 0
                    // Fan-triangulate. The SPC/DSK models are pure triangle soup, but a
                    // quad-meshed shape model is still a legitimate shape model.
                    let mutable first = -1
                    let mutable prev = -1
                    let mutable n = 0
                    let mutable bad = false
                    let mutable i = skipSpace line (a0 + 1)
                    while i < line.Length do
                        let e = tokenEnd line i
                        if e > i then
                            match corner useTc line i e with
                            | ValueNone -> bad <- true
                            | ValueSome k ->
                                n <- n + 1
                                if first < 0 then first <- k
                                elif prev < 0 then prev <- k
                                else
                                    index.Add first
                                    index.Add prev
                                    index.Add k
                                    prev <- k
                        i <- skipSpace line e
                    if bad || n < 3 then badFaces <- badFaces + 1
                    if n > 3 then polygons <- polygons + 1
            line <- reader.ReadLine()
        Ok ()
      with e ->
        Result.Error (sprintf "cannot read %s: %s" path e.Message)

    match parsed with
    | Result.Error e -> Result.Error e
    | Ok () ->

    if rawPos.Count = 0 then Result.Error (sprintf "%s declares no vertices" path)
    elif index.Count < 3 then Result.Error (sprintf "%s declares no triangles" path)
    else

    let positions = if useTc then outPos.ToArray() else rawPos.ToArray()
    let texCoords = if useTc then outTc.ToArray() else [||]
    let idx = index.ToArray()
    let sourceExtent = hi - lo
    let bbox = Box3d(lo * scale, hi * scale)

    if badFaces > 0 then
        Log.warn "[obj] %d face(s) skipped: an index does not resolve" badFaces
    if polygons > 0 then
        Log.line "[obj] %d face(s) had more than three corners and were fan-triangulated" polygons

    // Winding vote, exactly as NormalWinding.estimate does it for an OPC patch and with the
    // same limitation: valid for a star-shaped body, which is what these shape models are.
    // `generateNormal` builds cross(p1-p0, p2-p0); wherever that points into the body the
    // shader has to flip it, or every lighting and projector-facing test runs inward.
    let flip =
        let triCount = idx.Length / 3
        let stride = max 1 (triCount / 2000)
        let centre = bbox.Center
        let mutable outward = 0
        let mutable inward = 0
        let mutable t = 0
        while t < triCount do
            let i = t * 3
            let p0 = V3d positions.[idx.[i]]
            let p1 = V3d positions.[idx.[i + 1]]
            let p2 = V3d positions.[idx.[i + 2]]
            let n = Vec.cross (p1 - p0) (p2 - p0)
            if Vec.dot n ((p0 + p1 + p2) / 3.0 - centre) > 0.0 then outward <- outward + 1
            else inward <- inward + 1
            t <- t + stride
        let flip = if inward > outward then 1.0 else 0.0
        Log.line "[obj] winding: %d outward / %d inward -> NormalFlip %.0f" outward inward flip
        flip

    sw.Stop()
    Log.line "[obj] %d vertices, %d triangles in %.1f s"
        positions.Length (idx.Length / 3) sw.Elapsed.TotalSeconds
    Log.line "[obj] extent %.4f x %.4f x %.4f (file units) x %g = %.1f x %.1f x %.1f m"
        sourceExtent.X sourceExtent.Y sourceExtent.Z scale
        bbox.Size.X bbox.Size.Y bbox.Size.Z
    if texCoords.Length > 0 then
        Log.line "[obj] %d texture coordinate(s)" texCoords.Length
    else
        Log.line "[obj] no texture coordinates -- the textured shading variants are not available from this file"

    // A shape model that comes out a thousand times too small renders a body a few pixels
    // across, and every downstream complaint then blames the pointing. The accepted range
    // is deliberately enormous: this is here to catch a unit mistake, not to have an
    // opinion about what may be rendered.
    let size = bbox.Size.Length
    if size < 1.0 || size > 1.0e7 then
        Log.warn "[obj] this body is %.3g m across after --obj-scale %g -- is the file in the units you think? \
                  (the shape models shipped with the SPICE kernels are in kilometres, i.e. --obj-scale 1000)"
            size scale

    Ok {
        positions = positions
        texCoords = texCoords
        index = idx
        bbox = bbox
        sourceExtent = sourceExtent
        normalFlip = flip
        source = path
    }

/// A triaxial ellipsoid as a mesh, in metres.
///
/// The shadow caster of a binary is a shape like any other, and a body with no shape model
/// to hand is still a body with RADII in the kernel pool. Tessellating those radii puts the
/// coarse case and the real case through exactly the same depth pass, instead of keeping an
/// analytic ellipsoid test in the shader that agrees with the mesh path until one of them
/// changes.
///
/// `steps` is the longitude count; latitude gets half. 64 gives 8 192 triangles and a limb
/// smooth to ~2 % of a radius, far finer than the metre the penumbra is measured in.
let ellipsoid (radii : V3d) (steps : int) : Mesh =
    let nu = max 8 steps
    let nv = max 4 (steps / 2)
    let positions =
        [| for j in 0 .. nv do
             let theta = float j / float nv * Constant.Pi
             for i in 0 .. nu do
                 let phi = float i / float nu * Constant.PiTimesTwo
                 yield V3f(float32 (radii.X * sin theta * cos phi),
                           float32 (radii.Y * sin theta * sin phi),
                           float32 (radii.Z * cos theta)) |]
    let index = ResizeArray<int>()
    let at i j = j * (nu + 1) + i
    for j in 0 .. nv - 1 do
        for i in 0 .. nu - 1 do
            let a, b, c, d = at i j, at (i + 1) j, at (i + 1) (j + 1), at i (j + 1)
            // outward, matching what generateNormal's cross product expects
            index.Add a; index.Add d; index.Add c
            index.Add a; index.Add c; index.Add b
    {
        positions = positions
        texCoords = [||]
        index = index.ToArray()
        bbox = Box3d(-radii, radii)
        sourceExtent = 2.0 * radii
        normalFlip = 0.0
        source = sprintf "ellipsoid %.1f x %.1f x %.1f m" radii.X radii.Y radii.Z
    }

// ---------------------------------------------------------------------------------
// The de-shading fit's inputs.
//
// The OPC path fits the baked light direction against a per-vertex `.aara` brightness
// layer. A mesh has no such layer, so the equivalent is the texture itself, sampled at
// each vertex's own UV and paired with the vertex normal. Same two quantities and the same
// fit (SimulateImageVerb.fitFromSamples) -- only the source of the brightness differs.

/// Area-weighted vertex normals, oriented outward by the mesh's own winding vote. Not
/// uploaded: the shader builds its own per-face normal (generateNormal), and this exists
/// only so the fit has a normal to go with each brightness sample.
let vertexNormals (m : Mesh) : V3f[] =
    let n = Array.zeroCreate<V3f> m.positions.Length
    let idx = m.index
    let mutable i = 0
    while i + 2 < idx.Length do
        let a = idx.[i]
        let b = idx.[i + 1]
        let c = idx.[i + 2]
        // not normalised: the cross product's length is twice the triangle's area, which
        // is exactly the weight an area-weighted vertex normal wants
        let f = Vec.cross (m.positions.[b] - m.positions.[a]) (m.positions.[c] - m.positions.[a])
        n.[a] <- n.[a] + f
        n.[b] <- n.[b] + f
        n.[c] <- n.[c] + f
        i <- i + 3
    let s = if m.normalFlip > 0.5 then -1.0f else 1.0f
    n |> Array.map (fun v -> if v.Length > 0.0f then (v * s).Normalized else V3f.Zero)

/// (unit normal in the body frame, brightness 0..1) per textured vertex.
///
/// Strided down the same way the OPC path strides its root patch: the fit solves a 4x4
/// system, and 30k samples already over-determine it by four orders of magnitude.
let deshadeSamples (m : Mesh) (texturePath : string) : Result<(V3d * float)[], string> =
    if m.texCoords.Length = 0 then
        Result.Error (sprintf "%s carries no texture coordinates, so there is nothing to fit a baked light against"
                          (Path.GetFileName m.source))
    elif not (File.Exists texturePath) then
        Result.Error (sprintf "texture not found: %s" texturePath)
    else
    try
        let tex = PixImage.Load(texturePath).ToPixImage<byte>(Col.Format.Gray)
        let grey = tex.GetChannel Col.Channel.Gray
        let w = tex.Size.X
        let h = tex.Size.Y
        if w < 2 || h < 2 then Result.Error (sprintf "%s is %dx%d -- not a texture" texturePath w h)
        else
        let normals = vertexNormals m
        let count = min normals.Length m.texCoords.Length
        let stride = max 1 (count / 30000)
        let samples = ResizeArray<V3d * float>()
        let mutable i = 0
        while i < count do
            let n = normals.[i]
            if n.LengthSquared > 0.5f then
                let tc = m.texCoords.[i]
                let x = clamp 0 (w - 1) (int (Fun.Round(float tc.X * float (w - 1))))
                let y = clamp 0 (h - 1) (int (Fun.Round(float tc.Y * float (h - 1))))
                samples.Add ((V3d n).Normalized, float grey.[x, y] / 255.0)
            i <- i + stride
        if samples.Count < 100 then
            Result.Error (sprintf "only %d usable vertices for the de-shading fit" samples.Count)
        else Ok (samples.ToArray())
    with e ->
        Result.Error (sprintf "cannot read %s: %s" texturePath e.Message)

// ---------------------------------------------------------------------------------
// The scene graph.

let private geometry (m : Mesh) =
    let attributes = SymbolDict<Array>()
    attributes.[DefaultSemantic.Positions] <- (m.positions :> Array)
    if m.texCoords.Length > 0 then
        attributes.[DefaultSemantic.DiffuseColorCoordinates] <- (m.texCoords :> Array)
    IndexedGeometry(
        Mode = IndexedGeometryMode.TriangleList,
        IndexArray = (m.index :> Array),
        IndexedAttributes = attributes)

/// The mesh, with everything the shared shader stack expects already bound.
///
/// The OPC path receives these from OpcSg.build and projectionUniformMap, PER PATCH --
/// which is why both verbs hand the sun and the projector down inside a
/// `Sg.ProjectedImages` rather than as outer uniforms. A mesh is one draw call with one
/// model transform, so the same two values are plain uniforms here, read out of the same
/// record so that neither verb needs a second way to say where the sun is.
///
/// ModelTrafo stays identity: the vertices are body-fixed metres already. That is also why
/// no `Sg.trafo` appears -- the shading shader reaches the body frame by transforming
/// LocalNormal and BodyLocalPos by ModelTrafo, and here that is a no-op.
let sg (m : Mesh) (texture : Option<string>)
       (projectedImages : aval<Option<Sg.ProjectedImages>>) : ISg =
    let sun =
        projectedImages
        |> AVal.bind (function Some p -> p.sunDirection | None -> AVal.constant None)
    let projector =
        projectedImages
        |> AVal.bind (function Some p -> p.imageProjection | None -> AVal.constant None)

    Sg.ofIndexedGeometry (geometry m)
    // Placeholders for the attributes the shared vertex records carry but this mesh does
    // not supply. They are single values rather than buffers, and a real attribute in the
    // geometry overrides them -- the same trick PRo3D's own OBJ loader uses.
    |> Sg.vertexBufferValue DefaultSemantic.Colors (AVal.constant V4f.One)
    |> Sg.vertexBufferValue DefaultSemantic.Normals (AVal.constant V4f.OOII)
    |> Sg.vertexBufferValue DefaultSemantic.DiffuseColorCoordinates (AVal.constant V4f.Zero)
    |> (match texture with
        | Some path -> Sg.fileTexture DefaultSemantic.DiffuseColorTexture path true
        | None -> Sg.texture DefaultSemantic.DiffuseColorTexture DefaultTextures.blackTex)
    |> Sg.uniform "SunDirectionWorld"
        (sun |> AVal.map (fun s -> V3f (s |> Option.defaultValue V3d.ZAxis)))
    |> Sg.uniform' "SunLightEnabled" true
    |> Sg.uniform "ProjectedImageModelViewProj"
        (projector |> AVal.map (fun t ->
            M44f (t |> Option.map (fun x -> x.Forward) |> Option.defaultValue M44d.Identity)))
    |> Sg.uniform' "NormalFlip" (float32 m.normalFlip)

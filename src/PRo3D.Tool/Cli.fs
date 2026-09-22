namespace PRo3D.Tool

open CommandLine

/// Options for the `kdtree` verb.
///
/// Carried over verbatim from the standalone `opc-tool`, so that a migrating user only
/// has to prepend the verb: `opc-tool --forcekdtreerebuild <dir>` becomes
/// `pro3d-tool kdtree --forcekdtreerebuild <dir>`.
[<Verb("kdtree", HelpText = "Validate OPC directories and generate KdTrees.")>]
type KdTreeOptions =
    {
        [<Option(HelpText = "Prints all messages to standard output.")>]
        verbose : bool

        [<Option(HelpText = "Forces rebuild and overwrites existing kd-trees")>]
        forcekdtreerebuild : bool

        [<Option(HelpText = "Ignores master kd-trees and load or creates per-patch kd-trees as well as the lazy kd-tree cache")>]
        ignoreMasterKdTree : bool

        [<Option(HelpText = "Generate DDS")>]
        generatedds : bool

        [<Option(HelpText = "Skip patch validation (textures, aara files)")>]
        skipPatchValidation : bool

        [<Option(HelpText = "Overwrite DDS")>]
        overwritedds : bool

        [<Option(HelpText = "Hierarchies to process concurrently: 1 is sequential, 0 (default) or -1 uses all available cores", Required = false)>]
        degreesOfParallelism : int

        [<Value(0, HelpText = "Surface Directory", Required = true)>]
        surfaceDirectory : string
    }

/// Options for the `sun-angles` verb.
///
/// Defaults for the string options are applied in code rather than through the attribute,
/// because an unsupplied string field arrives as null.
[<Verb("sun-angles", HelpText = "Render per-pixel illumination geometry (incidence, emission, phase) for instrument images.")>]
type SunAnglesOptions =
    {
        [<Option("opc", HelpText = "OPC directory of the body", Required = true)>]
        opc : string

        [<Option("images", HelpText = "Folder containing instrument images with .mbi.json sidecars", Required = true)>]
        images : string

        [<Option("image", HelpText = "Process only this image; default: every image in the folder")>]
        image : string

        [<Option("out", HelpText = "Output directory (default: ./sun-angles)")>]
        out : string

        [<Option("body", HelpText = "SPICE body name of the OPC (default DIDYMOS)")>]
        body : string

        [<Option("frame", HelpText = "Body-fixed reference frame (default DIDYMOS_FIXED)")>]
        frame : string

        [<Option("observer", HelpText = "Observing spacecraft (default MILANI)")>]
        observer : string

        [<Option("kernel", HelpText = "Explicit SPICE metakernel; overrides the sidecar")>]
        kernel : string

        [<Option("kernel-root", HelpText = "SPICE kernel tree: a clone of https://spiftp.esac.esa.int/git/hera.git or its 'kernels' dir. Defaults to $PRO3D_SPICE_KERNELS.")>]
        kernelRoot : string

        [<Option("method", HelpText = "Projection method: spice or mbi (default mbi)")>]
        method : string

        [<Option("false-color", HelpText = "Also write one false-colour PNG per angle (blue=low to red=high), using the same colour ramp as the projection testbed")>]
        falseColor : bool

        [<Option("width", HelpText = "Output width; 0 (default) uses the source image's native width")>]
        width : int

        [<Option("height", HelpText = "Output height; 0 (default) uses the source image's native height")>]
        height : int
    }

/// Options for the `simulate-image` verb.
///
/// Defaults for the string options are applied in code rather than through the attribute,
/// because an unsupplied string field arrives as null. Numeric defaults use the attribute.
[<Verb("simulate-image", HelpText = "Render a simulated instrument image of a body: OPC or OBJ geometry, Lommel-Seeliger sun lighting, procedural micro-structure, cast shadows, optional de-shaded texture albedo.")>]
type SimulateImageOptions =
    {
        [<Option("opc", HelpText = "OPC directory of the body. Exactly one of --opc and --obj.")>]
        opc : string

        [<Option("obj", HelpText = "Shape model as a Wavefront OBJ instead of --opc; `.obj.gz` is read directly. Use this to render from the shape model the SPICE kernels ship (kernels/dsk/*.obj): at 5 km an AFC pixel covers 0.48 m while the Dimorphos OPC's posts are 1.96 m apart, so OPC frames are shape-limited rather than sensor-limited. Exactly one of --opc and --obj.")>]
        obj : string

        [<Option("obj-scale", Default = 1000.0, HelpText = "Metres per --obj file unit (default 1000, i.e. the file is in kilometres -- which is what the SPICE DSK shape models are). The body's extent in metres is logged, so a wrong scale is visible immediately.")>]
        objScale : float

        [<Option("obj-texture", HelpText = "Image to drape on --obj, using the mesh's own texture coordinates. Required by --deshade / --texture-albedo / --texture-only, which are refused without it. NOTE the `.png` beside each `.bds` in the kernel set is a preview render, not a map.")>]
        objTexture : string

        [<Option("time", HelpText = "Observation time, ISO-8601 UTC (e.g. 2027-03-15T12:00:00Z). Required unless --mbi supplies the observation.")>]
        time : string

        [<Option("mbi", HelpText = "Render the camera an existing image's .mbi.json sidecar defines, instead of a SPICE look-at camera at --time. Takes the image file or the sidecar; the epoch, instrument and pointing all come from it, so the result is directly comparable with that image.")>]
        mbi : string

        [<Option("write-mbi", HelpText = "Also write <out>.mbi.json and <out>.json describing the camera used, so the render can be imported into the PRo3D viewer and projected back onto the same body")>]
        writeMbi : bool

        [<Option("out", HelpText = "Output PNG path (default: ./simulated.png)")>]
        out : string

        [<Option("instrument", HelpText = "SPICE instrument frame whose frustum to render with (default HERA_AFC-1)")>]
        instrument : string

        [<Option("observer", HelpText = "Spacecraft carrying the instrument (default HERA)")>]
        observer : string

        [<Option("body", HelpText = "SPICE body name of the OPC (default DIMORPHOS)")>]
        body : string

        [<Option("frame", HelpText = "Body-fixed reference frame (default DIMORPHOS_FIXED)")>]
        frame : string

        [<Option("kernel", HelpText = "Explicit SPICE metakernel; default: <kernel-root>/mk/hera_plan.tm")>]
        kernel : string

        [<Option("kernel-root", HelpText = "SPICE kernel tree: a clone of https://spiftp.esac.esa.int/git/hera.git or its 'kernels' dir. Defaults to $PRO3D_SPICE_KERNELS.")>]
        kernelRoot : string

        [<Option("distance", Default = 0.0, HelpText = "Camera distance in metres, along the direction SPICE puts the spacecraft; 0 (default) uses the spacecraft's real distance")>]
        distance : float

        [<Option("width", HelpText = "Output width; 0 (default) uses the instrument's native width")>]
        width : int

        [<Option("height", HelpText = "Output height; 0 (default) uses the instrument's native height")>]
        height : int

        [<Option("albedo", Default = 0.16, HelpText = "Normal reflectance of the surface (default 0.16, the measured Dimorphos value)")>]
        albedo : float

        [<Option("deshade", HelpText = "Fit and divide baked-in illumination out of the OPC texture and use it as albedo; default is the constant --albedo (the de-shading is approximate -- see the docs)")>]
        deshade : bool

        [<Option("deshade-layer", HelpText = "Per-vertex attribute layer carrying the texture brightness, for the de-shading fit (default DRACO)")>]
        deshadeLayer : string

        [<Option("micro-scale", Default = 0.5, HelpText = "Feature size of the procedural micro-structure in metres (default 0.5)")>]
        microScale : float

        [<Option("micro-amplitude", Default = 0.3, HelpText = "Strength of the procedural normal perturbation, 0 disables (default 0.3)")>]
        microAmplitude : float

        [<Option("ambient", Default = 0.02, HelpText = "Ambient floor so the night side is distinguishable from space (default 0.02)")>]
        ambient : float

        [<Option("gain", Default = 0.0, HelpText = "Linear I/F -> DN gain; 0 (default) auto-exposes the 99.5th percentile to DN 245")>]
        gain : float

        [<Option("no-shadows", HelpText = "Skip the sun shadow map; shading then comes from the local sun angle alone")>]
        noShadows : bool

        [<Option("no-lighting", HelpText = "Render a flat white disk instead of a shaded body: the image is then the silhouette, for comparing pointing and shape against a real frame without shading in the way")>]
        noLighting : bool

        [<Option("texture-layer", HelpText = "Which texture layer of the OPC to draw, by name ('DRACO_1') or index ('8'); the names come from the .opcx and are listed if the one given does not match. Default: the patch's own default layer, which is NOT necessarily what a PRo3D scene shows -- a scene stores its own selectedTexture, so the same OPC can render as 'Earth' here and 'DRACO_1' in the viewer. Match this to the scene's selectedTexture before comparing a render against a viewer screenshot.")>]
        textureLayer : string

        [<Option("texture-only", HelpText = "Render the OPC's own texture as this camera sees it: no lighting, and no de-shading fit. Unlike --deshade, which fits a light direction, clamps the result and falls back to a constant albedo where it has no confidence. Use --texture-layer to choose which layer.")>]
        textureOnly : bool

        [<Option("texture-albedo", HelpText = "Light the texture WITHOUT dividing its baked illumination out: the mosaic's own lighting stays in and this epoch's sun lights it a second time. Ignored with --deshade. The naive rendering, on purpose -- it is what a reconstruction is compared against to find out whether de-lighting was necessary at all.")>]
        textureAlbedo : bool

        [<Option("project", HelpText = "Project this image onto the body instead of shading it, through PRo3D's projection shader, and render the result. With no --mbi the camera is that image's own, so the output must reproduce the input image -- which is what makes the projection checkable rather than merely plausible.")>]
        project : string

        [<Option("project-shader", HelpText = "Which projection shader --project goes through: 'single' (default, stableImageProjection -- what sun-angles and the testbeds use) or 'stack' (stableImageProjectionStack, a one-layer stack -- what the viewer renders). Rendering the same image both ways isolates the stack path.")>]
        projectShader : string

        [<Option("shadow-bias", Default = 0.006, HelpText = "Shadow-map depth bias in normalized depth (default 0.006). Raise against acne, lower against peter-panning. The default was swept against a SPICE ray-cast of the same mesh (scripts/check-lighting.py): 0.002 wrongly darkened 2.6-6 % of the sun-facing body, mostly as isolated pixels, and 0.02 left a fifth of the real shadow lit.")>]
        shadowBias : float


        [<Option("occluder-body", HelpText = "The other body of a binary, cast as a shadow onto the target -- 'DIDYMOS' when rendering Dimorphos. The scene holds only the target, so without this an eclipsed epoch renders as full daylight; Dimorphos is inside Didymos' umbra for ~12 %% of the close-orbit phase. Empty (default) disables it.")>]
        occluderBody : string

        [<Option("occluder-frame", HelpText = "Body-fixed frame of --occluder-body (default: <body>_FIXED)")>]
        occluderFrame : string

        [<Option("occluder-obj", HelpText = "Shape model of --occluder-body as a Wavefront OBJ (`.obj.gz` works), so the eclipse is cast by the primary's real shape. Without it the occluder is a tessellation of the body's reference radii, which puts ingress and egress within seconds but cannot give the shadow's edge the right shape.")>]
        occluderObj : string

        [<Option("occluder-obj-scale", Default = 1000.0, HelpText = "Metres per --occluder-obj file unit (default 1000, i.e. kilometres)")>]
        occluderObjScale : float
        [<Option("pointing", HelpText = "Where the camera orientation comes from: 'ck' (default) uses the spacecraft's measured/planned attitude and FAILS if the kernels have none at this epoch; 'lookat' aims the boresight at the body centre with an up-vector roll convention. 'ck' can legitimately produce no image -- if the instrument was pointed elsewhere, the body is not in the frame, and that is the answer, not a fault.")>]
        pointing : string
    }

/// Options for the `simulate-series` verb.
///
/// Many epochs, many variants, one process. The per-frame options are deliberately the
/// same names as `simulate-image`'s, because they end up in the same shading uniforms --
/// what this verb adds is the epoch list, the variant presets and the output layout.
///
/// There is no `--force` and no resume: a run renders every frame it was asked for and
/// rewrites the variant folders. Resuming is what let a folder end up holding frames from
/// two different builds, 90 degrees apart, with nothing in the data saying so -- and with
/// the whole series rendering in one process, the thing resuming saved is no longer worth
/// the class of bug it costs.
[<Verb("simulate-series", HelpText = "Render a whole series of simulated instrument images in one process: many epochs x many shading variants, sharing one shape-model load, one de-shading fit and one scene graph.")>]
type SimulateSeriesOptions =
    {
        [<Option("opc", HelpText = "OPC directory of the body. Exactly one of --opc and --obj.")>]
        opc : string

        [<Option("obj", HelpText = "Shape model as a Wavefront OBJ instead of --opc; `.obj.gz` is read directly. Use this to render from the shape model the SPICE kernels ship (kernels/dsk/*.obj): at 5 km an AFC pixel covers 0.48 m while the Dimorphos OPC's posts are 1.96 m apart, so OPC frames are shape-limited rather than sensor-limited. Exactly one of --opc and --obj.")>]
        obj : string

        [<Option("obj-scale", Default = 1000.0, HelpText = "Metres per --obj file unit (default 1000, i.e. the file is in kilometres -- which is what the SPICE DSK shape models are). The body's extent in metres is logged, so a wrong scale is visible immediately.")>]
        objScale : float

        [<Option("obj-texture", HelpText = "Image to drape on --obj, using the mesh's own texture coordinates. Required by --deshade / --texture-albedo / --texture-only, which are refused without it. NOTE the `.png` beside each `.bds` in the kernel set is a preview render, not a map.")>]
        objTexture : string

        [<Option("times-file", HelpText = "File of observation times, one ISO-8601 UTC epoch per line; blank lines and lines starting with '#' are ignored", Required = true)>]
        timesFile : string

        [<Option("out", HelpText = "Output directory; each variant gets a subdirectory of frames and sidecars", Required = true)>]
        out : string

        [<Option("variants", Default = "delit,baked,micro,smooth", HelpText = "Which shading variants to render, comma-separated: 'delit' (texture with its baked illumination divided out, + micro-structure -- the realistic one), 'baked' (the same texture WITHOUT that division, so the mosaic's own lighting stays in and this epoch's sun lights it again -- the naive rendering, for testing whether de-lighting is necessary at all), 'micro' (constant albedo + micro-structure), 'smooth' (constant albedo, no micro-structure). All share one camera and one exposure, so any pair differs in exactly one thing.")>]
        variants : string

        [<Option("stem-prefix", HelpText = "Filename prefix for the frames (default: derived from the instrument, e.g. HERA_AFC-1 -> AFC1). Frames are <prefix>_<VARIANT>_<yyyyMMdd_HHmmss>.png")>]
        stemPrefix : string

        [<Option("instrument", HelpText = "SPICE instrument frame whose frustum to render with (default HERA_AFC-1)")>]
        instrument : string

        [<Option("observer", HelpText = "Spacecraft carrying the instrument (default HERA)")>]
        observer : string

        [<Option("body", HelpText = "SPICE body name of the OPC (default DIMORPHOS)")>]
        body : string

        [<Option("frame", HelpText = "Body-fixed reference frame (default DIMORPHOS_FIXED)")>]
        frame : string

        [<Option("kernel", HelpText = "Explicit SPICE metakernel; default: <kernel-root>/mk/hera_plan.tm")>]
        kernel : string

        [<Option("kernel-root", HelpText = "SPICE kernel tree, defaulting to $PRO3D_SPICE_KERNELS")>]
        kernelRoot : string

        [<Option("pointing", HelpText = "Where the camera orientation comes from: 'ck' (default) uses the spacecraft's attitude and fails where the kernels have none; 'lookat' aims at the body centre. With 'ck' an epoch outside an observation window renders nothing, and that is an answer, not a fault.")>]
        pointing : string

        [<Option("distance", Default = 0.0, HelpText = "Camera distance in metres; 0 (default) uses the spacecraft's real distance at each epoch")>]
        distance : float

        [<Option("width", HelpText = "Output width; 0 (default) uses the instrument's native width")>]
        width : int

        [<Option("height", HelpText = "Output height; 0 (default) uses the instrument's native height")>]
        height : int

        [<Option("albedo", Default = 0.16, HelpText = "Normal reflectance of the surface (default 0.16, the measured Dimorphos value)")>]
        albedo : float

        [<Option("deshade-layer", HelpText = "Texture layer the 'delit' variant de-shades and draws; also selects --texture-layer, so one name drives both the fit and the divisor")>]
        deshadeLayer : string

        [<Option("texture-layer", HelpText = "Texture layer to draw, by name or index. Defaults to --deshade-layer when that is given.")>]
        textureLayer : string

        [<Option("micro-scale", Default = 0.5, HelpText = "Feature size of the procedural micro-structure in metres (default 0.5)")>]
        microScale : float

        [<Option("micro-amplitude", Default = 0.3, HelpText = "Normal perturbation strength for the 'delit' and 'micro' variants; 'smooth' is always 0")>]
        microAmplitude : float

        [<Option("ambient", Default = 0.02, HelpText = "Ambient floor so the night side is distinguishable from space (default 0.02)")>]
        ambient : float

        [<Option("gain", Default = 0.0, HelpText = "Linear I/F -> DN gain. Give one: across a series a per-frame auto-exposure (0) removes the very thing the series shows, the body getting brighter and darker as the illumination changes.")>]
        gain : float

        [<Option("no-shadows", HelpText = "Skip the sun shadow map; shading then comes from the local sun angle alone")>]
        noShadows : bool

        [<Option("no-lighting", HelpText = "Render a flat disk instead of a shaded body: the frame is then the silhouette. For comparing geometry against a renderer that does not light its output, where shading is only a source of disagreement.")>]
        noLighting : bool

        [<Option("shadow-bias", Default = 0.006, HelpText = "Shadow-map depth bias in normalized depth (default 0.006); swept against a SPICE ray-cast, see scripts/check-lighting.py")>]
        shadowBias : float

        [<Option("max-reprojection-error", Default = 0.1, HelpText = "How far, in pixels, the camera reconstructed from a frame's own sidecar may sit from the camera that rendered it before the frame counts as failed (default 0.1). This is checked for EVERY frame: a sidecar that does not reproduce its own render makes the frame unusable for projection, and finding that out later costs the whole series.")>]
        maxReprojectionError : float


        [<Option("occluder-body", HelpText = "The other body of a binary, cast as a shadow onto the target -- 'DIDYMOS' when rendering Dimorphos. The scene holds only the target, so without this an eclipsed epoch renders as full daylight; Dimorphos is inside Didymos' umbra for ~12 %% of the close-orbit phase. Empty (default) disables it.")>]
        occluderBody : string

        [<Option("occluder-frame", HelpText = "Body-fixed frame of --occluder-body (default: <body>_FIXED)")>]
        occluderFrame : string

        [<Option("occluder-obj", HelpText = "Shape model of --occluder-body as a Wavefront OBJ (`.obj.gz` works), so the eclipse is cast by the primary's real shape. Without it the occluder is a tessellation of the body's reference radii, which puts ingress and egress within seconds but cannot give the shadow's edge the right shape.")>]
        occluderObj : string

        [<Option("occluder-obj-scale", Default = 1000.0, HelpText = "Metres per --occluder-obj file unit (default 1000, i.e. kilometres)")>]
        occluderObjScale : float

        [<Option("keep-going", HelpText = "Render the remaining epochs after a failure instead of stopping. The run still exits non-zero and names every frame that failed.")>]
        keepGoing : bool
    }

/// Options for the `unproject` verb.
///
/// Same convention as `sun-angles`: string defaults are applied in code, because an unsupplied
/// string field arrives as null.
[<Verb("unproject", HelpText = "Convert image pixel coordinates to body-fixed surface coordinates on a shape model.")>]
type UnprojectOptions =
    {
        [<Option("config", HelpText = "JSON file supplying any of the options below; explicit flags win")>]
        config : string

        [<Option("opc", HelpText = "OPC directory of the body")>]
        opc : string

        [<Option("images", HelpText = "Folder containing the instrument images with .mbi.json sidecars")>]
        images : string

        [<Option("input", HelpText = "Table of 'image, x, y' rows; extra columns are carried through")>]
        input : string

        [<Option("out", HelpText = "Output table (default: ./unproject.csv)")>]
        out : string

        [<Option("body", HelpText = "SPICE body name of the OPC (default DIDYMOS)")>]
        body : string

        [<Option("frame", HelpText = "Body-fixed frame the coordinates are reported in (default DIDYMOS_FIXED)")>]
        frame : string

        [<Option("observer", HelpText = "Observing spacecraft; default is derived per image from the instrument")>]
        observer : string

        [<Option("kernel", HelpText = "Explicit SPICE metakernel; overrides the sidecar")>]
        kernel : string

        [<Option("kernel-root", HelpText = "SPICE kernel tree. Defaults to $PRO3D_SPICE_KERNELS.")>]
        kernelRoot : string

        [<Option("method", HelpText = "Projection method: spice or mbi (default mbi)")>]
        method : string

        [<Option("pixel-convention", HelpText = "How the input addresses pixels: 'image' (default, 0-based, top-left, y down) or 'fits' (1-based, bottom-left, y up)")>]
        pixelConvention : string
    }

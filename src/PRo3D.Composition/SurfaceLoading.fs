namespace PRo3D.Composition

open System
open System.IO

open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive

open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

/// Importing surfaces into a `SurfaceModel` and building their scene graphs. Typed on
/// `SurfaceModel` only, so every host (Viewer, Lite) shares it; the host writes the result back.
module SurfaceLoading =

    let private readLine (filePath:string) =
      use sr = new StreamReader (filePath)
      sr.ReadLine ()

    /// A `<surface>.trafo` next to the import path is a legacy pre-transform.
    let addLegacyTrafos (surfaces : IndexList<Surface>) =
        surfaces
        |> IndexList.map(fun s -> s,Path.ChangeExtension(s.importPath, ".trafo"))
        |> IndexList.map(fun(s,p) ->
           match (Serialization.fileExists p) with
           | Some path->
               let t = readLine path
               Log.line "[TRAFO] Importing trafo: %s" (t.ToString ())
               { s with preTransform = Trafo3d.Parse(t) }
           | None -> s
        )

    let getOPCxPath (surfacePath : string) =
        let name = Path.GetFileName surfacePath
        Path.ChangeExtension(Path.Combine(surfacePath, name), ".opcx")

    /// Reads the `.opcx` attribute/texture layer description, if the surface has one.
    let addSurfaceAttributes (surfaces : IndexList<Surface>) =
        surfaces
        |> IndexList.map(fun s -> s, s.importPath |> getOPCxPath)
        |> IndexList.map(fun(s,p) ->
           let loadOpcX (path : string) =
               let layers = SurfaceUtils.SurfaceAttributes.read path
               let textures = layers |> SurfaceProperties.getTextures

               // *.opc.json sidecar: DEM reference model and, for OPCs derived from a
               // SPICE DSK, the DSKBRIEF summary of the source *.bds shape model.
               // Not persisted into the scene - logged so the provenance is visible.
               OpcMetadata.tryReadForOpcx path
               |> Option.iter (OpcMetadata.log s.name)

               { s with
                   scalarLayers  = layers |> SurfaceProperties.getScalarsHmap
                   textureLayers = textures
                   primaryTexture = textures |> IndexList.tryFirst
                   opcxPath = Some path
               }
           match Serialization.fileExists p with
           | Some path->
               loadOpcX path
           | None ->
               match Directory.EnumerateFiles(s.importPath, "*.opcx") |> Seq.toList with
               | [singleOpcX] -> loadOpcX singleOpcX
               | _ -> s
        )

    /// Appends surfaces to the model (into the active group) and rebuilds the OPC scene graphs.
    let importSurfaces
        (runtime   : IRuntime)
        (signature : IFramebufferSignature)
        (scenePath : Option<string>)
        (surfaces  : IndexList<Surface>)
        (model     : SurfaceModel) : SurfaceModel =

        let surfaces =
            surfaces
            |> addLegacyTrafos
            |> addSurfaceAttributes
            |> IndexList.map( fun x -> { x with colorCorrection = Init.initColorCorrection})
            |> IndexList.map( fun x -> { x with radiometry = Init.initRadiometry})

        let existingSurfaces =
            model.surfaces.flat
            |> Leaf.toSurfaces
            |> HashMap.toList
            |> List.map snd
            |> IndexList.ofList

        let sChildren =
            surfaces
            |> IndexList.map Leaf.Surfaces

        let model =
            { model with surfaces = GroupsApp.addLeaves model.surfaces.activeGroup.path sChildren model.surfaces }

        let surfaceMap =
            (model.surfaces.flat |> Leaf.toSurfaces)

        let allSurfaces = existingSurfaces |> IndexList.append surfaces

        let sgSurfaces =
            allSurfaces
            |> IndexList.filter (fun s -> s.surfaceType = SurfaceType.SurfaceOPC)
            |> Sg.createSgSurfaces runtime signature
            |> HashMap.union model.sgSurfaces
            |> Files.expandLazyKdTreePaths scenePath surfaceMap

        { model with sgSurfaces = sgSurfaces }
        |> SurfaceModel.triggerSgGrouping

    /// `importSurfaces` for hosts that must not crash on a broken dataset: loading a patch
    /// hierarchy throws on malformed data.
    let tryImportSurfaces runtime signature scenePath surfaces (model : SurfaceModel) : Result<SurfaceModel, string> =
        try
            importSurfaces runtime signature scenePath surfaces model |> Ok
        with e ->
            Log.error "[SurfaceLoading] import failed: %s" e.Message
            Result.Error e.Message

    /// Builds the scene graphs of every surface in a (freshly deserialized) model.
    let prepareSurfaceModel
        (runtime   : IRuntime)
        (signature : IFramebufferSignature)
        (scenePath : Option<string>)
        (model     : SurfaceModel) : SurfaceModel =

        let surfaces = model.surfaces.flat |> Leaf.toSurfaces

        let surfacesList =
            surfaces
            |> HashMap.toList
            |> List.map snd
            |> IndexList.ofList

        let opcSurfs =
            surfacesList
            |> IndexList.filter ( fun x -> x.surfaceType = SurfaceType.SurfaceOPC)

        let sgSurfaces =
            Sg.createSgSurfaces runtime signature opcSurfs |> Files.expandLazyKdTreePaths scenePath surfaces

        // TODO hs: should how to handle multiple loaders here?
        let objSurfs =
            surfacesList
            |> IndexList.filter ( fun x -> x.surfaceType = SurfaceType.Mesh)

        let sgSurfaceObj =
            SurfaceUtils.ObjectFiles.CustomWavefrontLoader.createSgObjectsWavefront objSurfs

        let sgs = sgSurfaces |> HashMap.union sgSurfaceObj

        model
        |> SurfaceModel.withSgSurfaces sgs
        |> SurfaceModel.triggerSgGrouping

    /// Every OPC directory under `folder` (the folder itself, or a folder of OPCs), as
    /// importable surfaces. Same discovery the Viewer's "discover OPCs" import uses.
    let discoverOpcSurfaces (maxTriangleSize : float) (folders : list<string>) : IndexList<Surface> =
        folders
        |> List.choose Files.tryDirectoryExists
        |> List.collect Files.superDiscoveryMultipleSurfaceFolder
        |> List.filter (fun x -> Files.isSurfaceFolder x || Files.isZippedOpcFolder x)
        |> List.map (SurfaceUtils.mk SurfaceType.SurfaceOPC MeshLoaderType.Unkown maxTriangleSize)
        |> IndexList.ofList

    /// Surfaces saved with relative paths live in `<scene dir>/Surfaces/<surface name>`.
    let expandRelativePaths (scenePath : string) (model : SurfaceModel) : SurfaceModel =
        let surfacesDir = Path.Combine(Path.GetDirectoryName scenePath, "Surfaces")
        let flat =
            model.surfaces.flat
            |> Leaf.toSurfaces
            |> HashMap.map (fun _ s ->
                if s.relativePaths then
                    { s with opcPaths = Files.expandNamesToPaths (Path.Combine(surfacesDir, s.name)) s.opcNames }
                else s
                |> Leaf.Surfaces
            )
        { model with surfaces = { model.surfaces with flat = flat } }

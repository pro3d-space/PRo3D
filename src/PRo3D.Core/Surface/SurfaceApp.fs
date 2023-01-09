namespace PRo3D.Core.Surface

open System
open System.IO
open System.Diagnostics
open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.Opc

open Aardvark.VRVis.Opc
open Aardvark.UI.Operators
open Aardvark.UI.Trafos  

open OpcViewer.Base
open OpcViewer.Base.Picking

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Surface

open Adaptify.FSharp.Core
open Aardvark.Base.Coder

type SurfaceAppAction =
| SurfacePropertiesMessage   of SurfaceProperties.Action
| FlyToSurface               of Guid
| MakeRelative               of Guid
| RemoveSurface              of Guid*list<Index>
| PickSurface                of SceneHit*string
| OpenFolder                 of Guid
| RebuildKdTrees             of Guid
| ToggleActiveFlag           of Guid
| ChangeImportDirectory      of Guid*string
| ChangeImportDirectories    of list<string>
| ChangeOBJImportDirectories of list<string>
| GroupsMessage              of GroupsAppAction
//| PickObject                 of V3d
| PlaceSurface               of V3d
| ScalarsColorLegendMessage  of FalseColorLegendApp.Action
| ColorCorrectionMessage     of ColorCorrectionProperties.Action
| SetHomePosition            
| TranslationMessage         of TranslationApp.Action
| SetPreTrafo                of string


module SurfaceUtils =    
    
    /// creates a surface from opc folder path
    let mk (stype:SurfaceType) (preferredLoader : MeshLoaderType) (maxTriangleSize : float) path =                 
    
        let names = Files.getOPCNames path                
        {
            version         = Surface.current
            guid            = Guid.NewGuid()
            name            = IO.Path.GetFileName path
            importPath      = path
            opcNames        = names
            opcPaths        = names |> Files.expandNamesToPaths path
            fillMode        = FillMode.Fill
            cullMode        = CullMode.None
            isVisible       = true
            isActive        = true
            relativePaths   = false
            quality         = Init.quality
            priority        = Init.priority
            scaling         = Init.scaling
            preTransform    = Trafo3d.Identity            
            scalarLayers    = HashMap.Empty //IndexList.empty
            selectedScalar  = None
            textureLayers   = IndexList.empty
            selectedTexture = None         
    
            triangleSize    = { Init.triangleSize with value = maxTriangleSize }

            surfaceType     = stype
            preferredLoader = preferredLoader
    
            colorCorrection = Init.initColorCorrection
            homePosition    = None
            transformation  = Init.transformations
        }       
   

    module ObjectFiles =        
        open Aardvark.Geometry
        open Aardvark.Data.Wavefront
        
        let saveKdTree (path, kdTree : Aardvark.Geometry.KdIntersectionTree) =
            // serialize
            use stream = new MemoryStream()
            use coder = new BinaryWritingCoder(stream)
            coder.CodeT(ref kdTree)
            
            // write to file
            use fileStream = File.Create(path)
            //stream.Position <- int64 0
            stream.Seek(int64 0, SeekOrigin.Begin) |> ignore
            stream.CopyTo(fileStream)
            fileStream.Close ()


        module AssimpLoader =


            let loadObject (surface : Surface) : SgSurface =
                Log.line "[OBJ] Please wait while the file is being loaded..." 
                let obj = Loader.Assimp.load (surface.importPath)  
                Log.line "[OBJ] The file was loaded successfully!" 
                let dir = Path.GetDirectoryName(surface.importPath)
                let filename = Path.GetFileNameWithoutExtension surface.importPath
                let kdTreePath = Path.combine [dir; filename + ".aakd"] //Path.ChangeExtension(s.importPath, name)
                let mutable count = 0
                let kdTrees = 
                    if File.Exists(kdTreePath) |> not then
                        obj.meshes 
                        |> Array.map(fun x ->
                            let kdPath = sprintf "%s_%i.kd" surface.importPath count          
                            let pos = x.geometry.IndexedAttributes.[DefaultSemantic.Positions] |> unbox<V3f[]>
                        
                            //let indices = x.geometry.IndexArray |> unbox<int[]> potential problem with indices
                            let t = 
                                pos                      
                                |> Seq.map(fun x -> x.ToV3d())
                                |> Seq.chunkBySize 3
                                |> Seq.filter(fun x -> x.Length = 3)
                                |> Seq.map(fun x -> Triangle3d x)
                                |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) |> Seq.toArray
                                |> TriangleSet
                                              
                            Log.startTimed "Building kdtrees for %s" (Path.GetFileName surface.importPath |> Path.GetFileName)
                            let tree = 
                                KdIntersectionTree(t, 
                                    KdIntersectionTree.BuildFlags.MediumIntersection + KdIntersectionTree.BuildFlags.Hierarchical) //|> PRo3D.Serialization.save kdTreePath                  
                            Log.stop()
                        
                            saveKdTree (kdPath, tree) |> ignore
                        
                            let kd : KdTrees.LazyKdTree = {
                                kdTree        = Some (tree.ToConcreteKdIntersectionTree());
                                affine        = Trafo3d.Identity
                                boundingBox   = tree.BoundingBox3d
                                kdtreePath    = kdPath
                                objectSetPath = ""                  
                                coordinatesPath = ""
                                texturePath = ""
                              } 
                        
                            count <- count + 1
                        
                            kd.boundingBox, (KdTrees.Level0KdTree.LazyKdTree kd)
                        ) 
                        |> Array.toList
                        |> Serialization.save kdTreePath
                
                    else
                        Serialization.loadAs<List<Box3d*KdTrees.Level0KdTree>> kdTreePath
                        |> List.map(fun kd -> 
                            match kd with 
                            | _, KdTrees.Level0KdTree.InCoreKdTree tree -> (tree.boundingBox, (KdTrees.Level0KdTree.InCoreKdTree tree))
                            | _, KdTrees.Level0KdTree.LazyKdTree tree -> 
                               let loadedTree = if File.Exists(tree.kdtreePath) then Some (tree.kdtreePath |> KdTrees.loadKdtree) else None
                               (tree.boundingBox, (KdTrees.Level0KdTree.LazyKdTree {tree with kdTree = loadedTree}))
                        )

                let bb = obj.bounds
                let sg = 
                    obj
                    |> Sg.adapter
                    |> Sg.requirePicking
                    |> Sg.noEvents
                    |> Sg.scale 1.0
                    //|> Sg.uniform "RoverMVP" (AVal.constant M44f.Identity)
                    //|> Sg.uniform "HasRoverMVP" (AVal.constant false)

                let pose = Pose.translate bb.Center // bb.Center V3d.Zero
                let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }

                {
                    surface     = surface.guid    
                    trafo       = trafo
                    globalBB    = bb
                    sceneGraph  = sg
                    picking     = Picking.KdTree(kdTrees |> HashMap.ofList) //Picking.PickMesh meshes
                    isObj       = true
                    //transformation = Init.Transformations
                }
                 
            let createSgObjects surfaces =
                let sghs =
                  surfaces
                    |> IndexList.toList 
                    |> List.map loadObject

                let sgObjects =
                    sghs 
                      |> List.map (fun d -> (d.surface, d))
                      |> HashMap.ofList       

                sgObjects

        module CustomWavefrontLoader =

            type Face = {
                positionIndices : int[]
                texCoordIndices : int[]
                normalIndices   : int[]
                materialIndex   : int
            } 

            let getVertexColors (obj : WavefrontObject) =
                if (obj.VertexColors.IsEmptyOrNull() |> not) then
                        let isfloat = obj.VertexColors |> Seq.take 100 |> Seq.tryFind(fun c -> (c.R > 1.0f || c.G > 1.0f || c.B > 1.0f)) |> Option.isNone
                          
                        let colorArray = 
                            obj.VertexColors 
                            |> Seq.toArray 
                          
                        let colorArray2 =
                            if isfloat then
                                colorArray |> Array.map(fun c -> C4b((float c.R), (float c.G), (float c.B)))
                            else
                                colorArray |> Array.map(fun c -> C4b((int c.R), (int c.G), (int c.B)))
                                  
                        colorArray2
                    else [||]

            // this one is a bit ugly but performs inplace (modifies the array)
            let patchUVConvention (ig : IndexedGeometry) =
                match ig.IndexedAttributes.[DefaultSemantic.DiffuseColorCoordinates] with
                | :? array<V2f> as vertices -> 
                    for i in 0 .. vertices.Length - 1 do 
                        vertices.[i] <- V2f(vertices.[i].X, 1.0f - vertices.[i].Y)
                | :? array<V2d> as vertices -> 
                    for i in 0 .. vertices.Length - 1 do 
                    vertices.[i] <- V2d(vertices.[i].X, 1.0 - vertices.[i].Y)

                | v -> failwithf "UVs must be V2f or V2d (is: %A)" (v.GetType().GetElementType())

            let sgOfPolyMesh (texturePath : Option<string>) (vColor : C4b[]) (mesh : PolyMesh) =
                // apply vertex color
                if vColor.IsEmptyOrNull() |> not then
                    mesh.VertexAttributes.[DefaultSemantic.Colors] <- vColor
             
                let shift = Trafo3d.Translation(-mesh.BoundingBox3d.Center) // largecoordate - center => smaller coordinates
                let mesh = 
                    mesh.Transformed(shift) // large coordiante + (-shift) => small coordinate

                let mesh =
                    match texturePath with
                    | None -> 
                        // if we have not texture, make sure there are normals
                        if mesh.VertexAttributes.Contains(DefaultSemantic.Normals) then
                            mesh
                        else
                            mesh.WithPerVertexIndexedNormals(30.0 * Constant.RadiansPerDegree)
                    | _ -> 
                        mesh

                let ig = mesh.GetIndexedGeometry() // use high-level getIndexedGeometry function - low level access to arrays is error prone 

                let hasCoordiantes = ig.IndexedAttributes.Contains(DefaultSemantic.DiffuseColorCoordinates)
                if hasCoordiantes then
                    patchUVConvention ig

                let hasTextureAndCoords = 
                    hasCoordiantes && Option.isSome texturePath

                let applyTextureOrReplacement (sg : ISg) = 
                    match texturePath with
                    | None -> 
                        // i use fileTexture directly instead of Sg.texture - does this suffice? what do you think
                        sg |> Sg.texture DefaultSemantic.DiffuseColorTexture DefaultTextures.checkerboard
                    | Some texturePath ->
                        sg |> Sg.fileTexture DefaultSemantic.DiffuseColorTexture texturePath true // yes generate mipmaps


            
                // create the scene graph. note that, depending on the shader the sg is potentially missing coordinates etc
                Sg.ofIndexedGeometry ig
                // internally this creates https://github.com/aardvark-platform/aardvark.rendering/blob/032bce5ee4ce25d9b876c1f978231325f7d6e253/src/Aardvark.SceneGraph/SgFSharp.fs#L724
                // and https://github.com/aardvark-platform/aardvark.rendering/blob/032bce5ee4ce25d9b876c1f978231325f7d6e253/src/Aardvark.SceneGraph/SgFSharp.fs#L58
                // which is a single value - this allows us to have a placeholder independet of vertex array length...
                // note: if sg already has coordiantes, it overwrides this value anyways.. so no harm to apply it always..
                // less complex code less problems....
                |> Sg.vertexBufferValue DefaultSemantic.Colors (V4f.One |> AVal.constant)

                // handle texture related stuff
                |> Sg.vertexBufferValue DefaultSemantic.DiffuseColorCoordinates (V2d.Zero |> AVal.constant)
                // anyways, let us create a uniform, just in case the shader needs to know whether correct coordinates have been applied
                |> Sg.uniform "HasDiffuseColorCoordinates" (hasCoordiantes |> AVal.constant)
                |> Sg.uniform "HasDiffuseColorTexture" (hasTextureAndCoords |> AVal.constant)
                |> applyTextureOrReplacement

                |> Sg.vertexBufferValue DefaultSemantic.Normals (V4f.OOII |> AVal.constant)
                |> Sg.uniform "HasNormals" (ig.IndexedAttributes.Contains(DefaultSemantic.Normals) |> AVal.constant)

                |> Sg.trafo' shift.Inverse // apply inverse centering trafo to position the scene correctly
            

            // creates a scene graph, transforms all objects into the 
            let createSceneGraph (obj : WavefrontObject) = 
                let meshes = obj.GetFaceSetMeshes(true) |> Seq.toArray // create double meshes. later we will reduce it to float

                let meshesWithMaterial =
                    meshes |> Array.collect (fun pm ->
                        let mats = pm.FaceAttributes.[PolyMesh.Property.Material] :?> int[]
                        let diff = System.Collections.Generic.HashSet mats

                        if diff.Count = 1 then
                            let mid = Seq.head diff
                            pm.InstanceAttributes.[PolyMesh.Property.Material] <- mid
                            [| pm |]
                        else
                            diff |> Seq.toArray |> Array.map (fun mid ->
                                let faces = System.Collections.Generic.HashSet<int>()
                                for i in 0 .. pm.FaceCount - 1 do
                                    if mats.[i] = mid then faces.Add i |> ignore
                                let res = pm.SubSetOfFaces(faces)
                                res.InstanceAttributes.[PolyMesh.Property.Material] <- mid
                                res
                            )
                    )

                let vertexColors = obj |> getVertexColors
                meshesWithMaterial
                |> Array.choose (fun mesh -> 
                    match mesh.InstanceAttributes.TryGetValue PolyMesh.Property.Material with
                    | (true, (:? int as v)) ->
                        if v >= 0 && v < obj.Materials.Count then
                            let mat = obj.Materials[v]
                            let texturePath =
                                mat.MapItems |> Seq.tryPick (fun item -> 
                                    match item.Value with
                                    | :? string as value when item.Key = WavefrontMaterial.Property.DiffuseColorMap -> 
                                        Some value
                                    | _ -> 
                                        None
                                )
                            Some (sgOfPolyMesh texturePath vertexColors mesh)
                        else 
                            None
                    | _ -> 
                        None
                )
                |> Sg.ofArray


            let createSgsofOBJ (obj : WavefrontObject) (box : Box3d) = 
                if obj.Materials.IsEmptyOrNull() || obj.Materials.Count = 1 then
                    let textureOption = 
                        obj.Materials
                        |> Seq.tryHead
                        |> Option.map(fun mat -> mat.MapItems |> Seq.tryFind(fun item -> item.Key = WavefrontMaterial.Property.DiffuseColorMap) |> Option.map(fun item -> (string item.Value)))
             
              
                    let meshes = 
                        obj.GetFaceSetMeshes(true)
                        |> Seq.toList

                    let igs  = 
                        meshes 
                        |> List.map(fun mesh -> 
                      
                            let posArray = mesh.VertexAttributes.[DefaultSemantic.Positions].ToArrayOfT<V3d>() //|> Array.map(fun p -> p - box.Min)

                            mesh.VertexAttributes.[DefaultSemantic.Positions] <- posArray

                            if (obj.VertexColors.IsEmptyOrNull() |> not) then
                                let isfloat = obj.VertexColors |> Seq.take 100 |> Seq.tryFind(fun c -> (c.R > 1.0f || c.G > 1.0f || c.B > 1.0f)) |> Option.isNone
                          
                                let colorArray = 
                                    obj.VertexColors 
                                    |> Seq.toArray 
                          
                                let colorArray2 =
                                    if isfloat then
                                        colorArray |> Array.map(fun c -> C4b((float c.R), (float c.G), (float c.B)))
                                    else
                                        colorArray |> Array.map(fun c -> C4b((int c.R), (int c.G), (int c.B)))
                                  
                                mesh.VertexAttributes.[DefaultSemantic.Colors] <- colorArray2

                      
                            let hasTexCoords = obj.TextureCoordinates.IsEmptyOrNull() |> not                      
                            let hasTexture   = textureOption |> Option.map(fun tO -> tO |> Option.isSome) |> Option.defaultValue false
                      
                            if hasTexture && hasTexCoords then

                                let texCoordsArray = 
                                    (mesh.FaceVertexAttributes.[DefaultSemantic.DiffuseColorCoordinates].ToArrayOfT<V2d>())
                                    |> Array.map(fun f -> V2d(f.X, 1.0-f.Y))

                                mesh.FaceVertexAttributes.[DefaultSemantic.DiffuseColorCoordinates] <- texCoordsArray

                            mesh.GetIndexedGeometry(PolyMesh.GetGeometryOptions.Default))

                    let isgs = 
                        igs 
                        |> List.map (fun ig -> 
                            textureOption
                            |> Option.map(fun potPath -> 
                                potPath
    
                                |> Option.map(fun texPath ->
                                    let texture = 
                                        let config = { wantMipMaps = true; wantSrgb = false; wantCompressed = false }
                                        FileTexture(texPath,config) :> ITexture
                                    ig.Sg
                                    |> Aardvark.SceneGraph.SgFSharp.Sg.texture DefaultSemantic.DiffuseColorTexture (AVal.constant texture))
                                |> Option.defaultValue ig.Sg)
                            |> Option.defaultValue ig.Sg
                            |> Sg.noEvents)
                  
                    isgs 
          
                else
               
                    let vertexList      = obj.Vertices.ToListOfT<V4d>()

                    let offset          = vertexList |> Seq.head

                    let positions       = vertexList |> Seq.toList |> List.map(fun p -> V3d(p.X-offset.X, p.Y-offset.Y, p.Z-offset.Z))
                    let colors          = if obj.VertexColors.IsEmptyOrNull() then None else Some(obj.VertexColors |> Seq.toList)
                    let coordsOption    = if obj.TextureCoordinates.IsEmptyOrNull() then None else Some (obj.TextureCoordinates |> Seq.toList)
                    let normalsOption   = if obj.Normals.IsEmptyOrNull() then None else Some (obj.Normals |> Seq.toList)
                    let faceSets        = obj.FaceSets |> Seq.toList

                    let createISgOfFaces (diffuseTextureFile : Option<string>) (color : Option<C3f>) (faces : List<Face>) =                 
                        let posIndices, texCoordIndices, normalIndices = faces |> List.map(fun f -> (f.positionIndices, f.texCoordIndices, f.normalIndices)) |> List.unzip3
    
                        let posIComplete     = posIndices      |> Array.concat
                        let texCoordComplete = texCoordIndices |> Array.concat
                        let normalsComplete  = normalIndices   |> Array.concat

                        let matColor = color |> Option.map(fun c -> C3f(c.R, c.G, c.B)) |> Option.defaultValue C3f.White
    
                        let faceSetPositions, faceSetColors = 
                            colors
                            |> Option.map(fun cList -> 
                                posIComplete
                                |> Array.map(fun value -> positions.[value], cList.[value])
                                |> Array.unzip)
                            |> Option.defaultValue (
                                posIComplete
                                |> Array.map(fun value -> positions.[value], matColor)
                                |> Array.unzip)
                      
                  
                        let def = [
                            DefaultSemantic.Positions, (faceSetPositions) :> Array
                            DefaultSemantic.Colors, (faceSetColors) :> Array                    
                        ]
    
                        let def = 
                            coordsOption 
                            |> Option.map(fun coords -> 
                                let faceSetTexCoords =
                                    texCoordComplete
                                    |> Array.mapi(fun _ value -> V2f(coords.[value].X, (1.0f- coords.[value].Y)))
                              
                                def |> List.append [DefaultSemantic.DiffuseColorCoordinates, (faceSetTexCoords) :> Array])
                            |> Option.defaultValue def
    
                        let def = 
                            normalsOption
                            |> Option.map(fun normals ->
                                let faceSetNormals = 
                                    normalsComplete
                                    |> Array.mapi(fun _ n -> normals.[n])
                          
                                def |> List.append [DefaultSemantic.Normals, (faceSetNormals) :> Array])
                            |> Option.defaultValue def
                                                  
                        let indexAttributes = def |> SymDict.ofList 
    
                        let index = [|0 .. posIComplete.Length-1|]
    
                        let geometry =
                            IndexedGeometry(
                                Mode              = IndexedGeometryMode.TriangleList,
                                IndexArray        = index,
                                IndexedAttributes = indexAttributes
                            )       
                      
                        let sg = 
                            diffuseTextureFile 
                            |> Option.map(fun texPath -> 
                                let texture = 
                                    let config = { wantMipMaps = true; wantSrgb = false; wantCompressed = false }
                                    FileTexture(texPath,config) :> ITexture
                         
                                geometry.Sg
                                |> Aardvark.SceneGraph.SgFSharp.Sg.texture DefaultSemantic.DiffuseColorTexture (AVal.constant texture))
                            |> Option.defaultValue geometry.Sg
                            |> Sg.noEvents
    
    
                        sg
    
                    let isgs = 
                        faceSets
                        |> List.map(fun fs  -> 
                            fs.FirstIndices.RemoveAt(fs.FirstIndices.Count-1)
                            fs.FirstIndices
                            |> Seq.mapi (fun i firstIndex -> 
                                {
                                positionIndices = [| fs.VertexIndices.[firstIndex]; fs.VertexIndices.[firstIndex+1]; fs.VertexIndices.[firstIndex+2]|]        
                                texCoordIndices = [| fs.TexCoordIndices.[firstIndex]; fs.TexCoordIndices.[firstIndex+1]; fs.TexCoordIndices.[firstIndex+2]|]
                                normalIndices   = [| fs.NormalIndices.[firstIndex]; fs.NormalIndices.[firstIndex+1]; fs.TexCoordIndices.[firstIndex+2]|]
                                materialIndex   = fs.MaterialIndices.[i]                        
                                })
                            |> List.ofSeq
                            |> List.groupBy(fun face -> face.materialIndex)
                            )
                        |> List.concat
                        |> List.map(fun (matIndex,faceList) -> 
                            let currMapItems = obj.Materials.Item(matIndex).MapItems
                            let color    =  currMapItems |> Seq.tryFind(fun item -> item.Key = WavefrontMaterial.Property.DiffuseColor) |> Option.map(fun item -> (item.Value :?> C3f))
                            let fileName =  currMapItems |> Seq.tryFind(fun item -> item.Key = WavefrontMaterial.Property.DiffuseColorMap) |> Option.map(fun item -> (string item.Value)) //materials.[matIndex]. |> Seq.tryFind WavefrontMaterial.Property.DiffuseColorMap
                            createISgOfFaces fileName color faceList)

                    isgs

            // TEST LAURA: load .obj with wavefront (Martins code from dibit) (+ Harris updates Nov.22)
            let loadObjectWavefront (surface : Surface) : SgSurface =
                Log.line "[OBJ WAVEFRONT] Please wait while the file is being loaded..."
                let obj = ObjParser.Load(surface.importPath, true)
                Log.line "[OBJ WAVEFRONT] The file was loaded successfully!" 
                let dir = Path.GetDirectoryName(surface.importPath)
                let filename = Path.GetFileNameWithoutExtension surface.importPath
                let kdTreePath = Path.combine [dir; filename + ".aakd"] 
                let meshes = obj.GetFaceSetMeshes(true) |> Seq.toList
                let mutable count = 0
                let kdTrees = 
                    if File.Exists(kdTreePath) |> not then
                        meshes
                        |> List.map(fun x ->
                            let kdPath = sprintf "%s_%i.kd" surface.importPath count
                            Log.line "loading positions and indices of OBJ-Object"
                        
                            //let positions = x.PositionArray 
                            //let indices = x.VertexIndexArray 

                            Log.line "start building kdTree"
                            let triMesh = x.TriangulatedCopy()
                            let test = 
                                triMesh.Faces 
                                |> Seq.collect (fun face -> face.Polygon3d.Points )
                                |> Seq.chunkBySize 3
                                |> Seq.map(fun x -> Triangle3d x)
                                |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) |> Seq.toArray
                                |> TriangleSet
                                                                                

                            //Log.line "start building kdTree"
                            //let t = 
                            //    indices 
                            //    |> Seq.map(fun x -> positions.[x])
                            //    |> Seq.chunkBySize 3
                            //    |> Seq.filter(fun x -> x.Length = 3)
                            //    |> Seq.map(fun x -> Triangle3d x)
                            //    |> Seq.filter(fun x -> (IntersectionController.triangleIsNan x |> not)) |> Seq.toArray
                            //    |> TriangleSet
                    
                            Log.startTimed "Building kdtrees for %s" (Path.GetFileName surface.importPath |> Path.GetFileName)
                            let tree = 
                                KdIntersectionTree(test, 
                                    KdIntersectionTree.BuildFlags.MediumIntersection + KdIntersectionTree.BuildFlags.Hierarchical) //|> PRo3D.Serialization.save kdTreePath                  
                            Log.stop()
                        
                            saveKdTree (kdPath, tree) |> ignore
                        
                            let kd : KdTrees.LazyKdTree = {
                                kdTree        = Some (tree.ToConcreteKdIntersectionTree());
                                affine        = Trafo3d.Identity
                                boundingBox   = tree.BoundingBox3d
                                kdtreePath    = kdPath
                                objectSetPath = ""                  
                                coordinatesPath = ""
                                texturePath = ""
                                } 
                        
                            count <- count + 1
                            kd.boundingBox, (KdTrees.Level0KdTree.LazyKdTree kd)
                        ) 
                        |> Serialization.save kdTreePath
                
                    else
                        Serialization.loadAs<List<Box3d*KdTrees.Level0KdTree>> kdTreePath
                        |> List.map(fun kd -> 
                            match kd with 
                            | _, KdTrees.Level0KdTree.InCoreKdTree tree -> (tree.boundingBox, (KdTrees.Level0KdTree.InCoreKdTree tree))
                            | _, KdTrees.Level0KdTree.LazyKdTree tree -> 
                               let loadedTree = if File.Exists(tree.kdtreePath) then 
                                                    Some (tree.kdtreePath |> KdTrees.loadKdtree) 
                                                else None
                               (tree.boundingBox, (KdTrees.Level0KdTree.LazyKdTree {tree with kdTree = loadedTree}))
                        )

                let bb  = 
                    match meshes with
                    | [] -> 
                        Box3d.Invalid
                    | m::_ -> 
                        meshes |> List.fold (fun accum y -> Box3d.extendBy accum y.BoundingBox3d) m.BoundingBox3d

                let pose = Pose.translate (bb.Center) 
                let trafo = { TrafoController.initial with pose = pose; previewTrafo = Pose.toTrafo pose; mode = TrafoMode.Local }

                let sgs = createSceneGraph obj 

                let sg = 
                    sgs
                    //|> Sg.ofList
                    |> Sg.requirePicking
                    |> Sg.noEvents
                    |> Sg.scale 1.0

                {
                    surface         = surface.guid    
                    trafo           = trafo
                    globalBB        = bb
                    sceneGraph      = sg
                    picking         = Picking.KdTree(kdTrees |> HashMap.ofList)
                    isObj           = true
                    //transformation = Init.Transformations
                }
                 

            let createSgObjectsWavefront surfaces =
                let sghs =
                  surfaces
                    |> IndexList.toList 
                    |> List.filter(fun s ->
                        let dirExists = File.Exists s.importPath
                        if dirExists |> not then 
                            Log.error "[Surface.Sg] could not find %s" s.importPath
                        dirExists
                    )
                    |> List.map loadObjectWavefront

                let sgObjects =
                    sghs 
                      |> List.map (fun d -> (d.surface, d))
                      |> HashMap.ofList       

                sgObjects

        


    module SurfaceAttributes = 
        open System.Xml
            
        let get (name : string) (node : XmlNode)=
            node.SelectSingleNode(name).InnerText.Trim()
        
        let parseMap (index : int)(node : XmlNode) : ScalarLayer = 
            let definedRange = node |> get "ChannelsDefinedRange" |> Range1d.Parse
            { 
                version      = ScalarLayer.current
                label        = node |> get "Label" 
                actualRange  = node |> get "ChannelsActualRange"  |> Range1d.Parse
                definedRange = definedRange //node |> get "ChannelsDefinedRange" |> Range1d.Parse
                index        = index
                colorLegend  = (FalseColorsModel.initDefinedScalarsLegend definedRange)
            }
        
        let parseTexture (index : int)(node : XmlNode) : TextureLayer =
            { 
                version = TextureLayer.current
                label = node |> get "Label" 
                index = index
            }
        
        let parseLayer (index : int)(node : XmlNode) : AttributeLayer = 
            let typ = node |> get "Type"
            match typ with
                | "Texture" -> TextureLayer (parseTexture index node)
                | "Map"     -> ScalarLayer (parseMap index node)
                | _         -> failwith "type not supported"
        
        let layers (doc : XmlDocument) =
            let nodes = doc.SelectNodes "/Aardvark/SurfaceAttributes/AttributeLayers/AttributeLayer"
            nodes
             |> Seq.cast<XmlNode>
             |> Seq.mapi parseLayer
        
        let read (path:string) =
            let doc = new XmlDocument() in doc.Load path
            doc |> layers
                                            
module SurfaceApp =

    let hmapsingle (k,v) = HashMap.single k v    

    let updateSurfaceTrafos (trafos: list<SurfaceTrafo>) (model:SurfaceModel) =
        let surfaces =        
            model.surfaces.flat 
            |> HashMap.toList
            |> List.map(fun (_,v) -> 
              let s = (v |> Leaf.toSurface)
              s.name, s)
            |> HashMap.ofList
        
        let surfaces' = 
            trafos 
            |> List.filter (fun x -> x.id |> surfaces.ContainsKey)
            |> List.map(fun x -> HashMap.find x.id surfaces, x) // TODO to: non total! can be?
            |> List.map(fun (a,b) -> { a with preTransform = b.trafo })
              
        let flat' = 
             surfaces' 
            |> List.choose(fun x -> 
                let f = (fun _ -> x |> Leaf.Surfaces)
                Groups.updateLeaf' x.guid f model.surfaces 
                |> HashMap.tryFind x.guid 
                |> Option.map(fun leaf -> (x.guid,leaf) |> hmapsingle))          
            |> List.fold HashMap.union HashMap.empty
                      
        //write union of surface models ... union flat and recalculate all others
        //this does only update surfaces ... not kdtrees or sgs !!!!             
        { model with surfaces = { model.surfaces with flat = flat' } }

    let changeImportDirectories (model:SurfaceModel) (selectedPaths:list<string>) = 
        let surfacePaths = 
            selectedPaths
            |> List.map Files.superDiscoveryMultipleSurfaceFolder
            |> List.concat

        let surfaces =        
            model.surfaces.flat 
            |> HashMap.toList
            |> List.map(fun (_,v) -> 
                let s = (v |> Leaf.toSurface)
                let newPath = 
                    surfacePaths
                    |> List.map(fun p -> 
                        let name = p |> IO.Path.GetFileName
                        match name = s.name with
                        | true -> Some p
                        | false -> None
                    )
                    |> List.choose( fun np -> np) 
                match newPath.IsEmpty with
                | true -> s
                | false -> { s with importPath = newPath.Head } 
            )
              
        let flat' = 
            surfaces 
              |> List.choose(fun x -> 
                let f = (fun _ -> x |> Leaf.Surfaces)
                Groups.updateLeaf' x.guid f model.surfaces 
                  |> HashMap.tryFind x.guid 
                  |> Option.map(fun leaf -> (x.guid,leaf) |> hmapsingle))          
              |> List.fold HashMap.union HashMap.empty
              
        { model with surfaces = { model.surfaces with flat = flat' } }

    let changeOBJImportDirectories (model:SurfaceModel) (selectedPaths:list<string>) = 

        let surfaces =        
            model.surfaces.flat 
            |> HashMap.toList
            |> List.map(fun (_,v) -> 
                let s = (v |> Leaf.toSurface)
                let newPath = 
                    selectedPaths
                    |> List.map(fun p -> 
                        let name = p |> IO.Path.GetFileName
                        match name = s.name with
                        | true -> Some p
                        | false -> None
                    )
                    |> List.choose( fun np -> np) 
                match newPath.IsEmpty with
                | true -> s
                | false -> { s with importPath = newPath.Head } 
            )
              
        let flat' = 
            surfaces 
              |> List.choose(fun x -> 
                let f = (fun _ -> x |> Leaf.Surfaces)
                Groups.updateLeaf' x.guid f model.surfaces 
                  |> HashMap.tryFind x.guid 
                  |> Option.map(fun leaf -> (x.guid,leaf) |> hmapsingle))          
              |> List.fold HashMap.union HashMap.empty
              
        { model with surfaces = { model.surfaces with flat = flat' } }



    let update 
        (model     : SurfaceModel) 
        (action    : SurfaceAppAction) 
        (scenePath : option<string>) 
        (view      : CameraView) 
        (refSys    : ReferenceSystem) = 

        match action with
        | SurfacePropertiesMessage msg ->                
            let m = 
                match model.surfaces.singleSelectLeaf with
                | Some s -> 
                    let surface = model.surfaces.flat |> HashMap.find s |> Leaf.toSurface 
                    let s' = 
                        match msg with
                        | SurfaceProperties.Action.SetHomePosition -> { surface with homePosition = Some view }
                        | _ -> SurfaceProperties.update surface msg
                    
                    model |> SurfaceModel.updateSingleSurface s'                        
                | None -> model
            match msg with
            | SurfaceProperties.SetPriority _ -> m |> SurfaceModel.triggerSgGrouping 
            | _ -> m            
        | MakeRelative id ->                
            let model = Files.makeSurfaceRelative id model scenePath
            let sgSurfaces = model.sgSurfaces |> Files.expandLazyKdTreePaths scenePath (model.surfaces.flat |> Leaf.toSurfaces)
            { model with sgSurfaces = sgSurfaces}
        | RemoveSurface (id,path) -> //model
            let groups = GroupsApp.removeLeaf model.surfaces id path true
            let sg' = model.sgSurfaces |> HashMap.remove id
            { model with surfaces = groups; sgSurfaces = sg'} |> SurfaceModel.triggerSgGrouping              
        | RebuildKdTrees id ->                
            let surf = id |> SurfaceModel.getSurface model
            match surf with
            | Some (Leaf.Surfaces sf) ->                    
                let path = Files.getSurfaceFolder sf scenePath
                match path with
                | Some p ->                            
                    let path = @".\20170530_TextureConverter_single\Vrvis.TextureConverter.exe"
                    if File.Exists(path) then //\Vrvis.TextureConverter.exe") then
                        let mutable converter = new Process()
                        Log.line "start processing"                        
                        converter.StartInfo.FileName <- "Vrvis.TextureConverter.exe"
                        converter.StartInfo.Arguments <- p + " -overwrite" //-lazy"
                        converter.StartInfo.UseShellExecute <- true                          
                        converter.StartInfo.WorkingDirectory <- @".\20170530_TextureConverter_single"                                    
                        converter.Start() |> ignore    
                        converter.WaitForExit()
                           //)
                    else
                        Log.line "texture converter not found"                             
                    model
                | None -> model                         
            | _ -> model
        | ToggleActiveFlag id ->               
            let surf = id |> SurfaceModel.getSurface model
            match surf with
            | Some (Leaf.Surfaces s) ->
                { s with isActive = not s.isActive } |> SurfaceModel.updateSingleSurface' model
            | _ -> model
        | ChangeImportDirectory (id, dir) ->
            let surf = id |> SurfaceModel.getSurface model
            match surf with
            | Some (Leaf.Surfaces s) when (dir |> System.IO.Directory.Exists) && (dir |> Files.isSurfaceFolder) ->
                { s with importPath = dir } |> SurfaceModel.updateSingleSurface' model
            | _ -> model          
        | ChangeImportDirectories sl ->
            match sl with
            | [] -> model
            | paths ->
                let selectedPaths = paths |> List.choose Files.tryDirectoryExists
                changeImportDirectories model selectedPaths
        | ChangeOBJImportDirectories sl ->
            match sl with
            | [] -> model
            | paths ->
                let selectedPaths = paths |> List.choose( fun p -> if File.Exists p then Some p else None)
                changeOBJImportDirectories model selectedPaths    
        | GroupsMessage msg -> 
            let groups = GroupsApp.update model.surfaces msg

            match msg with
            | GroupsAppAction.RemoveGroup _ | GroupsAppAction.RemoveLeaf _ ->
                let sgs = 
                  model.sgSurfaces 
                      |> HashMap.filter(fun k _ -> groups.flat |> HashMap.containsKey k)

                { model with surfaces = groups; sgSurfaces = sgs } |> SurfaceModel.triggerSgGrouping                  
            | _ -> 
                { model with surfaces = groups }
        //| PickObject p -> model   
        | PlaceSurface p -> 
            match model.surfaces.singleSelectLeaf with
            | Some id -> 
                match model.surfaces.flat.TryFind(id) with 
                | Some s -> 
                    let trans = Trafo3d.Translation(p)
                    let s = s |> Leaf.toSurface
                    let f = (fun _ -> { s with preTransform = Trafo3d.Translation(p)  } |> Leaf.Surfaces)
                    let g = Groups.updateLeaf s.guid f model.surfaces
                    
                    let sgs' = 
                        model.sgSurfaces
                        |> HashMap.update id (fun x -> 
                            match x with 
                            | Some sg ->    
                                let pose = Pose.translate p // bb.Center
                                //let pose = { sg.trafo.pose with position = sg.trafo.pose.position + p }
                                
                                let trafo' = { 
                                  TrafoController.initial with 
                                    pose = pose
                                    previewTrafo = Trafo3d.Translation(p)
                                    mode = TrafoMode.Local 
                                }
                                { sg with trafo = trafo'; globalBB = (sg.globalBB.Transformed trafo'.previewTrafo) } // (Trafo3d.Translation(p))) }  //
                            | None   -> failwith "surface not found")                             
                    { model with surfaces = g; sgSurfaces = sgs'} 
                | None -> model
            | None -> model
        | ScalarsColorLegendMessage msg ->
            match model.surfaces.singleSelectLeaf with
            | Some s -> 
                let surface = model.surfaces.flat |> HashMap.find s |> Leaf.toSurface 
                match surface.selectedScalar with
                | Some s -> 
                    let sc = { s with colorLegend = (FalseColorLegendApp.update s.colorLegend msg) }                        
                    let scs = surface.scalarLayers |> HashMap.alter sc.index (Option.map(fun _ -> sc))
                    let s' = { surface with selectedScalar = Some sc; scalarLayers = scs }
                    model |> SurfaceModel.updateSingleSurface s'   
                | None -> model
            | None -> model
        | ColorCorrectionMessage msg ->       
            let m = 
                match model.surfaces.singleSelectLeaf with
                | Some s -> 
                    let surface = model.surfaces.flat |> HashMap.find s |> Leaf.toSurface
                    let s' = { surface with colorCorrection = (ColorCorrectionProperties.update surface.colorCorrection msg) }
                    model |> SurfaceModel.updateSingleSurface s'                        
                | None -> model
            m
        | SetPreTrafo str -> 
            //let m = 
            //    match model.surfaces.singleSelectLeaf, str.Length > 0 with
            //    | Some s, true -> 
            //        let surface = model.surfaces.flat |> HashMap.find s |> Leaf.toSurface
            //        let s' = { surface with preTransform = str |> Trafo3d.Parse}
            //        model |> SurfaceModel.updateSingleSurface s'             
            //    | _, _ -> model
            //m

            match model.surfaces.singleSelectLeaf with
            | Some id -> 
                match model.surfaces.flat.TryFind(id) with 
                | Some s -> 
                    let trafo = str |> Trafo3d.Parse
                    let s = s |> Leaf.toSurface
                    let f = (fun _ -> { s with preTransform = trafo  } |> Leaf.Surfaces)
                    let g = Groups.updateLeaf s.guid f model.surfaces
        
                    let sgs' = 
                        model.sgSurfaces
                        |> HashMap.update id (fun x -> 
                            match x with 
                            | Some sg ->    
                                { sg with globalBB = (sg.globalBB.Transformed trafo) } // (Trafo3d.Translation(p))) }  //
                            | None   -> failwith "surface not found")                             
                    { model with surfaces = g; sgSurfaces = sgs'} 
                | None -> model
            | None -> model

            //match model.surfaces.singleSelectLeaf with
            //| Some id -> 
            //    match model.surfaces.flat.TryFind(id) with 
            //    | Some s -> 
            //        let trafo = str |> Trafo3d.Parse
            //        let s = s |> Leaf.toSurface
            //        let f = (fun _ -> { s with preTransform = trafo  } |> Leaf.Surfaces)
            //        let g = Groups.updateLeaf s.guid f model.surfaces
        
            //        let sgs' = 
            //            model.sgSurfaces
            //            |> HashMap.update id (fun x -> 
            //                match x with 
            //                | Some sg ->    
            //                    let pose = Pose.transform Pose.identity trafo
            //                    let trafo' = { 
            //                      TrafoController.initial with 
            //                        pose = pose
            //                        previewTrafo = trafo
            //                        mode = TrafoMode.Local 
            //                    }
            //                    { sg with trafo = trafo'; globalBB = (sg.globalBB.Transformed trafo'.previewTrafo) } // (Trafo3d.Translation(p))) }  //
            //                | None   -> failwith "surface not found")                             
            //        { model with surfaces = g; sgSurfaces = sgs'} 
            //    | None -> model
            //| None -> model

        | TranslationMessage msg ->  
           
            let m = 
                //match model.surfaces.singleSelectLeaf with
                //    | Some id -> 
                //      match model.surfaces.flat.TryFind(id) with 
                //      | Some s -> 
                //         let surface = s |> Leaf.toSurface
                //         let s' = { surface with transformation = (TranslationApp.update surface.transformation msg) }
                //         let sgs' = 
                //             model.sgSurfaces
                //             |> HashMap.update id (fun x -> 
                //               match x with 
                //               | Some sg ->  
                //                let trafo' = { sg.trafo with previewTrafo = sg.trafo.previewTrafo * s'.transformation.trafo }
                //                { sg with trafo = trafo'; globalBB = (sg.globalBB.Transformed trafo'.previewTrafo) }
                //               | None   -> failwith "surface not found")                             
                //         let m' = { model with sgSurfaces = sgs'} 
                //         m' |> SurfaceModel.updateSingleSurface s'
                //      | None -> model
                //    | None -> model
                match model.surfaces.singleSelectLeaf with
                | Some s -> 
                    let surface = model.surfaces.flat |> HashMap.find s |> Leaf.toSurface
                    let t =  { surface.transformation with pivot = refSys.origin }
                    let transformation' = (TranslationApp.update t msg)
                    let s' = { surface with transformation = transformation' }
                    //let homePosition = 
                    //  match surface.homePosition with
                    //    | Some hp ->
                    //        let superTrafo = PRo3D.Transformations.fullTrafo' s' refSys
                    //        let trafo' = superTrafo.Forward
                    //        let pos = trafo'.TransformPos(hp.Location)
                    //        let forward = trafo'.TransformDir(hp.Forward)
                    //        let up = trafo'.TransformDir(hp.Up)
                    //        Some ( hp
                    //                 |> CameraView.withLocation pos
                    //                 |> CameraView.withForward forward )//(CameraView.lookAt pos forward hp.Up)
                    //    | None -> surface.homePosition
                    //let s'' = { s' with homePosition = homePosition }
                    
                    
                    //let sgs' = 
                    //      model.sgSurfaces
                    //      |> HashMap.update s (fun x -> 
                    //          match x with 
                    //          | Some sg ->  
                    //              let trafo' = { sg.trafo with previewTrafo = sg.trafo.previewTrafo * s'.transformation.trafo }
                    //              { sg with trafo = trafo'; globalBB = (sg.globalBB.Transformed trafo'.previewTrafo) }
                    //          | None   -> failwith "surface not found") 
                    //let m' = { model with sgSurfaces = sgs'} 
                    model |> SurfaceModel.updateSingleSurface s'            
                | None -> model
            m
        | _ -> model

    let absRelIcons (m : AdaptiveSurface)=
        adaptive {
            let! absRelIcons = 
                m.relativePaths 
                    |> AVal.map(fun x ->                                 
                        let icon = if x then "suitcase icon" else "cloud download icon"                                
                        AttributeMap.ofList [
                            clazz icon;                                                    
                            onClick (fun x -> MakeRelative (m.guid |> AVal.force))
                        ]         
                    )
            return absRelIcons
        }    

    let isSelected (model : AdaptiveGroupsModel) (s : AdaptiveSurface) =
        let id = s.guid |> AVal.force
            
        model.selectedLeaves
          |> ASet.map(fun x -> x.id = id)
          |> ASet.contains true
    
    let mkColor (model : AdaptiveGroupsModel) (s : AdaptiveSurface) =
        isSelected model s 
          |> AVal.bind (fun x -> if x then AVal.constant C4b.VRVisGreen else AVal.constant C4b.White)

    let lastSelected (model : AdaptiveGroupsModel) (s : AdaptiveSurface) =
            let id = s.guid |> AVal.force
            model.singleSelectLeaf 
                |> AVal.map( fun x -> match x with 
                                        | Some xx -> xx = id
                                        | _ -> false )

    let isSingleSelect (model : AdaptiveGroupsModel) (s : AdaptiveSurface) =
            model.singleSelectLeaf |> AVal.map( fun x -> 
                match x with 
                  | Some selected -> selected = (s.guid |> AVal.force)
                  | None -> false )

    let viewSurfacesInGroups 
        (scenePath : aval<Option<string>>)
        (path         : list<Index>) 
        (model        : AdaptiveGroupsModel) 
        (singleSelect : AdaptiveSurface*list<Index> -> SurfaceAppAction) 
        (multiSelect  : AdaptiveSurface*list<Index> -> SurfaceAppAction) 
        (lift         : GroupsAppAction -> SurfaceAppAction) 
        (surfaceIds   : alist<Guid>) : alist<DomNode<SurfaceAppAction>> =
        alist {
        
            let surfaces = 
                surfaceIds 
                |> AList.chooseA (fun x -> model.flat |> AMap.tryFind x)
            
            let surfaces = 
                surfaces
                |> AList.choose(function | AdaptiveSurfaces s -> Some s | _-> None )
            
            for s in surfaces do
            
                let singleSelect = fun _ -> singleSelect(s,path)
                let multiSelect = fun _ -> multiSelect(s,path)
                //let! selected = s |> isSingleSelect model
            
                let! c = mkColor model s
                let infoc = sprintf "color: %s" (Html.ofC4b C4b.White)
            
                let! key = s.guid                                                                             
                let! absRelIcons = absRelIcons s
            
                let visibleIcon = 
                    amap {
                        yield onMouseClick (fun _ -> lift <| GroupsAppAction.ToggleChildVisibility (key,path))
                        let! visible = s.isVisible
                        if visible then 
                            yield clazz "unhide icon" 
                        else 
                            yield clazz "hide icon"
                    } 
                    |> AttributeMap.ofAMap
               
                let headerColor = 
                    (isSingleSelect model s) 
                    |> AVal.map(fun x -> 
                        (if x then C4b.VRVisGreen else C4b.Gray) 
                        |> Html.ofC4b 
                        |> sprintf "color: %s"
                    ) 
            
               // let headerColor = sprintf "color: %s" (Html.ofC4b C4b.Gray)
                let headerAttributes =
                    amap {
                        yield onClick singleSelect
                        //let! selected = isSingleSelect model s
                        
                        //yield if selected then style "text-transform:uppercase" else style "text-transform:none"
                        //yield if selected then style bgc else style bgc
                    } 
                    |> AttributeMap.ofAMap
            
                let headerText = 
                    AVal.map2 (fun a b -> sprintf "%.0f|%s" a b) (s.priority.value) s.name


                let surfacePath = 
                    (s.Current, scenePath) ||> AVal.map2 Files.getSurfaceFolder

                let openFolderAttributes =
                    amap {
                        let! surfacePath = surfacePath
                        match surfacePath with
                        | None -> 
                            Log.warn "no surface path, disabling folder icon"
                            yield clazz "folder disabled icon"; 
                        | Some surfacePath -> 
                            let openFolderJs = Electron.openPath surfacePath
                            yield clientEvent "onclick" openFolderJs
                            yield clazz "folder icon"; 
                    } |> AttributeMap.ofAMap


                //[clientEvent "onclick" (sprintf "aardvark.electron.shell.showItemInFolder('%s');" (Helpers.escape path)) ] <---- this is the way to go for "reveal in explorer/finder"
            
                let bgc = sprintf "color: %s" (Html.ofC4b c)
                yield div [clazz "item"; style infoc] [
                    i [clazz "cube middle aligned icon"; onClick multiSelect;style bgc] [] 
                    div [clazz "content"; style infoc] [                     
                        yield Incremental.div (AttributeMap.ofList [style infoc])(
                            alist {
                                let! hc = headerColor
                                yield div[clazz "header"; style hc][
                                   Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                                ]                             
            
                                yield i [clazz "home icon"; onClick (fun _ -> FlyToSurface key)] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "Fly to surface"                                                     
            
                                yield Incremental.i openFolderAttributes AList.empty
                                    |> UI.wrapToolTip DataPosition.Bottom "Open Folder"                             
            
                                //yield Incremental.i (absRelIcons) (AList.empty)
                                yield Incremental.i visibleIcon AList.empty 
                                |> UI.wrapToolTip DataPosition.Bottom "Toggle Visible"

                                yield GuiEx.iconCheckBox s.isActive (ToggleActiveFlag key) 
                                |> UI.wrapToolTip DataPosition.Bottom "Toggle IsActive"

                                yield i [clazz "sync icon"; onClick (fun _ -> RebuildKdTrees key)] [] 
                                    |> UI.wrapToolTip DataPosition.Bottom "rebuild kdTree"           
            
                                let! path = s.importPath
                                let isobj = Path.GetExtension path = ".obj"
                                //
                                //  yield i 
                                if (((Directory.Exists path) |> not || (path |> Files.isSurfaceFolder |> not)) && (isobj |> not)) || ( isobj && (File.Exists path) |> not ) then
                                    yield i [
                                        clazz "exclamation red icon"
                                        //Dialogs.onChooseDirectory key ChangeImportDirectory;
                                        ////clientEvent "onclick" ("""
                                        ////   var files = parent.aardvark.dialog.showOpenDialog({properties: ['openDirectory','multiSelections']});
                                        ////   console.log(files);
                                        ////   parent.aardvark.processEvent('__ID__', 'onchoose', files);
                                        ////   """)
                                        //clientEvent "onclick" ("parent.aardvark.processEvent('__ID__', 'onchoose', parent.aardvark.dialog.showOpenDialog({properties: ['openDirectory','multiSelections']}));")
                                    ] []
                            } 
                        )                                     
                    ]
                ]
        }
           
    let rec viewTree (scenePath : aval<Option<string>>) path (group : AdaptiveNode) (model : AdaptiveGroupsModel) : alist<DomNode<SurfaceAppAction>> =

        alist {

            let! s = model.activeGroup
            let color = sprintf "color: %s" (Html.ofC4b C4b.White)                
            let children = AList.collecti (fun i v -> viewTree scenePath (i::path) v model) group.subNodes    
            let activeAttributes = GroupsApp.setActiveGroupAttributeMap path model group GroupsMessage
                                   
            let toggleIcon = 
                AVal.constant "unhide icon" //group.visible |> AVal.map(fun toggle -> if toggle then "unhide icon" else "hide icon")                

            let toggleAttributes = GroupsApp.clickIconAttributes toggleIcon (GroupsMessage(GroupsAppAction.ToggleGroup path))
               
            let desc =
                div [style color] [       
                    Incremental.text group.name
                    Incremental.i activeAttributes AList.empty 
                    |> UI.wrapToolTip DataPosition.Bottom "Set active"
                        
                    i [clazz "plus icon"
                       onMouseClick (fun _ -> 
                         GroupsMessage(GroupsAppAction.AddGroup path))] []
                    |> UI.wrapToolTip DataPosition.Bottom "Add Group"           

                    Incremental.i toggleAttributes AList.empty 
                    |> UI.wrapToolTip DataPosition.Bottom "Toggle Group"
                   // GuiEx.iconCheckBox group.visible (GroupsMessage(Groups.ToggleGroup path))
                ]
                 
            let itemAttributes =
                amap {
                    yield onMouseClick (fun _ -> SurfaceAppAction.GroupsMessage(GroupsAppAction.ToggleExpand path))
                    let! selected = group.expanded
                    if selected 
                    then yield clazz "icon outline open folder"
                    else yield clazz "icon outline folder"
                    yield style "overflow-y : visible"
                } |> AttributeMap.ofAMap
            
            let childrenAttribs =
                amap {
                    yield clazz "list"
                    let! isExpanded = group.expanded
                    if isExpanded then yield style "visible"
                    else yield style "hidden"
                }         

            let singleSelect = 
                    fun (s:AdaptiveSurface,path:list<Index>) -> 
                        SurfaceAppAction.GroupsMessage(GroupsAppAction.SingleSelectLeaf (path, s.guid |> AVal.force, ""))

            let multiSelect = 
                fun (s:AdaptiveSurface,path:list<Index>) -> 
                    SurfaceAppAction.GroupsMessage(GroupsAppAction.AddLeafToSelection (path, s.guid |> AVal.force, ""))

            let lift = fun (a:GroupsAppAction) -> (GroupsMessage a)

            yield div [ clazz "item"] [
                Incremental.i itemAttributes AList.empty
                div [ clazz "content" ] [                         
                    div [ clazz "description noselect"] [desc]
                    Incremental.div (AttributeMap.ofAMap childrenAttribs) (                          
                        alist { 
                            let! isExpanded = group.expanded
                            if isExpanded then yield! children
                            
                            if isExpanded then 
                                yield! viewSurfacesInGroups scenePath path model singleSelect multiSelect lift group.leaves
                        }
                    )  
                            
                ]
            ]
                
        }


    let viewSurfacesGroups (scenePath : aval<Option<string>>) (model:AdaptiveSurfaceModel) = 
        require GuiEx.semui (
            Incremental.div 
              (AttributeMap.ofList [clazz "ui celled list"]) 
              (viewTree scenePath [] model.surfaces.rootGroup model.surfaces)            
        )    
        

    //let viewSurfaceProperties (model:AdaptiveSurfaceModel) =
    //    adaptive {
    //        let! bla = model.surfaces.lastSelectedChild
    //        let opt = 
    //            match bla with
    //                | Some s -> SurfaceProperties.view s
    //                | None   -> div[][]

    //        return (opt |> UI.map SurfacePropertiesMessage)
    //    }    

   // let noSurfaceSelected = div[ style "font-style:italic"][ text "no surface selected" ]

    let lscToDom lsc isSome = 
        adaptive {
            match lsc with
                | Some b ->   
                    let! b' = b
                    match b' with 
                        | AdaptiveSurfaces bm -> return isSome bm
                        | _ -> return div[][]                                                             
                | None   -> return div[][] 
        }
    
      //failwith ""

    let viewSurfaceProperties (model:AdaptiveSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            let flat = model.surfaces.flat
            
            match guid with
              | Some i ->
                let! exists = flat |> AMap.keys |> ASet.contains i
                if exists then
                  let leaf = flat |> AMap.find i 
                  let! surf = leaf 
                  let x = match surf with | AdaptiveSurfaces s -> s | _ -> leaf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith                                     
                  return SurfaceProperties.view x |> UI.map SurfacePropertiesMessage
                else
                  return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map SurfacePropertiesMessage 
                  
              
              | None -> return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map SurfacePropertiesMessage 
        }                          
        
    let surfaceGroupProperties (model:AdaptiveSurfaceModel) =
        adaptive {                                
            return (GroupsApp.viewUI model.surfaces|> UI.map GroupsMessage)
        } 

    let viewTranslationTools (model:AdaptiveSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            let empty = div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map TranslationMessage 

            match guid with
              | Some i -> 
                let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                if exists then
                  let leaf = model.surfaces.flat |> AMap.find i 
                  let! surf = leaf 
                  let x = match surf with | AdaptiveSurfaces s -> s | _ -> leaf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
                  return TranslationApp.UI.view x.transformation |> UI.map TranslationMessage
                else
                  return empty
              | None -> return empty
        }                          

    let viewColorCorrectionTools (paletteFile : string) (model:AdaptiveSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            let empty = div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map ColorCorrectionMessage 
            
            match guid with
                | Some i -> 
                  let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                  if exists then
                    let leaf = model.surfaces.flat |> AMap.find i // TODO to: common - make a map here!
                    let! surf = leaf 
                    let colorCorrection = match surf with | AdaptiveSurfaces s -> s.colorCorrection | _ -> leaf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
                    return ColorCorrectionProperties.view paletteFile colorCorrection |> UI.map ColorCorrectionMessage
                  else 
                    return empty
                | None -> return empty 
        }                          

    //TODO LF refactor and simplify, use option.map, bind, default value as described in
    //https://hackmd.io/C3putqB_QNCwpxWO_oKZJQ#Working-with-Optionmap-Optionbind-and-OptiondefaultValue
    let viewColorLegendTools (colorPaletteStore : string) (model:AdaptiveSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            
            match guid with
                | Some i -> 
                    let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                    if exists then
                      let leaf = model.surfaces.flat |> AMap.find i
                      let! surf = leaf 
                      let scalar = 
                        match surf with 
                         | AdaptiveSurfaces s -> s.selectedScalar 
                         | _ -> leaf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
                      
                      let! scalar = scalar
                      match AdaptiveOption.toOption scalar with // why is AdaptiveSome here not available
                          | Some s -> return FalseColorLegendApp.UI.viewScalarMappingProperties colorPaletteStore s.colorLegend |> UI.map ScalarsColorLegendMessage
                          | None -> return div[ style "font-style:italic"][ text "no scalar in properties selected" ] |> UI.map ScalarsColorLegendMessage 
                    else
                      return div[ style "font-style:italic"][ text "no scalar in properties selected" ] |> UI.map ScalarsColorLegendMessage 
                                  
                | None -> return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map ScalarsColorLegendMessage 
        }                          

    //TODO LF refactor and simplify, use option.map, bind, default value as described in
    //https://hackmd.io/C3putqB_QNCwpxWO_oKZJQ#Working-with-Optionmap-Optionbind-and-OptiondefaultValue
    let showColorLegend (model:AdaptiveSurfaceModel) = 
        alist {
            let! guid = model.surfaces.singleSelectLeaf            
            match guid with
            | Some i -> 
                let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                if exists then
                    let leaf = model.surfaces.flat |> AMap.find i // TODO v5: common be total
                    let! surf = leaf 
                    let scalar = 
                        match surf with 
                        | AdaptiveSurfaces s -> s.selectedScalar 
                        | _ -> leaf |> sprintf "wrong type %A; expected AdaptiveSurfaces" |> failwith
                    let! scalar = scalar
                    match AdaptiveOption.toOption scalar with
                    | Some s ->  
                        yield Incremental.Svg.svg AttributeMap.empty (FalseColorLegendApp.Draw.createFalseColorLegendBasics "ScalarLegend" s.colorLegend)
                    | None -> yield div[][]
                else
                    yield div[][]
            | None -> yield div[][]
        } 

    let surfacesLeafButtonns (model:AdaptiveSurfaceModel) = 
        let ts = model.surfaces.activeChild
        let sel = model.surfaces.singleSelectLeaf
        adaptive {  
            let! ts = ts
            let! sel = sel
            match sel with
            | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map GroupsMessage)
            | None -> return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map GroupsMessage
        } 

    let surfacesGroupButtons (model:AdaptiveSurfaceModel) = 
        let ts = model.surfaces.activeGroup
        adaptive {  
            let! ts = ts
            return (GroupsApp.viewGroupButtons ts |> UI.map GroupsMessage)
        } 
    
    let surfaceUI (scenePath : aval<Option<string>>) (colorPaletteStore : string) (model:AdaptiveSurfaceModel) =
        let item2 = 
            model.surfaces.lastSelectedItem 
                |> AVal.bind (fun x -> 
                    match x with 
                        | SelectedItem.Group -> surfaceGroupProperties model
                        | _ -> viewSurfaceProperties model
                )
        let buttons = 
            model.surfaces.lastSelectedItem 
                |> AVal.bind (fun x -> 
                    match x with 
                        | SelectedItem.Group -> surfacesGroupButtons model
                        | _ -> surfacesLeafButtonns model
                )
        div[][                            
            yield GuiEx.accordion "Surfaces" "Cubes" true [ viewSurfacesGroups scenePath model ]
            yield GuiEx.accordion "Properties" "Content" false [
              Incremental.div AttributeMap.empty (AList.ofAValSingle item2)
               
            ]
             
            yield GuiEx.accordion "Transformation" "expand arrows alternate " false [
                Incremental.div AttributeMap.empty (AList.ofAValSingle(viewTranslationTools model))
                //div [] [Html.SemUi.textBox model.debugPreTrafo SetPreTrafo] // debug PreTrafo //  Bug: pretrafo ignored when picking annotation etc.
            ]  
                
            yield GuiEx.accordion "Color Adaptation" "file image outline" false [
                Incremental.div AttributeMap.empty (AList.ofAValSingle(viewColorCorrectionTools colorPaletteStore model))
            ] 

            yield GuiEx.accordion "Scalars ColorLegend" "paint brush" true [
                Incremental.div AttributeMap.empty (AList.ofAValSingle(viewColorLegendTools colorPaletteStore model))
            ] 

            yield GuiEx.accordion "Actions" "Asterisk" false [
                Incremental.div AttributeMap.empty (AList.ofAValSingle (buttons))
            ] 
        ]
    
    

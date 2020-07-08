namespace PRo3D.Surfaces

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Base.Rendering
open Aardvark.SceneGraph
open Aardvark.SceneGraph.IO
open Aardvark.SceneGraph.Opc

open Aardvark.VRVis.Opc

open Aardvark.UI.Operators
open Aardvark.UI.Trafos  

open PRo3D.Base
open PRo3D
open PRo3D.Groups
open System.Diagnostics

module SurfaceUtils =
    open Aardvark.SceneGraph.SgFSharp.Sg   
    open System.Diagnostics 
    
    /// creates a surface from opc folder path
    let mk (stype:SurfaceType) (maxTriangleSize : float) path =                 
    
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
            scalarLayers    = hmap.Empty //IndexList.empty
            selectedScalar  = None
            textureLayers   = IndexList.empty
            selectedTexture = None            
    
            triangleSize    = { Init.triangleSize with value = maxTriangleSize }
            surfaceType     = stype
    
            colorCorrection = Init.initColorCorrection
            homePosition    = None
            transformation  = Init.transformations
        }       
   

    module ObjectFiles =        
        open Aardvark.Geometry
        
        //TODO TO use loadObject from master
        let loadObject (surface : PRo3D.Surfaces.Surface) : SgSurface =                        
            Log.line "[OBJ] Please wait while the OBJ file is being loaded." 
            let obj = Loader.Assimp.load (surface.importPath)  
            Log.line "[OBJ] The OBJ file was loaded successfully!" 
            let dir = Path.GetDirectoryName(surface.importPath)
            let filename = Path.GetFileNameWithoutExtension surface.importPath
            let kdTreePath = dir + "\\" + filename + ".aakd" //Path.ChangeExtension(s.importPath, name)
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
                            |> Seq.filter(fun x -> (Aardvark.VRVis.Opc.KdTrees.triangleIsNan x |> not)) |> Seq.toArray
                            |> TriangleSet
                                              
                        Log.startTimed "Building kdtrees for %s" (Path.GetFileName surface.importPath |> Path.GetFileName)
                        let tree = 
                            KdIntersectionTree(t, 
                                KdIntersectionTree.BuildFlags.MediumIntersection + KdIntersectionTree.BuildFlags.Hierarchical) //|> PRo3D.Serialization.save kdTreePath                  
                        Log.stop()
                        
                        //saveKdTree (kdPath, tree) |> ignore   // CHECK-merge
                        
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
                //transformation = Init.Transformations
            }
                 
        let createSgObjects _ _ surfaces =
            let sghs =
              surfaces
                |> IndexList.toList 
                |> List.map loadObject

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


    
    type Action =
        | SurfacePropertiesMessage  of SurfaceProperties.Action
        | FlyToSurface              of Guid
        | MakeRelative              of Guid
        | RemoveSurface             of Guid*list<Index>
        | PickSurface               of SceneHit*string
        | OpenFolder                of Guid
        | RebuildKdTrees            of Guid
        | ToggleActiveFlag          of Guid
        | ChangeImportDirectory     of Guid*string
        | ChangeImportDirectories   of list<string>
        | GroupsMessage             of GroupsAppAction
        //| PickObject                of V3d
        | PlaceSurface              of V3d
        | ScalarsColorLegendMessage of FalseColorLegendApp.Action
        | ColorCorrectionMessage    of ColorCorrectionProperties.Action
        | SetHomePosition           
        | TranslationMessage        of TranslationApp.Action
        
    let doKdTreeIntersection 
        (m : SurfaceModel) 
        (refSys : PRo3D.ReferenceSystem.ReferenceSystem) 
        (r : FastRay3d) 
        (filterSurface : Guid -> Leaf -> SgSurface -> bool) 
        cache
        : option<float * PRo3D.Surfaces.Surface> * HashMap<_,_> = 

        let mutable cache = cache
        let activeSgSurfaces = 
            m.surfaces.flat
            |> HashMap.toList 
            |> List.choose (fun (id,leaf) -> 
                   match m.sgSurfaces |> HashMap.tryFind id with
                   | None -> None
                   | Some s -> 
                       if filterSurface id leaf s then Some s
                       else None
               )
    
        let hits = 
            activeSgSurfaces 
            |> List.map (fun d -> d.picking, (m.surfaces.flat |> HashMap.find d.surface) |> Leaf.toSurface)                      
            |> List.choose (
               fun (p ,surf) ->
                 match p with
                    | Picking.KdTree kd ->
                      if kd.IsEmpty then Log.error "no kdtree loaded for %s" surf.name; None
                      else                    
                        let superTrafo = PRo3D.Transformations.fullTrafo' surf refSys
                        //get bbs that are hit
                        let hitBoxes =
                          kd  
                            |> HashMap.toList |> List.map fst
                            |> List.filter(
                                fun x -> 
                                   let mutable t = 0.0
                                   let r' = r.Ray //combine pre and current transform
                                   x.Transformed(surf.preTransform.Forward * superTrafo.Forward).Intersects(r', &t)
                            )
                        //intersect corresponding kdtrees
                        let closestHit =
                          hitBoxes
                            |> List.choose(
                                fun key -> 
                                    //Log.line "intersection: %s; pr: %f" surf.name surf.priority.value                                                             
                                    //let ray = r.Ray.Transformed(surf.preTransform.Backward) |> FastRay3d  //combine pre and current transform                     
                                    let ray = r.Ray.Transformed(surf.preTransform.Backward * superTrafo.Backward) |> FastRay3d
                                    let hit, c = 
                                      kd |> DebugKdTreesX.intersectKdTrees key surf cache ray
                                    cache <- c
                                    hit
                                    )
                            |> List.sortBy(fun (t,_)-> t)
                            |> List.tryHead
                      
                        closestHit
                    | Picking.PickMesh pm -> Log.error "no kdtree loaded for %s" surf.name; None
               )
            |> List.groupBy(fun (_,surf) -> surf.priority.value) |> List.sortByDescending fst
            |> List.tryHead
            |> Option.bind(fun (_,b) -> b |> List.sortBy fst |> List.tryHead)
            //|> List.sortBy(fun (t,_) -> t)
            //|> List.tryPick(fun d -> Some d)
    
        hits, cache

    //let doKdTreeIntersection (m : SurfaceModel) (r : FastRay3d) cache : option<float * PRo3D.Surfaces.Surface> * HashMap<_,_> = 
    //  doKdTreeIntersection' m Trafo3d.Identity r cache 

    let updateTrafo (trafo:SurfaceTrafo) (surfaces:HashMap<string,PRo3D.Surfaces.Surface>) (model:SurfaceModel) =                                
      match surfaces.TryFind(trafo.id) with
        | Some s -> 
          let f = (fun _ -> { s with preTransform = trafo.trafo } |> Leaf.Surfaces)
          let g = Groups.updateLeaf s.guid f model.surfaces
          Log.line "MeasurementImporter: matched and updated %s" s.name
          { model with surfaces = g} 
        | None -> model
                 
    let updateTrafos (trafos:IndexList<SurfaceTrafo>) (model:SurfaceModel) =
      let surfaces = 
        model.surfaces.flat 
          |> HashMap.toList 
          |> List.map(fun (_,v) -> 
             let surf = v |> Leaf.toSurface
             (surf.name, surf))
          |> HashMap.ofList
      
      let rec update (p : list<SurfaceTrafo>) (model:SurfaceModel)  =
          match p with
            | x::rest -> 
              match rest with
                | [] -> updateTrafo x surfaces model
                | _ ->  update rest (updateTrafo x surfaces model) 
            | _ -> model
      update (trafos |> IndexList.toList) model

    //let union (l : SurfaceModel) (r : SurfaceModel) : SurfaceModel =
    //  let l' = { l with 

    //  l

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
          |> List.map(fun x -> surfaces.Find x.id,x)
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
                        |> List.map( fun p -> 
                                        let name = p |> IO.Path.GetFileName
                                        match name = s.name with
                                         | true -> Some p
                                         | false -> None)
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

   
    let update (model:SurfaceModel) (action:Action) (scenePath : option<string>) (view:CameraView) (refSys : PRo3D.ReferenceSystem.ReferenceSystem) = 
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
        | OpenFolder id ->
            let surf = id |> SurfaceModel.getSurface model
            match surf with
              | Some s -> 
                match s with 
                  | Leaf.Surfaces sf ->
                    let path = Files.getSurfaceFolder sf scenePath
                    match path with
                      | Some p -> 
                          Process.Start("explorer.exe", p) |> ignore
                          model
                      | None -> model
                  | _ -> failwith "can only contain surfaces"
              | None -> model
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
            
        | GroupsMessage msg -> 
            let groups = GroupsApp.update model.surfaces msg

            match msg with
              | GroupsAppAction.RemoveGroup _ | GroupsAppAction.RemoveLeaf _ ->
                let sgs = 
                    model.sgSurfaces 
                        |> HashMap.filter(fun k _ -> groups.flat |> HashMap.containsKey k)

                { model with surfaces = groups; sgSurfaces = sgs } |> SurfaceModel.triggerSgGrouping                  
              | _ -> { model with surfaces = groups }
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

    let absRelIcons (m : MSurface)=
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

    let isSelected (model : MGroupsModel) (s : MSurface) =
        let id = s.guid |> AVal.force
            
        model.selectedLeaves
          |> ASet.map(fun x -> x.id = id)
          |> ASet.contains true
    
    let mkColor (model : MGroupsModel) (s : MSurface) =
        isSelected model s 
          |> AVal.bind (fun x -> if x then AVal.constant C4b.VRVisGreen else AVal.constant C4b.White)

    let lastSelected (model : MGroupsModel) (s : MSurface) =
            let id = s.guid |> AVal.force
            model.singleSelectLeaf 
                |> AVal.map( fun x -> match x with 
                                        | Some xx -> xx = id
                                        | _ -> false )

    let isSingleSelect (model : MGroupsModel) (s : MSurface) =
            model.singleSelectLeaf |> AVal.map( fun x -> 
                match x with 
                  | Some selected -> selected = (s.guid |> AVal.force)
                  | None -> false )

    let viewSurfacesInGroups 
       (path         : list<Index>) 
       (model        : MGroupsModel) 
       (singleSelect : MSurface*list<Index> -> 'outer) 
       (multiSelect  : MSurface*list<Index> -> 'outer) 
       (lift         : GroupsAppAction -> 'outer) 
       (surfaceIds   : alist<Guid>) : alist<DomNode<'outer>> =
       alist {

         let surfaces = 
            surfaceIds 
               |> AList.chooseM (fun x -> model.flat |> AMap.tryFind x)

         let surfaces = 
            surfaces
               |> AList.map AVal.force
               |> AList.choose(function | MSurfaces s -> Some s | _-> None )

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
                  yield onMouseClick (fun _ -> lift <| Groups.ToggleChildVisibility (key,path))
                  let! visible = s.isVisible
                  if visible then 
                    yield clazz "unhide icon" 
                  else 
                    yield clazz "hide icon"
                } |> AttributeMap.ofAMap
            
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
                } |> AttributeMap.ofAMap

             let headerText = AVal.map2 (fun a b -> sprintf "%.0f|%s" a b) (s.priority.value) s.name

             let bgc = sprintf "color: %s" (Html.ofC4b c)
             yield div [clazz "item"; style infoc] [
                 i [clazz "cube middle aligned icon"; onClick multiSelect;style bgc][] 
                 div [clazz "content"; style infoc] [                     
                     yield Incremental.div (AttributeMap.ofList [style infoc])(
                         alist {
                             let! hc = headerColor
                             yield div[clazz "header"; style hc][
                                Incremental.span headerAttributes ([Incremental.text headerText] |> AList.ofList)
                             ]
                             //if selected then
                             //    yield div[onClick singleSelect][ u[][Incremental.text desc]]
                             //else
                             //    yield div[onClick singleSelect][Incremental.text desc]
 
                             yield i [clazz "home icon"                                                
                                      onClick (fun _ -> FlyToSurface key) ][] |> UI.wrapToolTipBottom "Fly to surface"                                                     

                             yield i [clazz "folder icon"                                                
                                      onClick (fun _ -> OpenFolder key) ][] |> UI.wrapToolTipBottom "Open Folder"

                             //yield i [
                             //  clazz "microchip icon"
                             //  onClick (fun _ -> RebuildKdTrees key)
                             //][] |> UI.wrapToolTip "process kdtrees"

                             yield Incremental.i (absRelIcons) (AList.empty)
                             yield Incremental.i visibleIcon AList.empty |> UI.wrapToolTipBottom "Toggle Visible"
                             yield GuiEx.iconCheckBox s.isActive (ToggleActiveFlag key) |> UI.wrapToolTipBottom "Toggle IsActive"

                             let! path = s.importPath
                             //
                             //  yield i 
                             if (Directory.Exists path) |> not || (path |> Files.isSurfaceFolder |> not) then
                               yield i [
                                 clazz "exclamation red icon"
                                 //Dialogs.onChooseDirectory key ChangeImportDirectory;
                                 ////clientEvent "onclick" ("""
                                 ////   var files = parent.aardvark.dialog.showOpenDialog({properties: ['openDirectory','multiSelections']});
                                 ////   console.log(files);
                                 ////   parent.aardvark.processEvent('__ID__', 'onchoose', files);
                                 ////   """)
                                 //clientEvent "onclick" ("parent.aardvark.processEvent('__ID__', 'onchoose', parent.aardvark.dialog.showOpenDialog({properties: ['openDirectory','multiSelections']}));")
                                 ]
                                 []
                         } 
                     )                                     
                 ]
             ]
       }
           
    let rec viewTree path (group : MNode) (model : MGroupsModel) : alist<DomNode<Action>> =

        alist {

            let! s = model.activeGroup
            let color = sprintf "color: %s" (Html.ofC4b C4b.White)                
            let children = AList.collecti (fun i v -> viewTree (i::path) v model) group.subNodes    

            let activeIcon =
                adaptive {                    
                    let! group  =  group.key
                    return if (s.id = group) then "circle icon" else "circle thin icon"
                }

            let setActive = GroupsAppAction.SetActiveGroup (group.key |> AVal.force, path, group.name |> AVal.force)
            let activeAttributes = 
                GroupsApp.clickIconAttributes activeIcon (GroupsMessage setActive)
                                   
            let toggleIcon = AVal.constant "unhide icon" //group.visible |> AVal.map(fun toggle -> if toggle then "unhide icon" else "hide icon")                

            let toggleAttributes = GroupsApp.clickIconAttributes toggleIcon (GroupsMessage(GroupsAppAction.ToggleGroup path))
               
            let desc =
                div [style color] [       
                    Incremental.text group.name
                    Incremental.i activeAttributes AList.empty |> UI.wrapToolTipBottom "Set active"
                        
                    i [clazz "plus icon"
                       onMouseClick (fun _ -> 
                         GroupsMessage(GroupsAppAction.AddGroup path))] []
                    |> UI.wrapToolTip "Add Group" TTAlignment.Bottom                         

                    Incremental.i toggleAttributes AList.empty |> UI.wrapToolTipBottom "Toggle Group"
                   // GuiEx.iconCheckBox group.visible (GroupsMessage(Groups.ToggleGroup path))
                ]
                 
            let itemAttributes =
                amap {
                    yield onMouseClick (fun _ -> Action.GroupsMessage(GroupsAppAction.ToggleExpand path))
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
                    fun (s:MSurface,path:list<Index>) -> 
                        Action.GroupsMessage(GroupsAppAction.SingleSelectLeaf (path, s.guid |> AVal.force, ""))

            let multiSelect = 
                fun (s:MSurface,path:list<Index>) -> 
                    Action.GroupsMessage(GroupsAppAction.AddLeafToSelection (path, s.guid |> AVal.force, ""))

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
                                    yield! viewSurfacesInGroups path model singleSelect multiSelect lift group.leaves
                            }
                        )  
                                
                    ]
            ]
                
        }


    let viewSurfacesGroups (model:MSurfaceModel) = 
        require GuiEx.semui (
            Incremental.div 
              (AttributeMap.ofList [clazz "ui celled list"]) 
              (viewTree [] model.surfaces.rootGroup model.surfaces)            
        )    
        

    //let viewSurfaceProperties (model:MSurfaceModel) =
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
                        | MSurfaces bm -> return isSome bm
                        | _ -> return div[][]                                                             
                | None   -> return div[][] 
        }
    
      //failwith ""

    let viewSurfaceProperties (model:MSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            let flat = model.surfaces.flat
            
            match guid with
              | Some i ->
                let! exists = flat |> AMap.keys |> ASet.contains i
                if exists then
                  let leaf = flat |> AMap.find i |> AVal.bind(id)
                  let! surf = leaf 
                  let x = match surf with | MSurfaces s -> s | _ -> leaf |> sprintf "wrong type %A; expected MSurfaces" |> failwith                                     
                  return SurfaceProperties.view x |> UI.map SurfacePropertiesMessage
                else
                  return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map SurfacePropertiesMessage 
                  
              
              | None -> return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map SurfacePropertiesMessage 
        }                          
        
    let surfaceGroupProperties (model:MSurfaceModel) =
        adaptive {                                
            return (GroupsApp.viewUI model.surfaces|> UI.map GroupsMessage)
        } 

    let viewTranslationTools (model:MSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            let empty = div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map TranslationMessage 

            match guid with
              | Some i -> 
                let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                if exists then
                  let leaf = model.surfaces.flat |> AMap.find i |> AVal.bind(id)
                  let! surf = leaf 
                  let x = match surf with | MSurfaces s -> s | _ -> leaf |> sprintf "wrong type %A; expected MSurfaces" |> failwith
                  return TranslationApp.UI.view x.transformation |> UI.map TranslationMessage
                else
                  return empty
              | None -> return empty
        }                          

    let viewColorCorrectionTools (model:MSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            let empty = div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map ColorCorrectionMessage 
            
            match guid with
                | Some i -> 
                  let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                  if exists then
                    let leaf = model.surfaces.flat |> AMap.find i |> AVal.bind(id)
                    let! surf = leaf 
                    let colorCorrection = match surf with | MSurfaces s -> s.colorCorrection | _ -> leaf |> sprintf "wrong type %A; expected MSurfaces" |> failwith
                    return ColorCorrectionProperties.view colorCorrection |> UI.map ColorCorrectionMessage
                  else 
                    return empty
                | None -> return empty 
        }                          

    //TODO LF refactor and simplify, use option.map, bind, default value as described in
    //https://hackmd.io/C3putqB_QNCwpxWO_oKZJQ#Working-with-Optionmap-Optionbind-and-OptiondefaultValue
    let viewColorLegendTools (model:MSurfaceModel) =
        adaptive {
            let! guid = model.surfaces.singleSelectLeaf
            
            match guid with
                | Some i -> 
                    let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                    if exists then
                      let leaf = model.surfaces.flat |> AMap.find i |> AVal.bind(id)
                      let! surf = leaf 
                      let scalar = 
                        match surf with 
                         | MSurfaces s -> s.selectedScalar 
                         | _ -> leaf |> sprintf "wrong type %A; expected MSurfaces" |> failwith
                      
                      let! scalar = scalar
                      match scalar with
                          | Some s -> return FalseColorLegendApp.UI.viewScalarMappingProperties s.colorLegend |> UI.map ScalarsColorLegendMessage
                          | None -> return div[ style "font-style:italic"][ text "no scalar in properties selected" ] |> UI.map ScalarsColorLegendMessage 
                    else
                      return div[ style "font-style:italic"][ text "no scalar in properties selected" ] |> UI.map ScalarsColorLegendMessage 
                                  
                | None -> return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map ScalarsColorLegendMessage 
        }                          

    //TODO LF refactor and simplify, use option.map, bind, default value as described in
    //https://hackmd.io/C3putqB_QNCwpxWO_oKZJQ#Working-with-Optionmap-Optionbind-and-OptiondefaultValue
    let showColorLegend (model:MSurfaceModel) = 
        alist {
            let! guid = model.surfaces.singleSelectLeaf            
            match guid with
            | Some i -> 
                let! exists = (model.surfaces.flat |> AMap.keys) |> ASet.contains i
                if exists then
                    let leaf = model.surfaces.flat |> AMap.find i |> AVal.bind(id)
                    let! surf = leaf 
                    let scalar = 
                        match surf with 
                        | MSurfaces s -> s.selectedScalar 
                        | _ -> leaf |> sprintf "wrong type %A; expected MSurfaces" |> failwith
                    let! scalar = scalar
                    match scalar with
                    | Some s ->  
                        yield Incremental.Svg.svg AttributeMap.empty (FalseColorLegendApp.Draw.createFalseColorLegendBasics "ScalarLegend" s.colorLegend)
                    | None -> yield div[][]
                else
                    yield div[][]
            | None -> yield div[][]
        } 

    let surfacesLeafButtonns (model:MSurfaceModel) = 
        let ts = model.surfaces.activeChild
        let sel = model.surfaces.singleSelectLeaf
        adaptive {  
            let! ts = ts
            let! sel = sel
            match sel with
            | Some _ -> return (GroupsApp.viewLeafButtons ts |> UI.map GroupsMessage)
            | None -> return div[ style "font-style:italic"][ text "no surface selected" ] |> UI.map GroupsMessage
        } 

    let surfacesGroupButtons (model:MSurfaceModel) = 
        let ts = model.surfaces.activeGroup
        adaptive {  
            let! ts = ts
            return (GroupsApp.viewGroupButtons ts |> UI.map GroupsMessage)
        } 
    
    let surfaceUI (model:MSurfaceModel) =
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
            yield GuiEx.accordion "Surfaces" "Cubes" true [ viewSurfacesGroups model ]
            yield GuiEx.accordion "Properties" "Content" false [
              Incremental.div AttributeMap.empty (AList.ofModSingle item2)
               
            ]
            yield GuiEx.accordion "Actions" "Asterisk" false [
                Incremental.div AttributeMap.empty (AList.ofModSingle (buttons))
            ]  
            yield GuiEx.accordion "Transformation" "expand arrows alternate " false [
                Incremental.div AttributeMap.empty (AList.ofModSingle(viewTranslationTools model))
            ]  
                
            yield GuiEx.accordion "Color Adaptation" "file image outline" false [
                Incremental.div AttributeMap.empty (AList.ofModSingle(viewColorCorrectionTools model))
            ] 

            yield GuiEx.accordion "Scalars ColorLegend" "paint brush" true [
                Incremental.div AttributeMap.empty (AList.ofModSingle(viewColorLegendTools model))
            ] 
        ]
    
    

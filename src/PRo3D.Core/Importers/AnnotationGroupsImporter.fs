namespace PRo3D.Core

open System
open System.Xml.Linq
open System.Xml

open Aardvark.Base
open Aardvark.UI
open Aardvark.UI.Primitives
open PRo3D.Base
open PRo3D.Base.Annotation


open FSharp.Data.Adaptive

module AnnotationGroupsImporter = 

    let xname s = XName.Get(s)    

    let rec getGroups (trafo:Trafo3d) (fileName : string) up north (m:XElement) : (Node * HashMap<Guid,Leaf> * HashMap<Guid,string>) = 
        let name    = (m.Element(xname "Name")).Value.ToString()
        let visible = (m.Element(xname "IsVisible")).Value.ToBool()
                           
        let annotations = 
            (m.Elements(xname "Measurements").Elements(xname "object")) 
            |> List.ofSeq
            |> List.map (MeasurementsImporter.getAnnotation trafo)          
            |> List.map(fun x -> 
                let dns = 
                    x.points 
                    |> DipAndStrike.calculateDipAndStrikeResults (up) (north)
        
                let results = 
                    Calculations.calculateAnnotationResults x up north Planet.None
        
                { x with dnsResults = dns; results = Some results} )
            |> List.map Leaf.Annotations
        
        let flat' = 
            annotations 
            |> List.map(fun x -> x.id, x) 
            |> HashMap.ofList
        
        let subGroupsNFlatNLookup = 
            (m.Elements(xname "SubGroups")).Elements(xname "MeasurementGroup") 
            |> List.ofSeq 
            |> List.map(fun x -> getGroups trafo fileName up north x)
        
        let flat' = 
            subGroupsNFlatNLookup 
            |> List.map (fun (_,x,_) -> x)             
            |> List.fold (fun acc x -> HashMap.union acc x) flat'
        
        let nodes = 
            subGroupsNFlatNLookup 
            |> List.map (fun (x,_,_) -> x)             
            |> IndexList.ofList
        
        // collect lookups from subnodes
        let lookUp = 
            subGroupsNFlatNLookup 
            |> List.map (fun (_,_,x) -> x)             
            |> List.fold (fun acc x -> HashMap.union acc x) HashMap.empty
        
        // add current annotations to lookup
        let lookUp' =
            annotations 
            |> List.map(fun x -> HashMap.add x.id name HashMap.empty)
            |> List.fold(fun a b -> HashMap.union a b ) lookUp
        
        let g = 
            {
                version  = Node.current
                key      = Guid.NewGuid()
                name     = if name = "Measurements" then fileName + "_" + name else name
                leaves   = annotations |> List.map(fun x -> x.id) |> IndexList.ofList
                subNodes = nodes
                visible  = visible
                expanded = true
                defaultColor = Node.initialDefaultColor
            }

        (g, flat', lookUp')

    type XmlReader with
    /// Returns a lazy sequence of XElements matching a given name to start from
        member reader.StreamElements(name, ?namespaceURI) =
            let readOp =
                match namespaceURI with
                | None    -> fun () -> reader.ReadToFollowing(name)
                | Some ns -> fun () -> reader.ReadToFollowing(name, ns)
            seq {
                while readOp() do
                    match XElement.ReadFrom reader with
                    | :? XElement as el -> yield el
                    | _ -> ()
            }

    let import (path:string) (refSys:ReferenceSystem) =

        let trafoFile = System.IO.Path.ChangeExtension(path, ".trafo")
        let t = 
            match (Serialization.fileExists trafoFile) with
            | Some path-> 
                use sr = new System.IO.StreamReader (path)
                sr.ReadLine () |> Trafo3d.Parse
            | None -> Trafo3d.Identity

        let fileName = path |> System.IO.Path.GetFileName

        let reader = XmlReader.Create path
        let root = reader.StreamElements("MeasurementGroups") 
        let xGroups = (root.Elements(xname "MeasurementGroup")).ToListOfT<XElement>()
        printfn "%A" xGroups.Count
        let groupsNFlat = 
            xGroups 
            |> Seq.toList
            |> List.map(getGroups t fileName refSys.up.value refSys.north.value)

        let flat = 
            groupsNFlat 
            |> List.map (fun (_,x,_) -> x)
            |> List.fold (fun acc x -> HashMap.union acc x) HashMap.empty

        let groups = 
            groupsNFlat |> List.map (fun (x,_,_) -> x) |> IndexList.ofList
        
        let lookup = 
            groupsNFlat 
            |> List.map (fun (_,_,x) -> x)
            |> List.fold (fun acc x -> HashMap.union acc x) HashMap.empty        

        groups, flat, lookup    


    // SBMT catalogs can contain thousands of annotations. The UI tree
    // (Drawing.UI.fs:280-282) materialises EVERY leaf DomNode at the moment
    // the containing group is expanded -- 4,800 rows at once is visible
    // jank. We chunk the imported leaves into sub-folders so expanding the
    // top-level imported group reveals ~50 collapsed sub-folders instead of
    // ~4,800 individual rows. The user expands the buckets they actually
    // want to look at.
    let private importChunkSize = 100

    let private mkLeafNode
        (name : string)
        (annotationIds : System.Guid list)
        : Node =
        {
            version  = Node.current
            key      = System.Guid.NewGuid()
            name     = name
            leaves   = annotationIds |> IndexList.ofList
            subNodes = IndexList.empty
            visible  = true
            expanded = false
            defaultColor = Node.initialDefaultColor
        }

    // Imports a single SBMT structure file as one annotation group.
    // trafo : applied to every parsed XYZ after the km->m unit scale. v1
    //         callers pass Trafo3d.Identity. The seam is in place for a
    //         later SPICE-derived frame transform (see plans/archive/sbmtImport.md).
    //
    // dnsResults / annotation results are intentionally NOT computed per
    // annotation: SBMT catalogs can contain thousands of ellipses, and the
    // per-row LinearRegression3d / SVD + Log.line calls dominate import time.
    // Points have no meaningful DnS; ellipses already encode their plane in
    // the sampled boundary, so recomputing it adds no information. PRo3D
    // can recompute on demand when a user actually inspects an annotation.
    let importSbmt
        (trafo : Trafo3d)
        (path : string)
        (_refSys : ReferenceSystem)
        (referenceFrame : string)
        : IndexList<Node> * HashMap<Guid, Leaf> * HashMap<Guid, string> =

        let fileName = path |> System.IO.Path.GetFileName

        let annotationLeaves =
            SbmtImporter.startImporter trafo referenceFrame path
            |> IndexList.toList
            |> List.map Leaf.Annotations

        let flat =
            annotationLeaves
            |> List.map (fun x -> x.id, x)
            |> HashMap.ofList

        let ids = annotationLeaves |> List.map (fun x -> x.id)

        // Build the top-level node. If the catalog is small, keep it flat;
        // otherwise bucket into sub-folders of importChunkSize.
        let topNode, lookup =
            if ids.Length <= importChunkSize then
                let lookup =
                    ids
                    |> List.map (fun id -> id, fileName)
                    |> HashMap.ofList
                let node =
                    { mkLeafNode fileName ids with expanded = false }
                node, lookup
            else
                let chunks = ids |> List.chunkBySize importChunkSize
                let subNodes, lookup =
                    chunks
                    |> List.mapi (fun i chunk ->
                        let start = i * importChunkSize + 1
                        let stop  = start + chunk.Length - 1
                        let chunkName = sprintf "%d - %d" start stop
                        let chunkLookup =
                            chunk |> List.map (fun id -> id, chunkName)
                        mkLeafNode chunkName chunk, chunkLookup)
                    |> List.unzip
                let lookup = lookup |> List.concat |> HashMap.ofList
                let node : Node =
                    {
                        version  = Node.current
                        key      = System.Guid.NewGuid()
                        name     = sprintf "%s (%d)" fileName ids.Length
                        leaves   = IndexList.empty
                        subNodes = subNodes |> IndexList.ofList
                        visible  = true
                        // Collapsed by default; same reason as the buckets.
                        expanded = false
                        defaultColor = Node.initialDefaultColor
                    }
                node, lookup

        IndexList.single topNode, flat, lookup

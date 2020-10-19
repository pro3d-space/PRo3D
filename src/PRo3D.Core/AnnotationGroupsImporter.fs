namespace PRo3D

open System
open System.Xml.Linq
open System.Xml

open Aardvark.Base
open Aardvark.UI
open PRo3D.Groups
open PRo3D.ReferenceSystem
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

namespace PRo3D

module RoverPlacementImporter = 
    open System
    open System.Xml.Linq
    open System.Xml

    open Aardvark.Base
    open Aardvark.UI    

    open FSharp.Data.Adaptive

    let xname s = XName.Get(s)

   
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

    let getContextData (reader:XmlReader) =
        let context = reader.StreamElements("rawentity").Elements(xname "context").ToListOfT<XElement>()
        let attributes = context.[0].Elements(xname "attribute").ToListOfT<XElement>()
        let data = attributes
                        |> Seq.toList
                        |> List.map( fun x -> (x.Attribute(xname "value").Value.ToString().Split ';') )

        let placementD = data.[1] |> Seq.toList
        placementD
            |> List.map ( fun x -> x.Split '=')
            |> List.map ( fun x -> if x.Length > 1 then Some (x.[0],x.[1]) else None)
            |> List.choose ( fun x -> x)
            |> HashMap.ofList

               
    let startRPImporter (path:string) =
        let reader = XmlReader.Create path
        let metadata = reader.StreamElements("rawentity").Elements(xname "metadata").ToListOfT<XElement>()
        let attributes = metadata.[0].Elements(xname "attribute").ToListOfT<XElement>()
        let data = attributes
                        |> Seq.toList
                        |> List.map( fun x -> (x.Attribute(xname "name").Value.ToString(),x.Attribute(xname "value").Value.ToString()) )
                        |> HashMap.ofList
                        
        let th = data |> HashMap.find ("Y")
        data
        
        
        
   
namespace PRo3D

module SurfaceTrafoImporter = 
    open System
    open System.Xml.Linq
    open System.Xml

    open Aardvark.Base
    open Aardvark.UI

    let xname s = XName.Get(s)

    let getTrafo (t:XElement) = 
        let strafo = t.Value.NestedBracketSplitLevelOne()
                        |> Seq.map(fun x ->  M44d.Parse(x))
                        |> List.ofSeq
        let trafo = Trafo3d(strafo.[0], strafo.[1])
        trafo
    
    let getData (m:XElement) = 
        let id = (m.Element(xname "Id")).Value 
        let trafo = getTrafo (m.Element(xname "Trafo"))
        
        Log.warn "Found %A" id
        {
            id = id
            trafo = trafo
        }
        
        
    type XmlReader with
    /// Returns a lazy sequence of XElements matching a given name.
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

    let startImporter (path:string) =
        let reader = XmlReader.Create path
        let surfaces = reader.StreamElements("Surfaces").Elements(xname "Surface")
        surfaces 
            |> Seq.map getData
            |> PList.ofSeq  
        


    type Action =
        | Import    of string
        
    let update (model : SurfaceTrafoImporterModel) (act : Action) =
        match act with
            | Import s         -> { model with trafos = startImporter s }
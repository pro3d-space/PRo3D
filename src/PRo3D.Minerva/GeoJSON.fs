namespace PRo3D.Minerva

open Aardvark.Base
open FSharp.Data
open FSharp.Data.JsonExtensions

open FSharp.Data.Adaptive

module MinervaGeoJSON =
  open FSharp.Data.Runtime
  open PRo3D.Base
  open System.Net
  open System.Collections.Specialized
  open System.Text
  //open RestSharp
  //open RestSharp.Authenticators

  let shout s =
    Log.line "%A" s  

  let parseBoundingBox (bbox : JsonValue) : Box2d =
    let bbox = bbox.AsArray()
    
    if bbox.Length <> 4 then failwith "invalid bounding box of size other than 4"
    
    let minLat = bbox.[0].AsFloat()
    let minLon = bbox.[1].AsFloat()
    let maxLat = bbox.[2].AsFloat()
    let maxLon = bbox.[3].AsFloat()
    
    Box2d(minLon, minLat, maxLon, maxLat)

  let parseTypus (typus : JsonValue) : Typus =
    match typus.AsString().ToLowerInvariant() with
      | "featurecollection" -> Typus.FeatureCollection
      | "feature"           -> Typus.Feature
      | "polygon"           -> Typus.Polygon
      | "point"             -> Typus.Point
      | s -> s |> sprintf "[parseTypus] string %A unknown" |> failwith

  let parseProperties (ins : Instrument) (properties : JsonValue) : Properties = 
    let id = (properties?id).AsString()                
    match ins with
        | Instrument.MAHLI ->          
          {
            MAHLI_Properties.id = id |> FeatureId
            beginTime = (properties?begin_time).AsDateTime()
            endTime = (properties?end_time).AsDateTime()
          } |> Properties.MAHLI
        | Instrument.FrontHazcam | Instrument.FrontHazcamL | Instrument.FrontHazcamR ->
          {
            FrontHazcam_Properties.id = id |> FeatureId
            beginTime = (properties?begin_time).AsDateTime()
            endTime = (properties?end_time).AsDateTime()
          } |> Properties.FrontHazcam
        | Instrument.Mastcam | Instrument.MastcamL | Instrument.MastcamR ->
          {
            Mastcam_Properties.id = id |> FeatureId
            beginTime = (properties?begin_time).AsDateTime()
            endTime = (properties?end_time).AsDateTime()
          } |> Properties.Mastcam
        | Instrument.APXS ->
          {
            APXS_Properties.id = id |> FeatureId
          } |> Properties.APXS       
        | Instrument.ChemLib ->
          {
            ChemCam_Properties.id = id |> FeatureId           
          } |> Properties.ChemCam       
        | Instrument.ChemRmi ->
          {
            ChemCam_Properties.id = id |> FeatureId            
          } |> Properties.ChemCam       
        | Instrument.NotImplemented ->
          {
            APXS_Properties.id = id |> FeatureId
          } |> Properties.APXS               
        | _ -> failwith "encountered invalid instrument from parsing"

  let parseSingleCoord (c : array<float>) : V3d =
    if c.Length <> 3 then failwith "invalid coordinate of size other than 3"
    V3d(c.[1],c.[0],c.[2])

  let parseSingleCoordXYZ (c : array<float>) : V3d =
    if c.Length <> 3 then failwith "invalid coordinate of size other than 3"
    V3d(c.[0],c.[1],c.[2])

  let parseCoordinates typus (coordinates : JsonValue) = 
    match typus with
    | Typus.Point -> coordinates.AsArray() |> Array.map(fun x -> x.AsFloat()) |> parseSingleCoord
    | _ -> typus |> sprintf "typus %A not implemented" |> failwith

  let parseGeometry (geometry : JsonValue) : PRo3D.Minerva.Geometry = 
    let typus = geometry.GetProperty("type") |> parseTypus
    let coords = geometry?coordinates |> parseCoordinates typus |> List.singleton
    
    {
      typus       = typus
      coordinates = coords
      positions   = List.empty
    }        

  let parseFeature (feature : JsonValue) : Feature =
    let instrument = (feature?properties?instrument_id).AsString() |> MinervaModel.toInstrument
    let prop = feature?properties |> parseProperties instrument
    
    let position = feature?properties?position.AsArray() |> Array.map(fun x -> x.AsFloat()) |> parseSingleCoordXYZ
    let geometry = feature?geometry |> parseGeometry    

    let sol = (feature?properties?planet_day_number).AsInteger()

    let w = (feature?properties?image_width).AsInteger()
    let h = (feature?properties?image_height).AsInteger()

    {
      id          = (feature?id).AsString()
      instrument  = instrument
      typus       = feature.GetProperty("type") |> parseTypus
      boundingBox = Box2d.Invalid//feature?bbox |> parseBoundingBox
      properties  = prop
      geometry    = { geometry with positions = position |> List.singleton }
      sol         = sol
      dimensions  = V2i(w,h)
    } 

  let parseFeatures (features : JsonValue) : list<Feature> =  
    features.AsArray() |> List.ofArray |> List.map (parseFeature)
    
  let parseRoot(root : JsonValue) : FeatureCollection =
    //let instrument = (root?id).AsString() |> Model.toInstrument

    {
      version     = FeatureCollection.current
      name        = (root?id).AsString()
      typus       = root.GetProperty("type") |> parseTypus    
      boundingBox = Box2d.Invalid//(root?bbox) |> parseBoundingBox
      features    = (root?features) |> parseFeatures |> IndexList.ofList
    }

  let load (siteUrl:string) : FeatureCollection =     
     JsonValue.Load(siteUrl) |> parseRoot   
     
  let combineFeatureCollections (collections : seq<FeatureCollection>) : FeatureCollection =
      {
          version     = FeatureCollection.current
          name        = "layers"
          typus       = Typus.FeatureCollection
          boundingBox = collections |> Seq.fold (fun (a:Box2d) (fc:FeatureCollection) -> a.ExtendedBy(fc.boundingBox)) Box2d.Invalid
          features    = collections |> Seq.map(fun x -> x.features) |> IndexList.concat
      }

  let parseRootProperties (props : JsonValue) =         
    {
      totalCount   = (props?totalCount).AsInteger()
      startIndex   = (props?startIndex).AsInteger()
      itemsPerPage = (props?itemsPerPage).AsInteger()
      published    = (props?published).AsDateTime()
    }

  type QueryDict = HashMap<string,string>

  let toNameValue (nvPairs : QueryDict) =
    let mutable nv = new NameValueCollection()
    for (n,v) in nvPairs do nv.Add(n,v)
    nv    

  let patchThroughWebClient (nv : QueryDict) (site : string)  = 
    let mutable client = new WebClient()    
    client.UseDefaultCredentials <- true       
    let credentials = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("minerva:tai8Ies7"))   
    client.Headers.[System.Net.HttpRequestHeader.Authorization] <- "Basic " + credentials   
   // client.QueryString <- nv |> toNameValue    
    
    let response = client.UploadValues(site, nv |> toNameValue)
    let responseString = Encoding.Default.GetString(response)
    responseString
    //client.DownloadString(site)

  //let patchThroughRestSharpClient (nv : QueryDict) (site : string)  = 
  //  let mutable client = RestClient(site)

  //  let mutable request = RestRequest(Method.POST)
  //  client.Authenticator <- HttpBasicAuthenticator("minerva", "tai8Ies7")
  //  request.AddHeader("Postman-Token", "a8d4ee8e-d6c7-4ea4-84c1-e0f5e4c9d9a6") |> ignore
  //  request.AddHeader("cache-control", "no-cache") |> ignore
  //  request.AddHeader("Authorization", "Basic bWluZXJ2YTp0YWk4SWVzNw==") |> ignore
  //  request.AddHeader("content-type", "multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW") |> ignore
  //  request.AddParameter("multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW", "------WebKitFormBoundary7MA4YWxkTrZu0gW\r\nContent-Disposition: form-data; name=\"cql\"\r\n\r\n(planetDayNumber>751AND planetDayNumber<903)\r\n------WebKitFormBoundary7MA4YWxkTrZu0gW--", ParameterType.RequestBody) |> ignore
  //  let mutable response = client.Execute(request)
  //  response.Content

  let loadFromUrls (sites : list<string * QueryDict>) = 
    let count = sites.Length |> float
    sites 
      |> List.mapi(fun i (x,nv) -> 
        Report.Progress(float i / count)
        x |> patchThroughWebClient nv |> JsonValue.Parse
      )
      //|> Async.Parallel
      //|> Async.RunSynchronously 
      |> List.map parseRoot
      |> combineFeatureCollections


  let loadPaged (site:string) (cql : QueryDict) = 
    
    let bla = site |> patchThroughWebClient cql |> JsonValue.Parse
    let probs = bla?properties |> parseRootProperties
    Log.line "[Minerva] found %i entries" probs.totalCount
    let pages =
      [0 .. probs.itemsPerPage .. probs.totalCount]
        |> List.map(fun index -> 
          //sprintf "%s?startIndex=%d&count=%d&%s" site index probs.itemsPerPage cql            
            let nv = [("startIndex",index.ToString()); ("count",probs.itemsPerPage.ToString())] |> HashMap.ofList
            site, (HashMap.union nv cql)
          )

    pages |> loadFromUrls
    

  let loadMultiple (sites : list<string>) : FeatureCollection =     
    Log.startTimed "fetching data from sites"
    let result = 
      sites |> List.map(fun a -> loadPaged a QueryDict.Empty) |> combineFeatureCollections
    //let result = 
    //  sites |> Load
    Log.stop()
    result
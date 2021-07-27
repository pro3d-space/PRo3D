namespace PRo3D.Minerva

open System
open System.IO
open Aardvark.Base
open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI

open FSharp.Data
open FSharp.Data.JsonExtensions
open Aardvark.Application
open PRo3D.Minerva.Communication
open Aardvark.Geometry

open Chiron
open PRo3D.Base

#nowarn "0686"

module Config =
    let mutable ShowMinervaErrors = false
    let mutable besideExecuteable = "."

type FeatureId = FeatureId of string

type Typus = 
  | FeatureCollection = 0
  | Feature           = 1
  | Polygon           = 2
  | Point             = 3

type MAHLI_Properties =
  {
    id        : FeatureId
    beginTime : DateTime
    endTime   : DateTime
  }

type FrontHazcam_Properties =
  {
    id        : FeatureId
    beginTime : DateTime
    endTime   : DateTime
  }

type Mastcam_Properties =
  {
    id        : FeatureId
    beginTime : DateTime
    endTime   : DateTime
  }

type ChemCam_Properties =
  {
    id        : FeatureId    
  }

type APXS_Properties =
  {
    id        : FeatureId
  }

type Properties =
    | MAHLI       of MAHLI_Properties
    | FrontHazcam of FrontHazcam_Properties
    | Mastcam     of Mastcam_Properties
    | APXS        of APXS_Properties
    | ChemCam     of ChemCam_Properties
    member this.id =
        match this with
        | MAHLI       k -> k.id
        | FrontHazcam k -> k.id
        | Mastcam     k -> k.id
        | APXS        k -> k.id
        | ChemCam     k -> k.id

type Instrument = 
    | MAHLI          =  0
    | FrontHazcam    =  1
    | Mastcam        =  2
    | APXS           =  3
    | FrontHazcamR   =  4
    | FrontHazcamL   =  5
    | MastcamR       =  6
    | MastcamL       =  7
    | ChemLib        =  8
    | ChemRmi        =  9
    | NotImplemented = 10

type Geometry = 
    {
        typus       : Typus
        coordinates : list<V3d>
        positions   : list<V3d>
    }

type Feature =
    { 
        id          : string
        instrument  : Instrument
        typus       : Typus
        properties  : Properties
        boundingBox : Box2d
        geometry    : Geometry
        sol         : int
        dimensions  : V2i
    }

type RootProperties = 
    {
        totalCount   : int
        startIndex   : int 
        itemsPerPage : int    
        published    : DateTime
    }

[<ModelType>]
type FeatureCollection = 
    {
        version     : int
        name        : string
        typus       : Typus
        boundingBox : Box2d    
        features    : IndexList<Feature>
    }
with
    static member current = 0
    static member initial =
        { 
            version     = FeatureCollection.current
            name        = "initial"
            boundingBox = Box2d.Invalid
            typus       = Typus.Feature
            features    = IndexList.empty
        }
    static member private readV0 =
      json {
          let! name         = Json.read "name"
          let! boundingBox  = Json.read "boundingBox"
          let! typus        = Json.read "typus"
          let! features     = Json.read "features"

          return 
            { 
                version     = FeatureCollection.current
                name        = name
                boundingBox = boundingBox |> Box2d.Parse
                typus       = typus       |> enum<Typus>
                features    = features    |> Serialization.jsonSerializer.UnPickleOfString
            }
      }
    static member FromJson( _ : FeatureCollection) = 
        json {
            let! v = Json.read "version"
            match v with            
            | 0 -> return! FeatureCollection.readV0
            | _ -> return! v |> sprintf "don't know version %A  of FeatureCollection" |> Json.error
        }
    static member ToJson (x:FeatureCollection) =
        json {
            do! Json.write "version"      x.version
            do! Json.write "name"         x.name
            do! Json.write "boundingBox" (x.boundingBox.ToString())
            do! Json.write "typus"       (x.typus |> int)
            do! Json.write "features"    (x.features |> Serialization.jsonSerializer.PickleToString)
        }

type QueryAction =
    | SetMinSol         of Numeric.Action
    | SetMaxSol         of Numeric.Action
    | SetDistance       of Numeric.Action
    | SetFilterLocation of V3d
    | CheckMAHLI
    | CheckFrontHazcam
    | CheckMastcam
    | CheckAPXS
    | CheckFrontHazcamR
    | CheckFrontHazcamL
    | CheckMastcamR
    | CheckMastcamL
    | CheckChemLib
    | CheckChemRmi
  //| UseQueriesForDataFile
type SelectedProduct =
    {
        id:  string
        pos: V3d
    }

type MinervaAction =
    | LoadProducts                  of string * string
    | Save
    | Load
    | ApplyFilters
    | ClearFilter
    | PerformQueries  
    | ClearSelection
    | FilterFromSelection
    | SendSelection
    | SendScreenSpaceCoordinates
    | FilterByIds                   of list<string>
    | SelectByIds                   of list<string>
    | RectangleSelect               of Box3d
    | ConnectVisplore
    | FlyToProduct                  of V3d
    | QueryMessage                  of QueryAction
    | SetPointSize                  of Numeric.Action
    | SetTextSize                   of Numeric.Action
    | SingleSelectProduct           of string
    | AddProductToSelection         of string
    | PickProducts                  of SceneHit
    | HoverProducts                 of SceneHit
    | HoverProduct                  of Option<SelectedProduct>
    | OpenTif                       of string
    | OpenFolder                    of string
    | LoadTifs                      of string
    | ShowFrustraForSelected
  //| ChangeInstrumentColor of ColorPicker.Action * Instrument

[<ModelType>]
type SgFeatures = {
    names       : string[]
    positions   : V3d[]
    colors      : C4b[]
    trafo       : Trafo3d
}

//TODO Lf model with HashMap<instrumentType, color>
[<ModelType>]
type InstrumentColor = {
    mahli        : C4b    
    frontHazcam  : C4b 
    mastcam      : C4b  
    apxs         : C4b  
    frontHazcamR : C4b 
    frontHazcamL : C4b 
    mastcamR     : C4b 
    mastcamL     : C4b 
    chemLib      : C4b 
    chemRmi      : C4b  
    color        : ColorInput
}

[<ModelType>]
type FeatureProperties = {
    pointSize   : NumericInput
    textSize    : NumericInput
    //instrumentColor : InstrumentColor
}

module QueryModelInitial =
    let minSol = 
        {
            value = 0.0
            min =  0.0
            max = 10000.0
            step = 1.0
            format = "{0:0}"
        }
    
    let maxSol = 
        {
            value = 3000.0
            min =  0.0
            max = 10000.0
            step = 1.0
            format = "{0:0}"
        }
    
    let distance = 
        {
            value = 10000000.0
            min =  0.0
            max = 10000000.0
            step = 100.0
            format = "{0:0}"
        }

[<ModelType>]
type QueryModel = {
    version              : int
    minSol               : NumericInput
    maxSol               : NumericInput
    
    distance             : NumericInput
    filterLocation       : V3d
    
    //TODO LF ... model with hset or hmap
    checkMAHLI           : bool
    checkFrontHazcam     : bool
    checkMastcam         : bool
    checkAPXS            : bool
    checkFrontHazcamR    : bool
    checkFrontHazcamL    : bool
    checkMastcamR        : bool
    checkMastcamL        : bool
    checkChemLib         : bool
    checkChemRmi         : bool        
}
with 
    static member current = 0    
    static member initial =
        {
            version               = QueryModel.current

            minSol                = QueryModelInitial.minSol
            maxSol                = QueryModelInitial.maxSol
           
            distance              = QueryModelInitial.distance
            filterLocation        = V3d.Zero
            
            checkMAHLI            = true
            checkFrontHazcam      = true
            checkMastcam          = true
            checkAPXS             = true
            checkFrontHazcamR     = true
            checkFrontHazcamL     = true
            checkMastcamR         = true
            checkMastcamL         = true
            checkChemLib          = true
            checkChemRmi          = true              
        }

    static member readV0 = 
        json {
            let! minSol   = Json.readWith Ext.fromJson<NumericInput,Ext> "minSol"
            let! maxSol   = Json.readWith Ext.fromJson<NumericInput,Ext> "maxSol"
            let! distance = Json.readWith Ext.fromJson<NumericInput,Ext> "distance"
            let! filterLocation = Json.read "filterLocation"

            return 
                { QueryModel.initial with 
                    minSol         = minSol 
                    maxSol         = maxSol
                    distance       = distance
                    filterLocation = filterLocation |> V3d.Parse
                }
        }
    static member FromJson( _ :QueryModel) = 
        json {
            let! v = Json.read "version"
            match v with 
            | 0 -> return! QueryModel.readV0
            | _ -> return! v |> sprintf "don't know version %A of Annotation" |> Json.error
        }
    static member ToJson (x : QueryModel) =
        json {
            do! Json.write "version" x.version
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "minSol" x.minSol
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "maxSol" x.maxSol
            do! Json.writeWith Ext.toJson<NumericInput,Ext> "distance" x.distance
            do! Json.write "filterLocation" (x.filterLocation.ToString())
            //do! Json.write "maxSol"  x.maxSol
        }

[<ModelType>]
type SelectionModel = {
    version              : int
    selectedProducts     : HashSet<string> 
    highlightedFrustra   : HashSet<string>

    singleSelectProduct  : option<string>
    selectionMinDist     : float

    [<NonAdaptive>]
    kdTree               : PointKdTreeD<V3d[],V3d>
    [<NonAdaptive>]
    flatPos              : array<V3d>
    [<NonAdaptive>]
    flatID               : array<string>
}
with 
    static member current = 0
    static member initial =
        {
            version = SelectionModel.current
            selectedProducts     = HashSet.Empty
            highlightedFrustra   = HashSet.Empty
            singleSelectProduct  = None
            kdTree               = Unchecked.defaultof<_>
            flatPos              = Array.empty
            flatID               = Array.empty
            selectionMinDist     = 0.05
        }

//type Selection = {
//    selectedProducts        : HashSet<string> 
//    singleSelectProduct     : option<string>
//}

//type MessagingMailbox = MailboxProcessor<MailboxAction>

module MinervaModel =
    module Initial =
        let pointSize = 
            {
                value = 5.0
                min = 0.5
                max = 15.0
                step = 0.1
                format = "{0:0.00}"
            }
        
        let textSize = 
            {
                value = 10.0
                min = 0.001
                max = 30.0
                step = 0.001
                format = "{0:0.000}"
            }  
        
        let featureProperties = 
            {
                pointSize = pointSize
                textSize = textSize
                //instrumentColor = instrumentC
            }
        
        let sgFeatures =
            {
                names     = Array.empty
                positions = Array.empty
                colors    = Array.empty
                trafo     = Trafo3d.Identity
            }
        
        let selectedSgFeatures =
            {
                names     = Array.empty
                positions = Array.empty
                colors    = Array.empty
                trafo     = Trafo3d.Identity
            }                       

        let sites = [
            //@"https://minerva.eox.at/opensearch/collections/MAHLI/json/"
            //@"https://minerva.eox.at/opensearch/collections/FrontHazcam-Right/json/"
            //@"https://minerva.eox.at/opensearch/collections/FrontHazcam-Left/json/"
            //@"https://minerva.eox.at/opensearch/collections/Mastcam-Right/json/"    
            //@"https://minerva.eox.at/opensearch/collections/Mastcam-Left/json/"
            //@"https://minerva.eox.at/opensearch/collections/APXS/json/"
            @"https://minerva.eox.at/opensearch/collections/all/json/"
        ]

    let toInstrument (id : string) = 
        match id.ToLowerInvariant() with
        | "mahli"        -> Instrument.MAHLI
        | "apxs"         -> Instrument.APXS
        | "fhaz_left_b"  -> Instrument.FrontHazcamL    
        | "fhaz_right_b" -> Instrument.FrontHazcamR    
        | "mast_left"    -> Instrument.MastcamL
        | "mast_right"   -> Instrument.MastcamR
        | "chemcam_libs" -> Instrument.ChemLib
        | "chemcam_rmi"  -> Instrument.ChemRmi      
        | _ -> id |> sprintf "unknown instrument %A" |> failwith
    
    let instrumentColor (instr : Instrument) =
        match instr with 
        | Instrument.MAHLI          -> C4b(27,158,119)
        | Instrument.FrontHazcam    -> C4b(255,255,255)
        | Instrument.Mastcam        -> C4b(255,255,255)
        | Instrument.APXS           -> C4b(230,171,2)
        | Instrument.FrontHazcamR   -> C4b(31,120,180)
        | Instrument.FrontHazcamL   -> C4b(166,206,227)
        | Instrument.MastcamR       -> C4b(227,26,28)
        | Instrument.MastcamL       -> C4b(251,154,153)
        | Instrument.ChemRmi        -> C4b(173,221,142)
        | Instrument.ChemLib        -> C4b(49,163,84)
        | Instrument.NotImplemented -> C4b(0,0,0)
        | _ -> failwith "unknown instrument"

    let getProperties (ins : Instrument) (insId:string) (row:CsvRow) : Properties = 
        match ins with
            | Instrument.MAHLI ->   
                {
                MAHLI_Properties.id = insId |> FeatureId
                beginTime =  DateTime.Parse(row.GetColumn "{Timestamp}Start_time")
                endTime =  DateTime.Parse(row.GetColumn "{Timestamp}Stop_time")
                } |> Properties.MAHLI
            | Instrument.FrontHazcam | Instrument.FrontHazcamL | Instrument.FrontHazcamR ->
                {
                FrontHazcam_Properties.id = insId |> FeatureId
                beginTime =  DateTime.Parse(row.GetColumn "{Timestamp}Start_time")
                endTime =  DateTime.Parse(row.GetColumn "{Timestamp}Stop_time")
                } |> Properties.FrontHazcam
            | Instrument.Mastcam | Instrument.MastcamL | Instrument.MastcamR ->
                {
                Mastcam_Properties.id = insId |> FeatureId
                beginTime =  DateTime.Parse(row.GetColumn "{Timestamp}Start_time")
                endTime =  DateTime.Parse(row.GetColumn "{Timestamp}Stop_time")
                } |> Properties.Mastcam
            | Instrument.APXS ->
              {
                APXS_Properties.id = insId |> FeatureId
              } |> Properties.APXS       
            | Instrument.ChemLib ->
              {
                ChemCam_Properties.id = insId |> FeatureId           
              } |> Properties.ChemCam       
            | Instrument.ChemRmi ->
              {
                ChemCam_Properties.id = insId |> FeatureId            
              } |> Properties.ChemCam       
            | Instrument.NotImplemented ->
              {
                APXS_Properties.id = insId |> FeatureId
              } |> Properties.APXS               
            | _ -> failwith "encountered invalid instrument from parsing"

    let intOrDefault (def : int) (name: string) =
        match name with | "" -> def | _ -> name.AsInteger()            

    let getFeature (row:CsvRow) : option<Feature> =

        let id' = row.GetColumn "{Key}Product_id"
        match id' with
         | "" -> None
         | _-> 
            let inst = row.GetColumn "{Category}Instrument_id"
            let instrument = inst |> toInstrument
            let sol' = (row.GetColumn "{Value}{Sol}Planet_day_number").AsInteger()

            let omega = (row.GetColumn "{Angle}Omega").AsFloat()
            let phi = (row.GetColumn "{Angle}Phi").AsFloat()
            let kappa = (row.GetColumn "{Angle}Kappa").AsFloat()

            let x = (row.GetColumn "{CartX}X").AsFloat()
            let y = (row.GetColumn "{CartY}Y").AsFloat()
            let z = (row.GetColumn "{CartZ}Z").AsFloat()


            let w = (row.GetColumn "{Value}Image_width") |> intOrDefault 0            
            let h = (row.GetColumn "{Value}Image_height") |> intOrDefault 0            

            let instName = row.GetColumn "{Category}Instrument_name"

            let props = getProperties instrument inst row

            let geo = 
                {
                    typus = Typus.Point
                    coordinates = V3d(omega, phi, kappa) |> List.singleton
                    positions = V3d(x, y, z) |> List.singleton
                }

            let feature = 
                {
                  id          = id'
                  instrument  = instrument
                  typus       = Typus.Feature 
                  boundingBox = Box2d.Invalid//feature?bbox |> parseBoundingBox
                  properties  = props
                  geometry    = geo
                  sol         = sol'
                  dimensions  = V2i(w, h)
                } 
            Some feature   

    let loadDumpCSV dumpFile cacheFile =
        let cachePath = cacheFile
        let path = dumpFile
        Log.startTimed "[Minerva] Loading products"
        let featureCollection =
            match (File.Exists path, File.Exists cachePath) with
             | (true, false) -> 
                let allData = CsvFile.Load(path).Cache()

                let features = 
                    allData.Rows
                    |> Seq.toList

                let count = features.Length
                    
                let features =
                    features
                    |> List.mapi(fun i x ->                         
                        Report.Progress(float i / float count)
                        getFeature x )      
                    |> List.choose id

                {
                  version     = FeatureCollection.current
                  name        = "dump"
                  typus       = Typus.FeatureCollection    
                  boundingBox = Box2d.Invalid
                  features    = features |> IndexList.ofList
                } //|> Serialization.save cachePath
             | (_, true) -> Serialization.loadAs cachePath
             | _ when Config.ShowMinervaErrors -> 
                Log.error "[Minerva] sth. went wrong with dump.csv"
                FeatureCollection.initial
             | _ -> 
                FeatureCollection.initial

        Log.stop()
        featureCollection
   
[<ModelType>]
type Session =
    {
        version            : int
        queryFilter        : QueryModel
        featureProperties  : FeatureProperties
        selection          : SelectionModel

        queries            : list<string>
        filteredFeatures   : IndexList<Feature> //TODO TO make to ids
        dataFilePath       : string
    }
with
    static member current = 0
    static member initial = 
        {
            version           = Session.current
            queryFilter       = QueryModel.initial
            queries           = List.empty
            filteredFeatures  = IndexList.empty
            selection         = SelectionModel.initial
            featureProperties = MinervaModel.Initial.featureProperties        
            dataFilePath      = ""
        }
    static member readV0 = 
        json {            
            //let! data        = Json.read "data"
            let! queryFilter = Json.read "queryFilter"
                        
            return 
                { 
                    Session.initial with                      
                      queryFilter = queryFilter
                      //queries          = List.empty
                      //filteredFeatures = IndexList.empty

                      //featureProperties = Model.Initial.featureProperties
                      //selection         = Model.Initial.selectionModel
                      
                }
        }
    static member FromJson(_ : Session) = 
        json {
            let! v = Json.read "version"
            match v with            
              | 0 -> return! Session.readV0
              | _ -> return! v |> sprintf "don't know version %A  of Minerva.Session" |> Json.error
        }

    static member ToJson (x : Session) =
        json {
            do! Json.write "version"       Session.current     
            do! Json.write "queryFilter"   x.queryFilter
        }

[<ModelType>]
type MinervaModel = 
    {        
        session            : Session        
        data               : FeatureCollection

        [<NonAdaptive>]
        comm        : option<Communicator.Communicator>

        [<NonAdaptive>]
        vplMessages : ThreadPool<MinervaAction>
        
        kdTreeBounds         : Box3d
        hoveredProduct       : Option<SelectedProduct>
        solLabels            : HashMap<string, V3d>
        sgFeatures           : SgFeatures
        selectedSgFeatures   : SgFeatures
        picking              : bool        
    }                                    
with 
    static member initial =


        {
            session = Session.initial
            data = FeatureCollection.initial

            comm = None
            vplMessages = ThreadPool.Empty

            kdTreeBounds = Box3d.Invalid
            hoveredProduct = None
            solLabels = HashMap.empty
            sgFeatures = MinervaModel.Initial.sgFeatures
            selectedSgFeatures = MinervaModel.Initial.selectedSgFeatures
            picking = false
        }

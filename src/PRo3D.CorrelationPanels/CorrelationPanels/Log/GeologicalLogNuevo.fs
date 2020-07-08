namespace CorrelationDrawing.Nuevo

open System

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Base.Monads
open Aardvark.UI
open Aardvark.Application
     
open CorrelationDrawing.LogTypes
open CorrelationDrawing.Types
open CorrelationDrawing.SemanticTypes
open CorrelationDrawing.LogNodeTypes
open CorrelationDrawing.AnnotationTypes

open PRo3D.Base
open PRo3D.Base.Annotation
open FParsec
open Svgplus
open Svgplus.DiagramItemType
open Svgplus.RectangleType
open Chiron
open UIPlus

type FaciesBorder = {
    version   : int
    level     : int
    contactId : ContactId
    elevation : double
}
with 
    static member current = 0
    static member private readV0 =
        json {
            let! level     = Json.read "level"
            let! contactId = Json.read "contactId"
            let! elevation = Json.read "elevation"
            
            return { 
                version   = FaciesBorder.current
                level     = level
                contactId = contactId |> ContactId
                elevation = elevation |> Double.Parse
            }
        }
    static member FromJson(_:FaciesBorder) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! FaciesBorder.readV0
            | _ -> return! v |> sprintf "don't know version %d of FaciesBorder" |> Json.error
        }
    static member ToJson (x : FaciesBorder) =
        json {
            do! Json.write "version" x.version
            do! Json.write "level" x.level

            do! Json.write "contactId" (x.contactId |> ContactId.value)

            //match x.elevation with
            //| Double.PositiveInfinity ->        
            //    do! Json.write "elevation" "+INF"
            //| Double.NegativeInfinity ->
            //    do! Json.write "elevation" "-INF"
            //| _ -> 
            do! Json.write "elevation" (x.elevation.ToString())
        }

type FaciesId = FaciesId of Guid

module FaciesId =
    let createNew() = 
        Guid.NewGuid() |> FaciesId
    let invalid =
        Guid.Empty |> FaciesId
    let getValue (FaciesId id) =
        id
    let fromRectangleId (RectangleId rid) =
        rid |> FaciesId        
    let toRectangleId (FaciesId fid) =
        fid |> RectangleId        

type Facies = {
    version      : int
    id           : FaciesId
    lower        : FaciesBorder
    upper        : FaciesBorder
    range        : Range1d
    levels       : Range1i
    subFacies    : list<Facies>
    measurements : HashSet<ContactId>
    grainType    : GrainType
    isUncertain  : bool
}
with 
    static member current = 2
    static member private readV2 =
        json {
            let! lower          = Json.read "lower"
            let! upper          = Json.read "upper"
            let! range          = Json.read "range"
            let! levels         = Json.read "levels"
            let! subFacies      = Json.read "subFacies"
            let! measurements   = Json.read "measurements"
            let! faciesId       = Json.read "id"
            let! grainType      = Json.read "grainType"
            let! isUncertain    = Json.read "isUncertain"
            
            return { 
                version      = Facies.current
                id           = faciesId |> FaciesId
                lower        = lower
                upper        = upper
                range        = range  |> Range1d.Parse
                levels       = levels |> Range1i.Parse
                subFacies    = subFacies
                measurements = measurements |> List.map ContactId  |> HashSet.ofList
                grainType    = GrainType.fromString grainType
                isUncertain  = isUncertain
            }
        }
    static member private readV1 =
        json {
            let! lower        = Json.read "lower"
            let! upper        = Json.read "upper"
            let! range        = Json.read "range"
            let! levels       = Json.read "levels"
            let! subFacies    = Json.read "subFacies"
            let! measurements = Json.read "measurements"
            let! faciesId     = Json.read "id"                        

            return { 
                version      = Facies.current
                id           = faciesId |> FaciesId
                lower        = lower
                upper        = upper
                range        = range  |> Range1d.Parse
                levels       = levels |> Range1i.Parse
                subFacies    = subFacies
                measurements = measurements |> List.map ContactId  |> HashSet.ofList
                grainType    = GrainType.Unknown
                isUncertain  = true
            }
        }
    static member private readV0 =
        json {
            let! lower        = Json.read "lower"
            let! upper        = Json.read "upper"
            let! range        = Json.read "range"
            let! levels       = Json.read "levels"
            let! subFacies    = Json.read "subFacies"  
            
            return { 
                version      = Facies.current
                id           = FaciesId.createNew()
                lower        = lower
                upper        = upper
                range        = range  |> Range1d.Parse
                levels       = levels |> Range1i.Parse
                subFacies    = subFacies
                measurements = HashSet.empty
                grainType    = GrainType.Unknown
                isUncertain  = true
            }
        }
    static member FromJson(_:Facies) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! Facies.readV0
            | 1 -> return! Facies.readV1
            | 2 -> return! Facies.readV2
            | _ -> return! v |> sprintf "don't know version %d of Facies" |> Json.error
        }
    static member ToJson (x : Facies) =
        json {
            do! Json.write "version"   x.version
            do! Json.write "id"       (x.id |> FaciesId.getValue)
            do! Json.write "lower"     x.lower
            do! Json.write "upper"     x.upper
            do! Json.write "range"    (x.range  |> string)
            do! Json.write "levels"   (x.levels |> string)
            do! Json.write "subFacies" x.subFacies
            do! Json.write "measurements" (x.measurements |> HashSet.toList |> List.map ContactId.value)
            do! Json.write "grainType"   x.grainType.toString
            do! Json.write "isUncertain" x.isUncertain
        }

[<ModelType>]
type GeologicalLogNuevo = {
    [<NonAdaptive>]
    version            : int
    [<NonAdaptive>]
    id                 : LogId

    name               : string

    facies             : Facies
    contactPoints      : HashMap<ContactId, V3d>
    referencePlane     : DipAndStrikeResults
    planeScale         : float
    referenceElevation : float
}
with 
    static member current = 0
    static member private readV0 =
        json {
            let! id             = Json.read "id"
            let! facies         = Json.read "facies"
            let! name           = Json.read "name"
            let! contactPoints  = Json.read "contactPoints"
            let! referencePlane = Json.read "referencePlane"
            let! planeScale     = Json.read "planeScale"
            
            let contactPoints = 
                contactPoints 
                |> List.map(fun (a:Guid,b:string) -> (ContactId a), (b |> V3d.Parse)) 
                
            return { 
                version            = GeologicalLogNuevo.current
                id                 = id |> LogId
                facies             = facies
                contactPoints      = contactPoints |> HashMap.ofList
                referencePlane     = referencePlane
                referenceElevation = Double.NaN
                planeScale         = planeScale
                name               = name
            }
        }
    static member FromJson(_:GeologicalLogNuevo) = 
        json {
            let! v = Json.read "version"
            match v with
            | 0 -> return! GeologicalLogNuevo.readV0
            | _ -> return! v |> sprintf "don't know version %d of MyType" |> Json.error
        }
    static member ToJson (x : GeologicalLogNuevo) =
        json {
            do! Json.write "version" x.version
            do! Json.write "id"     (x.id |> LogId.value)
            do! Json.write "facies"  x.facies
            do! Json.write "name"    x.name
           
            let contactPoints = 
                x.contactPoints 
                |> HashMap.toList 
                |> List.map(fun (a,b) -> 
                    let (ContactId guid) = a
                    (guid, b.ToString())
                )

            do! Json.write "contactPoints"  contactPoints
            do! Json.write "referencePlane" x.referencePlane
            do! Json.write "planeScale"     x.planeScale
        }

module Facies =

    let create lower upper subNodes =
        if lower.elevation > upper.elevation then
            failwith "[Facies] invalid facies creation"
    
        let minLevel = min lower.level upper.level
        let maxLevel = max lower.level upper.level
    
        {            
            version      = Facies.current
            id           = FaciesId.createNew()
            lower        = lower
            upper        = upper
            range        = Range1d(lower.elevation, upper.elevation)
            levels       = Range1i(minLevel, maxLevel)
            subFacies    = subNodes
            measurements = HashSet.empty
            grainType    = GrainType.Unknown
            isUncertain  = true
        }
    
    let updateRanges (facies : Facies) =
        {
            facies with
                levels = Range1i(facies.lower.level, facies.upper.level)
                range  = Range1d(facies.lower.elevation, facies.upper.elevation)
        }
    
    let createRoot =
        let upper = { version = FaciesBorder.current; level = -1; contactId = ContactId.invalid; elevation = Double.PositiveInfinity }
        let lower = { version = FaciesBorder.current; level = -1; contactId = ContactId.invalid; elevation = Double.NegativeInfinity }
    
        {
            version      = Facies.current
            id           = FaciesId.createNew()
            lower        = lower
            upper        = upper
            range        = Range1d(lower.elevation, upper.elevation)
            levels       = Range1i(lower.level, upper.level)
            subFacies    = []           
            measurements = HashSet.empty
            grainType    = GrainType.Unknown
            isUncertain  = true
        }        

    ///retrieves deepest tree front, i.e. all nodes without subnodes
    let rec leafCut (facies:Facies) : list<Facies> =
        match facies.subFacies with
        | [] -> [facies]
        | _ -> 
            facies.subFacies 
            |> List.collect(fun x -> x |> leafCut)

    let rec levelCut (currentDepth : int) (targetDepth : int) (facies : Facies) : list<Facies> =
        if currentDepth = targetDepth then
            [facies]
        else
            match facies.subFacies with
            | [] -> [facies]
            | fs ->
                fs
                |> List.collect(fun x -> x |> levelCut (currentDepth + 1) targetDepth)
      
    let rec updateFacies (toUpdate : FaciesId) (updateFun : Facies -> Facies) (facies : Facies) : Facies =        
        if (facies.id = toUpdate) then
            updateFun facies
        else
            let subFacies =
                facies.subFacies 
                |> List.map (updateFacies toUpdate updateFun)

            { facies with subFacies = subFacies }

    let rec tryFindFacies (id:FaciesId) (facies : Facies) : option<Facies> =
        if(facies.id = id) then
            Some facies
        else            
            facies.subFacies 
            |> List.choose (tryFindFacies id)
            |> List.tryHead

module GeologicalLogNuevo =

    let initial =

        let id = Guid.NewGuid() 
        
        {
            version            = GeologicalLogNuevo.current
            id                 = id |> LogId
            name               = id |> string
            facies             = Facies.createRoot
            contactPoints      = HashMap.empty
            referencePlane     = DipAndStrikeResults.initial
            referenceElevation = Double.NaN
            planeScale         = Double.NaN
        }      

    let tryCreateFaciesBorder contactsLookup semApp (referencePlane : DipAndStrikeResults) (referenceElevation : float) up p id =
        //get elevation
        //let referenceElevation = PRo3D.Base.CooTransformation.getElevation' planet p

        let elevation = referencePlane.plane.Height(p)
            //match DipAndStrike.signedOrientation up referencePlane.plane with
            //| -1 -> -referencePlane.plane.Height(p)
            //| _  -> referencePlane.plane.Height(p)

        let elevation = elevation + referenceElevation  //PRo3D.Base.CooTransformation.getElevation' planet p

        //get contact
        let border   = 
            match contactsLookup |> HashMap.tryFind id with
            | Some (c : Contact) ->
                let semantic = semApp.semantics |> HashMap.tryFind c.semanticId
                match semantic with
                | Some s -> 
                    Some { version = FaciesBorder.current; level = s.level.value; elevation = elevation; contactId = c.id }
                | None -> 
                    Log.warn "[Correlations] couldn't find semantic %A of %A" c.semanticId id
                    None 
            | None -> 
                Log.warn "[Correlations] couldn't find contact %A" id
                None

        border

    let splitFacies 
        contactsLookup 
        semApp
        (contact : FaciesBorder)
        (facies  : Facies) : Facies * Facies =

        //check inside range

        if(facies.range.Contains contact.elevation |> not) then 
            failwith "[Facies] facies can only be split inside range"

        let left =  { facies with id = FaciesId.createNew(); upper = contact } |> Facies.updateRanges
        let right = { facies with id = FaciesId.createNew(); lower = contact } |> Facies.updateRanges

        left, right
        
    //Facies is singular and plural. sorry for the confusion
    let rec addContactToFacies (currentLevel : int) contactsLookUp semApp (contact : FaciesBorder) (facies : Facies) : Facies =
        Log.line "[Log] current %d; inserting %d with elevation %f" currentLevel contact.level contact.elevation

        let nextLevel = currentLevel + 1

        match contact.level with
        | a when a > nextLevel ->
            Log.line "[Log] descend"
            match facies.subFacies with
            | [] -> 
                //if next level does not exist, copy current facies and descend
                Log.line "[Log] creating next level"
                { facies with subFacies = [facies] }
                |> addContactToFacies nextLevel contactsLookUp semApp contact
            | _ ->
                //descend into correct facies
                let subNodes =
                    facies.subFacies
                    |> List.map (fun x -> 
                        if x.range.Contains contact.elevation then
                            addContactToFacies nextLevel contactsLookUp semApp contact x
                        else
                            x
                    )
                { facies with subFacies = subNodes }
        | a when a = nextLevel ->
            //if no subfacies exist copy current facies
            let facies = 
                match facies.subFacies with
                | [] -> { facies with subFacies = [facies] }
                | _ -> facies

            //insert contact by splitting respective facies
            Log.line "[Log] insert"
            let subNodes =
                [                    
                    for node in facies.subFacies do
                        if node.range.Contains contact.elevation then
                            //replace node with two nodes resulting from split
                            Log.line "[Log] splitting %A" node.range
                            let left, right = splitFacies contactsLookUp semApp contact node
                            Log.line "[Log] into %A %A" left.range right.range
                            yield! [left; right]
                        else
                            yield node
                ]
            
            { facies with subFacies = subNodes }
        | _ ->
            failwith "[Geological Log] error in termination condition"
       
    let mutable logNameCounter = 0

    let updateLogWithNewPoints 
        contactsLookup 
        semApp 
        planet 
        (contactId,p)
        (log : GeologicalLogNuevo) =

        let log = { log with contactPoints = log.contactPoints |> HashMap.alter contactId (fun _ -> Some p) }

        let referenceElevation = 
            CooTransformation.getElevation' planet log.referencePlane.centerOfMass

        let up =
            CooTransformation.getUpVector log.referencePlane.centerOfMass planet
        

        let contacts =
            log.contactPoints
            |> HashMap.toList
            |> List.choose(fun (id, point) -> 
                tryCreateFaciesBorder 
                    contactsLookup 
                    semApp 
                    log.referencePlane 
                    referenceElevation
                    up 
                    point 
                    id
            )
            |> List.sortByDescending (fun fb -> fb.elevation)

        Log.line "correlation log creation %A" contacts
        //drawnLog
        let root = Facies.createRoot
           
        let contacts = 
            contacts             
            |> List.sortBy(fun x -> x.level)
        
        let facies =
            contacts 
            |> List.sortBy(fun x -> x.level)
            |> List.fold(fun acc c ->
                addContactToFacies 
                    root.levels.Min 
                    contactsLookup 
                    semApp 
                    c
                    acc
            ) root

        { log with facies = facies }

    let createLog
        (logId          : LogId)
        (selectedPoints : HashMap<ContactId, V3d>)
        (referencePlane : DipAndStrikeResults)
        (planeScale     : float)
        (contactsLookup : ContactsTable)
        (semApp         : SemanticsModel)
        (planet         : Planet) =

        let referenceElevation = 
            CooTransformation.getElevation' planet referencePlane.centerOfMass

        let up =
            CooTransformation.getUpVector referencePlane.centerOfMass planet

        let contacts =
            selectedPoints
            |> HashMap.toList
            |> List.choose(fun (id, point) -> 
                tryCreateFaciesBorder contactsLookup semApp referencePlane referenceElevation up point id
            )
            |> List.sortByDescending (fun fb -> fb.elevation)

        Log.line "correlation log creation %A" contacts
        //drawnLog
        let root = Facies.createRoot
       
        let contacts = 
            contacts             
            |> List.sortBy(fun x -> x.level)

        let tree = 
            contacts 
            |> List.sortBy(fun x -> x.level)
            |> List.fold(fun acc c ->
                addContactToFacies 
                    root.levels.Min 
                    contactsLookup 
                    semApp 
                    c
                    acc
            ) root                

        Log.line "correlation log creation facies %A" tree

        let logName = 
            // update counter
            logNameCounter <- logNameCounter + 1
            sprintf "Log%i" logNameCounter

        { 
            version            = GeologicalLogNuevo.current
            id                 = logId
            name               = logName//logId |> LogId.value |> string
            facies             = tree
            contactPoints      = selectedPoints
            referencePlane     = referencePlane
            referenceElevation = referenceElevation
            planeScale         = planeScale
        }
            
type DiagramConfig = {
    north               : V3d
    up                  : V3d
    elevationZeroHeight : float
    yToSvg              : float
    defaultWidth        : float    
    infinityHeightPixel : float
    defaultBorderColor  : C4b
}

module LogToDiagram =
    open UIPlus
    open Svgplus.RectangleStackTypes
    open Svgplus.RectangleType
    open CorrelationDrawing

    let tryGetBorderProperty
        (f      : CorrelationSemantic -> 'a)
        (annos  : ContactsTable)
        (semApp : SemanticsModel) 
        (border : FaciesBorder) =

        annos 
        |> HashMap.tryFind border.contactId
        |> Option.bind (fun a -> semApp.semantics |> HashMap.tryFind a.semanticId )
        |> Option.map (fun s -> f s)

    let tryGetBorderColor (border : FaciesBorder) (annotations : ContactsTable) (semApp : SemanticsModel) =
        tryGetBorderProperty 
            (fun s -> s.color) 
            annotations semApp    
            border

    let tryGetBorderThickness (border : FaciesBorder) (annotations : ContactsTable) (semApp : SemanticsModel) =
        tryGetBorderProperty
            (fun s -> s.thickness) 
            annotations semApp    
            border

    let calcMetricValue n contacts = 
        None
        //failwith ""
        //LogNodes.Recursive.calcMetricValue n contacts //TODO!!!!
    
    let faciesToRectangle 
        (config    : DiagramConfig) 
        (colourMap : ColourMap) 
        (semantics : SemanticsModel) 
        (contacts  : ContactsTable) 
        (stackType : StackType)
        (facies    : Facies) =

        let metricVal = calcMetricValue facies contacts // allways None...
                
        //distinguish nodes containing metric values (grainsizes)
        let (isUncertain, width, colour) =            
                let defaultColor = colourMap.mappings.[colourMap.defaultValue].colour
                let defWidth = 
                    match stackType with
                    | Primary -> config.defaultWidth
                    | Secondary -> 15.0 // MAGIC width for secondary stack
                (facies.isUncertain, defWidth, defaultColor)
    
        //let yAxisUpperBorder = sprintf "%.2f" (facies.range.Max - config.elevationZeroHeight)
        
        let dataHeight, fixedInfinityHeight = 
            match facies.range.Size.IsInfinity() with
            | true -> 
                let subNodesDataHeight =
                    facies
                    |> Facies.leafCut
                    |> List.sortByDescending (fun facies -> facies.range.Center)
                    |> List.fold (fun (state: Range1d) fa -> 
                        let r = fa.range
                        let lower = if r.Min.IsNegativeInfinity() then r.Max else r.Min
                        let upper = if r.Max.IsPositiveInfinity() then r.Min else r.Max
                        Range1d(Range1d(lower, upper), state)
                    ) Range1d.Invalid

                subNodesDataHeight.Size, Some config.infinityHeightPixel
            | false -> 
                facies.range.Size, None

        let height = 
            match fixedInfinityHeight with
            | Some infHeight -> dataHeight * config.yToSvg + infHeight
            | None -> dataHeight * config.yToSvg                        
                
        let grainInfo : GrainSizeInfo =
            let middle = colourMap.mappings.[facies.grainType].defaultMiddle
            let displayWidth = (21.0 + System.Math.Log(middle,2.0)) * 10.0   
            {
                grainType    = facies.grainType
                middleSize   = middle  
                displayWidth = displayWidth
            }

        //(facies.id |> FaciesId.toRectangleId)
        let rectId = RectangleId.createNew()
        
        {
           Svgplus.Rectangle.init rectId with
                faciesId          = facies.id |> FaciesId.getValue
                dim               = { width = width; height = height}
                worldHeight       = dataHeight
                isUncertain       = isUncertain
                colour            = colour
                fixedInfinityHeight = fixedInfinityHeight
                grainSize         = grainInfo
        }
            
    let toBorderContactId (ContactId guid) : BorderContactId = 
        guid |> BorderContactId

    let toContactId (BorderContactId guid) : ContactId =
        guid |> ContactId

    let faciesToRectangleStack 
        (stackId   : RectangleStackId) 
        (config    : DiagramConfig) 
        (facies    : list<Facies>)  
        (colourMap : ColourMap) 
        (semantics : SemanticsModel) 
        (contacts  : ContactsTable)
        (stackType : StackType) =        
        
        let orderedFacies = 
            facies 
            |> List.sortByDescending(fun x -> x.range.Center)

        let rectangles =
            orderedFacies 
            |> List.map(fun x ->
                let rect =  x |> faciesToRectangle config colourMap semantics contacts stackType
                (rect.id, rect) 
            )

        let rectPairs = 
            rectangles 
            |> List.map fst 
            |> List.pairwise

        //gets all lower boundaries of all facies
        let faciesBorders = 
            orderedFacies
            |> List.take(orderedFacies.Length-1)
            |> List.map(fun x -> x.lower)

        let borders =
            rectPairs
            |> List.zip faciesBorders
            |> List.map(fun (b, (u,l)) ->
                
                let color,thickness =
                    b 
                    |> tryGetBorderProperty (fun s -> s.color.c, s.thickness.value) contacts semantics                    
                    |> Option.defaultValue (config.defaultBorderColor, 20.0)

                let b1 : RectangleBorder = {   
                    id             = Guid.NewGuid() |> RectangleBorderId
                    contactId      = b.contactId |> toBorderContactId
                    upperRectangle = u
                    lowerRectangle = l
                    color          = color
                    weight         = thickness
                }
                b1.id, b1
            )
            |> HashMap.ofList

        let upmost = orderedFacies |> List.tryHead
        let lowest = orderedFacies |> List.tryLast

        let dataRange =
            Option.map2 (fun l u ->
                Range1d(l.range.Max,u.range.Min)) lowest upmost //ignores infinity nodes
            |> Option.defaultValue (Range1d.Invalid)
        
        if faciesBorders.Length <> rectPairs.Length then
            failwith "[Correlations] borders and rectangle pairs must be of equal length"

        let rectangleStack =
            (RectangleStackApp.create 
                stackId 
                (rectangles |> HashMap.ofList) 
                (rectangles |> List.map fst)
                borders
                dataRange
                config.yToSvg
                stackType)

        rectangleStack, orderedFacies
    
    let primaryLog 
        id
        (config    : DiagramConfig)
        (facies    : Facies) 
        (colourMap : ColourMap) 
        (semantics : SemanticsModel) 
        (contacts  : ContactsTable) =

        let faciesLeafCut =
            facies
            |> Facies.leafCut
            |> List.sortByDescending (fun facies -> facies.range.Center)

        faciesToRectangleStack id config faciesLeafCut colourMap semantics contacts Primary
    
    //let aggregateMeasurements (faciess : list<Facies>) : HashSet<ContactId> = 
    //    faciess |> List.fold (fun acc x -> acc |> HashSet.union x.measurements) HashSet.empty

    let rec fetchMeasurements (facies : Facies) : HashSet<ContactId> =        
        if ((facies.id |> FaciesId.getValue).ToString() = "5c4e3ad8-259b-4370-9bac-a7014895f042") then
            Log.line "not emptz !!!"

        match facies.subFacies with
        | [] -> 
            if (facies.measurements.IsEmpty) |> not then
                Log.line "not emptz !!!"

            facies.measurements
        | xs ->                         
            xs 
            |> List.fold(fun acc x -> acc |> HashSet.union (fetchMeasurements x)) HashSet.empty

    let aggregateMeasurements (facies : list<Facies>) : list<Facies> = 
        facies 
        |> List.map(fun x -> 
            { x with measurements = fetchMeasurements x }
        )

    let roses contacts up north facies rectangleIds = 
        List.map2(fun facies rectId ->
            if (facies.measurements.IsEmpty) |> not then
                Log.line "not empty !!!"

            let angles =
                facies.measurements 
                |> HashSet.toList
                |> List.choose(fun x -> contacts |> HashMap.tryFind x )
                |> List.choose(fun (x : Contact) ->
                    x.points 
                    |> IndexList.map(fun y -> y.point)                         
                    |> DipAndStrike.calculateDipAndStrikeResults up north
                )
                |> List.map(fun x -> x.dipAzimuth)
            
            let rose : Svgplus.DiagramItemType.RoseDiagramRelated = 
                {
                    relatedRectangle = rectId
                    roseDiagram = RoseDiagram.init angles 
                }
            (rose.roseDiagram.id, rose)
        ) facies rectangleIds
        |> HashMap.ofList 

    let private readLine (filePath:string) =
         use sr = new System.IO.StreamReader (filePath)
         sr.ReadLine ()     

    let secondaryLog 
        id 
        (config      : DiagramConfig)
        (facies      : Facies) 
        (faciesDepth : int)
        (colourMap   : ColourMap) 
        (semantics   : SemanticsModel)        
        (contacts    : ContactsTable) =        

        let cutAtLevel =
            if System.IO.File.Exists ".\levelcut" then
                readLine ".\levelcut" |> int
            else
                0
            

        let faciesLevelCut =
            facies
            |> Facies.levelCut cutAtLevel faciesDepth
            |> List.sortByDescending (fun facies -> facies.range.Center)
            |> aggregateMeasurements        

        //failwith "do planefits and dip angles for contacts"

        let stack, orderedFacies = faciesToRectangleStack id config faciesLevelCut colourMap semantics contacts Secondary

        let roses = roses contacts config.up config.north orderedFacies stack.order

        (stack, facies, roses)

    let faciesToLogNodes (rectangleStack:RectangleStack) (facies:list<Facies>) : list<LogNode> =
        
        let rectangles = 
            rectangleStack.order 
            |> List.map (fun x -> rectangleStack.rectangles.Find x)

        let zipped = 
            facies 
            |> List.zip rectangles        

        let bla : list<LogNode> = 
            zipped 
            |> List.map(fun (r,f) ->
                {
                    id = f.id |> FaciesId.getValue |> LogNodeId //Guid.NewGuid() |> LogNodeId
                    rectangleId = r.id
                    logId = rectangleStack.id
                    nodeType = LogNodeType.Hierarchical
                    level = NodeLevel.invalid
                    lBorder = None
                    uBorder = None
                    annotation = None
                    mainBody = r
                    children = IndexList.empty
                }
            )

        bla
    
    let setItemHeader (text : string) (item : DiagramItem) =
        {
            item with 
                DiagramItem.header = { 
                    item.header with 
                        label = { 
                            item.header.label with 
                                textInput = { 
                                    item.header.label.textInput with 
                                        text = text }
        }}}

    let createDiagram
        (log       : GeologicalLogNuevo)
        (semantics : SemanticsModel)
        (contacts  : ContactsTable)   
        (colourMap : ColourMap)        
        (config    : DiagramConfig) = 

        let facies = log.facies
        
        //let rectangles =
        //    facies
        //    |> Facies.leafCut            
        //    |> List.map (fun n -> nodeToRectangle config colourMap semantics contacts n)

        //let rmap = 
        //    rectangles 
        //    |> List.map (fun (r,h) -> (r.id, r))
        //    |> HashMap.ofList          

        let primaryStack, primaryFacies = 
            primaryLog (RectangleStackId.createNew()) config facies colourMap semantics contacts 

        let primaryNodes = faciesToLogNodes primaryStack primaryFacies                                

        let secondaryStack, secondaryFacies, roseDiagrams = 
            secondaryLog (RectangleStackId.createNew()) config facies 1 colourMap semantics contacts             

        let diagramItemId = log.id |> LogId.value |> DiagramItemId.createFrom 

        let contactPoint= 
            log.contactPoints
            |> HashMap.values
            |> Box3d
            |> fun x -> 
                Log.line "\n\nFOUND CONTACT POINT AT: %A \n\n" x.Center
                x.Center

        let item = 
            DiagramItem.createDiagramItem primaryStack (Some secondaryStack) roseDiagrams config.yToSvg diagramItemId contactPoint
        
        let item = item |> setItemHeader log.name

        let ref : LogDiagramReferences =
            {
                itemId        = item.id
                mainLog       = primaryStack.id
                secondaryLog  = Some secondaryStack.id
            }
        
        let (LogId guid) = log.id

        let rectangleStackId = 
            guid |> RectangleStackId

        let log =
            {
                id             = rectangleStackId
                diagramRef     = ref
                state          = State.Display
                defaultWidth   = config.defaultWidth
                nodes          = primaryNodes |> IndexList.ofList
                annoPoints     = HashMap.empty
            }
        
        (item, log) 
    
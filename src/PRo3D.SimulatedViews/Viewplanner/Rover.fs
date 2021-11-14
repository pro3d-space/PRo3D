namespace PRo3D.SimulatedViews

open System
open System.Runtime.InteropServices

open FSharp.Data.Adaptive

open Aardvark.Base
open Aardvark.UI
open IPWrappers

open JR

module RoverProvider =

    let toInstrument i (inst : InstrumentPlatforms.SInstrument) = 
        let pos    = inst.m_oInstrumentExtrinsics.m_oPosition.ToV3d()
        let up     = inst.m_oInstrumentExtrinsics.m_oUp.ToV3d()
        let lookAt = inst.m_oInstrumentExtrinsics.m_oLookAt.ToV3d()
        let box    = inst.m_oInstrumentExtrinsics.m_oBoundingBox.ToBox3d()

        let focals = 
            JR.InstrumentPlatforms.UnMarshalArray<double>(
                    inst.m_pdCalibratedFocalLengths, (int)inst.m_nNrOfCalibratedFocalLengths) 

        let extrinsics =
            {
                position  = pos
                camUp     = up
                camLookAt = lookAt
                box       = box
            }

        let intrinsics : Intrinsics =
            {
                horizontalFieldOfView         = inst.m_oInstrumentIntrinsics.m_dFieldOfViewH
                verticalFieldOfView           = inst.m_oInstrumentIntrinsics.m_dFieldOfViewV
                horizontalResolution          = inst.m_oInstrumentIntrinsics.m_nResolutionH
                verticalResolution            = inst.m_oInstrumentIntrinsics.m_nResolutionV
                horizontalPrinciplePoint      = inst.m_oInstrumentIntrinsics.m_dPrinciplePointH
                verticalPrinciplePoint        = inst.m_oInstrumentIntrinsics.m_dPrinciplePointV
                horizontalFocalLengthPerPixel = inst.m_oInstrumentIntrinsics.m_dFocalLengthPerPxH
                verticalFocalLengthPerPixel   = inst.m_oInstrumentIntrinsics.m_dFocalLengthPerPxV
                horizontalDistortionMap       = String.Empty
                verticalDistortionMap         = String.Empty
                vignettingMap                 = String.Empty
            }

       // let focalToNumeric (focals : list<double>) (current : double) : NumericInput =

        let instrument = {
            index                  = i
            id                     = inst.m_pcInstrumentName.ToStrAnsi()
            calibratedFocalLengths = focals |> Array.toList
            //currentFocalLength     = inst.m_dCurrentFocalLengthInMm
            intrinsics             = intrinsics
            extrinsics             = extrinsics
            iType                  = InstrumentType.Undefined

            focal = {
                        value  = inst.m_dCurrentFocalLengthInMm
                        min    = focals |> Array.head 
                        max    = focals |> Array.last
                        step   = 0.1
                        format = "{0:0.0}"
            }
        }        

        instrument

    let toInstruments (instruments : InstrumentPlatforms.SInstrument[]) : list<Instrument> = 
        instruments |> Array.mapi toInstrument |> Array.toList
       
    let shiftNumericInput shift (input:NumericInput)= 
        {
            value  = input.value
            min    = input.min + shift
            max    = input.max + shift
            step   = input.step
            format = input.format
        }

    let axisHack (axis:Axis) = 
        if (axis.id = "Pan Axis") 
        then
            {axis with angle = axis.angle |> shiftNumericInput -180.0}
        else 
            axis

    let shiftOutput (axis:Axis) (shift:bool) = 
        if (axis.id = "Pan Axis") && shift
        then
            {axis with angle = {axis.angle with value = axis.angle.value - 360.0}}
        else 
            axis

    let toAxes (axes : InstrumentPlatforms.SAxis[]) : list<Axis> =
        axes 
        |> Array.mapi(fun i x ->
            { 
                index        = i
                id           = x.m_pcAxisId.ToStrAnsi()
                description  = x.m_pcAxisDescription.ToStrAnsi()
                startPoint   = x.m_oStartPoint.ToV3d()
                endPoint     = x.m_oEndPoint.ToV3d()

                angle = {
                    value  = x.m_fCurrentAngle.DegreesFromGons()
                    min    = x.m_fMinAngle.DegreesFromGons()
                    max    = x.m_fMaxAngle.DegreesFromGons()
                    step   = 0.1
                    format = "{0:0.0}"
                }
            }
        ) 
        |> Array.map axisHack
        |> Array.toList

    let toRover (platform : InstrumentPlatforms.SPlatform) =
        let wheels = 
            InstrumentPlatforms.UnMarshalArray<InstrumentPlatforms.SPoint3D>(
                platform.m_poPointsOnGround, 
                (int)platform.m_nNrOfPlatformPointsOnGround
            ).ToV3ds()
                       
        let axes = 
            InstrumentPlatforms.UnMarshalArray<InstrumentPlatforms.SAxis>(
                platform.m_poPlatformAxes, 
                (int)platform.m_nNrOfPlatformAxes) 
            |> toAxes

        let instruments = 
            InstrumentPlatforms.UnMarshalArray<InstrumentPlatforms.SInstrument>(
                platform.m_poPlatformInstruments, 
                (int)platform.m_nNrOfPlatformInstruments) 
            |> toInstruments
                
        {
            id              = platform.m_pcPlatformId.ToStrAnsi()
            platform2Ground = platform.m_oPlatform2Ground.m_oHelmertTransfMatrix.ToM44d()
            wheelPositions  = wheels |> List.ofSeq
            instruments     = instruments |> List.map(fun x -> x.id, x) |> HashMap.ofList
            axes            = axes |> List.map(fun x -> x.id, x) |> HashMap.ofList
            box             = platform.m_oBoundingBox.ToBox3d()
        }
        
    let platformNames () =
        let numberOfPlatforms = InstrumentPlatforms.GetNrOfAvailablePlatforms();
        Log.line "Number of Plattforms: %d" numberOfPlatforms
    
        let pointerArray : IntPtr array = Array.zeroCreate (int numberOfPlatforms)
        InstrumentPlatforms.GetAvailablePlatforms(pointerArray, numberOfPlatforms);
       
        pointerArray |> Array.map(fun x -> Marshal.PtrToStringAnsi(x))

    let initRover platformId =
        //Get various counts to initialize respective arrays
        let numWheelPoints = uint32 6 // IPWrapper.GetNrOfPlatformPointsOnGround(platformId)
        let nuAdaptiveInstruments = InstrumentPlatforms.GetNrOfPlatformInstruments(platformId)
        let nuAdaptiveAxis =        InstrumentPlatforms.GetNrOfPlatformAxes(platformId)

        Log.line "Initialising %A" platformId
        Log.line "Wheels: %d, Instruments: %d, Axes: %d" numWheelPoints nuAdaptiveInstruments nuAdaptiveAxis

        //create and empty platform struct to be filled by the backend
        let mutable platform = 
            InstrumentPlatforms.SPlatform.CreateEmpty(numWheelPoints, nuAdaptiveInstruments, nuAdaptiveAxis)

        //crucial for backend to know which platform / rover to take
        platform.m_pcPlatformId <- platformId.ToPtr();

        //init platform writes values into platform reference
        let errorCode = InstrumentPlatforms.InitPlatform(ref platform, numWheelPoints, nuAdaptiveInstruments, nuAdaptiveAxis)
        if (errorCode <> 0) then failwith "init platform failed" else ()

        //convert initialised platform to rover and add to database
        let rover = platform |> toRover        
        
        rover, platform
        
module RoverApp = 
    open FSharp.Data.Adaptive

    type Action =
        //| ChangeAngle      of string * Numeric.Action
        //| ChangeFocal      of string * Numeric.Action
        | SelectRover      of option<Rover>
        //| SelectInstrument of option<Instrument>
        //| SelectAxis       of option<Axis>

    let updateRoversAndPlatforms p m shift =
        let error = InstrumentPlatforms.UpdatePlatform(ref p)
        match error with
          | 0 ->
            let r'         = p |> RoverProvider.toRover
            let r''        = { r' with axes = r'.axes |> HashMap.map(fun x y -> RoverProvider.shiftOutput y shift) }
            let rovers    = m.rovers |> HashMap.alter r''.id (Option.map(fun _ -> r''))
            let platforms = m.platforms |> HashMap.alter r''.id (Option.map(fun _ -> p))

            { m with rovers = rovers; platforms = platforms }
          | _ -> 
            Log.error "encountered %d from update platform" error
            m

    let updateFocusPlatform (up : InstrumentFocusUpdate) (m : RoverModel) = 
        let r = m.rovers |> HashMap.find up.roverId    
        let mutable p = m.platforms |> HashMap.find up.roverId       
        let i = r.instruments|> HashMap.find up.instrumentId
        let mutable pInstruments = InstrumentPlatforms.UnMarshalArray<InstrumentPlatforms.SInstrument>(p.m_poPlatformInstruments)

        pInstruments.[i.index].m_dCurrentFocalLengthInMm <- up.focal
        InstrumentPlatforms.MarshalArray(pInstruments, p.m_poPlatformInstruments)

        updateRoversAndPlatforms p m false    

    let updateAnglePlatform (up : AxisAngleUpdate) (m : RoverModel) = 
        // use option map ?
        let r = m.rovers |> HashMap.find up.roverId    
        let mutable p = m.platforms |> HashMap.find up.roverId       
        let a = r.axes |> HashMap.find up.axisId
        let mutable pAxes = InstrumentPlatforms.UnMarshalArray<InstrumentPlatforms.SAxis>(p.m_poPlatformAxes)

        //let up = { up with angle = -up.angle}

        // push new angles to platform (deg to gon!!!)               
        let angle, shift = if (up.axisId = "Pan Axis"  && (up.angle < 0.0)) then 
                                (up.angle + 360.0), true 
                            else
                                up.angle, false

        pAxes.[a.index].m_fCurrentAngle <- angle.GonsFromDegrees() //up.angle.GonsFromDegrees()
        InstrumentPlatforms.MarshalArray(pAxes, p.m_poPlatformAxes)        
        
        updateRoversAndPlatforms p m shift
            

    let updateRovers (r : Rover) (m : RoverModel) = 
        let rovers' = 
            m.rovers 
                |> HashMap.update r.id (fun x -> 
                    match x with 
                        | Some _ -> r
                        | None   -> failwith "rover not found")
                        
        { m with selectedRover = Some r; rovers = rovers' }

    let update (m : RoverModel) (a:Action) =
        match a with
          | SelectRover r      -> { m with selectedRover = r }
          
    let angle = 
        {
            value = 0.0
            min =  -90.0
            max = 90.0
            step = 0.1
            format = "{0:0.0}"
        }

    let initial =
        {
            rovers = HashMap.Empty
            platforms = HashMap.Empty
            //selectedInstrument = None
            selectedRover = None
            //selectedAxis = None
            //currentAngle = angle
        }

    let mapTolist (input : amap<_,'a>) : alist<'a> = 
        input |> AMap.toASet |> AList.ofASet |> AList.map snd    
    
    let roversList (m:AdaptiveRoverModel) = 
        (m.rovers |> mapTolist)

    let toText (r : AdaptiveRover) =
        adaptive {
            let! instr  = r.instruments |> mapTolist |> AList.count
            let! axes   = r.axes |> mapTolist |> AList.count
            let! wheels = r.wheelPositions |> AVal.map(fun x -> x.Length)

            return sprintf "instr: %d | axes: %d  | wheels: %d" instr axes wheels
        }

    let viewRovers (m:AdaptiveRoverModel) = 
        Incremental.div 
            (AttributeMap.ofList [clazz "ui divided list inverted segment"; style "overflow-y : auto; width: 300px"]) 
            (                  
                alist {                                                                                         
                    for r in (roversList m) do
                        yield div [clazz "item"] [
                            div [clazz "content"] [                                        
                                div [clazz "header"] [
                                    Incremental.text r.id
                                ]
                                div [clazz "description"] [
                                   Incremental.text (r |> toText)
                                ]
                            ]
                        ]
                }
            )   

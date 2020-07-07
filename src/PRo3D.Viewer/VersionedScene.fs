namespace PRo3D.Scene.Versioned

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open Aardvark.UI.Primitives

open PRo3D
open PRo3D.Base
open PRo3D.Versioned
open Chiron

#nowarn "0686"
[<DomainType>]
type NavigationModel' = {
    version        : int
    view           : CameraView
    navigationMode : NavigationMode
    exploreCenter  : V3d
}

module NavigationModel' = 
    open PRo3D.Navigation2

    let current = 0
    let convert (old : NavigationModel) : NavigationModel' = 
        {
            version        = current
            view           = old.camera.view
            navigationMode = old.navigationMode
            exploreCenter  = old.exploreCenter
        }

    let read0 = 
        json {            
            let! navigationMode = Json.read "navigationMode"
            let! exploreCenter  = Json.read "exploreCenter"

            let! (cameraView : list<string>) = Json.read "view"
            let cameraView = cameraView |> List.map V3d.Parse
            let cameraView = CameraView(cameraView.[0],cameraView.[1],cameraView.[2],cameraView.[3], cameraView.[4])

            return 
                {
                    version        = current
                    view           = cameraView
                    navigationMode = navigationMode |> enum<NavigationMode>
                    exploreCenter  = exploreCenter  |> V3d.Parse
                }
        }

    type NavigationModel' with 
        static member FromJson(_ : NavigationModel') =
            json {
                let! v = Json.read "version"
                match v with
                    | 0 -> return! read0
                    | _ -> return! v |> sprintf "don't know version %A  of ViewConfigModel" |> Json.error
            }
        static member ToJson (x : NavigationModel') =
            json {
                do! Json.write "version"        x.version
                
                let camView = 
                    [x.view.Sky; x.view.Location; x.view.Forward; x.view.Up ; x.view.Right] |> List.map(fun x -> x.ToString())      
                do! Json.write "view" camView            
                do! Json.write "navigationMode" (x.navigationMode.ToString())
                do! Json.write "exploreCenter " (x.exploreCenter.ToString())
            }

[<DomainType>]
type ViewConfigModel' = {
    [<NonIncremental>]
    version                 : int
    nearPlane               : NumericInput
    farPlane                : NumericInput
    navigationSensitivity   : NumericInput
    importTriangleSize      : NumericInput
    arrowLength             : NumericInput
    arrowThickness          : NumericInput
    dnsPlaneSize            : NumericInput
    offset                  : NumericInput
    lodColoring             : bool
    drawOrientationCube     : bool
    }

#nowarn "0044"

module ViewConfigModel' =
  //convert from nonversioned to versioned
  let current = 1
  let convert (oldConfig : PRo3D.ViewConfigModel) : ViewConfigModel' = 
    {
        version               = 0
        nearPlane             = oldConfig.nearPlane
        farPlane              = oldConfig.farPlane
        navigationSensitivity = oldConfig.navigationSensitivity
        arrowLength           = oldConfig.arrowLength          
        arrowThickness        = oldConfig.arrowThickness
        dnsPlaneSize          = oldConfig.dnsPlaneSize       
        lodColoring           = oldConfig.lodColoring
        importTriangleSize    = oldConfig.importTriangleSize
        drawOrientationCube   = oldConfig.drawOrientationCube
        offset                = PRo3D.ViewConfigModel.depthOffset
    }

  let initial = {
    version = current
    nearPlane             = PRo3D.ViewConfigModel.initNearPlane
    farPlane              = PRo3D.ViewConfigModel.initFarPlane
    navigationSensitivity = PRo3D.ViewConfigModel.initNavSens
    arrowLength           = PRo3D.ViewConfigModel.initArrowLength
    arrowThickness        = PRo3D.ViewConfigModel.initArrowThickness
    dnsPlaneSize          = PRo3D.ViewConfigModel.initPlaneSize
    lodColoring           = false
    importTriangleSize    = PRo3D.ViewConfigModel.initImportTriangleSize        
    drawOrientationCube   = false
    offset                = PRo3D.ViewConfigModel.depthOffset       
  }
     
  module V0 =
    let read = 
      json {
        let! nearPlane                    = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
        let! farPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
        let! navigationSensitivity        = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
        let! arrowLength                  = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
        let! arrowThickness               = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
        let! dnsPlaneSize                 = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
        let! (lodColoring : bool)         = Json.read "lodColoring"
        let! importTriangleSize           = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
        let! (drawOrientationCube : bool) = Json.read "drawOrientationCube"                        

        //return initial

        return {            
            version               = current
            nearPlane             = nearPlane
            farPlane              = farPlane
            navigationSensitivity = navigationSensitivity
            arrowLength           = arrowLength
            arrowThickness        = arrowThickness
            dnsPlaneSize          = dnsPlaneSize
            lodColoring           = lodColoring
            importTriangleSize    = importTriangleSize      
            drawOrientationCube   = drawOrientationCube
            offset                = PRo3D.ViewConfigModel.depthOffset               
        }
      }
  module V1 =
    let read = 
      json {
        let! nearPlane                    = Json.readWith Ext.fromJson<NumericInput,Ext> "nearPlane"
        let! farPlane                     = Json.readWith Ext.fromJson<NumericInput,Ext> "farPlane"
        let! navigationSensitivity        = Json.readWith Ext.fromJson<NumericInput,Ext> "navigationSensitivity"
        let! arrowLength                  = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowLength"
        let! arrowThickness               = Json.readWith Ext.fromJson<NumericInput,Ext> "arrowThickness"
        let! dnsPlaneSize                 = Json.readWith Ext.fromJson<NumericInput,Ext> "dnsPlaneSize"
        let! (lodColoring : bool)         = Json.read "lodColoring"
        let! importTriangleSize           = Json.readWith Ext.fromJson<NumericInput,Ext> "importTriangleSize"
        let! (drawOrientationCube : bool) = Json.read "drawOrientationCube"                        
        let! depthoffset           = Json.readWith Ext.fromJson<NumericInput,Ext> "depthOffset"
    
        //return initial
    
        return {            
            version               = current
            nearPlane             = nearPlane
            farPlane              = farPlane
            navigationSensitivity = navigationSensitivity
            arrowLength           = arrowLength
            arrowThickness        = arrowThickness
            dnsPlaneSize          = dnsPlaneSize
            lodColoring           = lodColoring
            importTriangleSize    = importTriangleSize      
            drawOrientationCube   = drawOrientationCube
            offset = depthoffset
        }
      }
   
type ViewConfigModel' with 
  static member FromJson(_ : ViewConfigModel') = 
    json {
        let! v = Json.read "version"
        match v with
            | 0 -> return! ViewConfigModel'.V0.read
            | 1 -> return! ViewConfigModel'.V1.read
            | _ -> return! v |> sprintf "don't know version %A  of ViewConfigModel" |> Json.error
    }
  static member ToJson (x : ViewConfigModel') =
         json {
             do! Json.write "drawOrientationCube" x.drawOrientationCube                       
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "importTriangleSize" x.importTriangleSize
             do! Json.write "lodColoring" x.lodColoring
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "dnsPlaneSize" x.dnsPlaneSize
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "arrowThickness" x.arrowThickness
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "arrowLength" x.arrowLength
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "navigationSensitivity" x.navigationSensitivity
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "farPlane" x.farPlane
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "nearPlane" x.nearPlane
             do! Json.writeWith (Ext.toJson<NumericInput,Ext>) "offset" x.offset
             do! Json.write "version" x.version
         }

module IO = 
  let writeToFile path (contents : string) =
      System.IO.File.WriteAllText(path, contents)

  let readFromFile path =
      System.IO.File.ReadAllText(path)  

  let writeStuff() =
    let oldconfig = ViewConfigModel.initial |> ViewConfigModel'.convert

    let v0Path = ".\configtest_v0.json"

    //let result = oldconfig |> Json.serialize |> Json.formatWith JsonFormattingOptions.Pretty 
    //Log.line "writing configjson v%A to %A: \n %A" oldconfig.version v0Path result
    //result |> writeToFile v0Path

    let (v0config : ViewConfigModel') = v0Path |> readFromFile |> Json.parse |> Json.deserialize

  //  let (readConfig : ViewConfigModel') = result |> Json.parse |> Json.deserialize
    Log.line "reading configjson v%A: \n %A" v0config.version v0config
    //readConfig
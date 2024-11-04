namespace PRo3D.Base

open FSharp.Data.Adaptive
open Adaptify
open Aardvark.UI.Primitives
open PRo3D
open PRo3D.Core
open Aardvark.Base
open PRo3D.Base
open Aardvark.Rendering
open Aether


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module NavigationModel =
    let freeFlyCamera = FreeFlyController.initial //CameraController.initial // RNO is this necessary for deprecated ArcBall?
    let initial = {
        freeFlyCamera  = freeFlyCamera        
        navigationMode = NavigationMode.FreeFly        
        exploreCenter  = V3d.Zero // make option        
        orbitCamera    = OrbitState.ofFreeFly 1.0 freeFlyCamera
    }

    /// creates an intial NavigationModel with default values for
    /// sensitivity, 
    /// panFactor, and 
    /// zoomFactor
    let initialDefault = 
        let freeFlyCamera = 
            {FreeFlyController.initial with 
                sensitivity = 3.0
                panFactor = 0.0008
                zoomFactor = 0.0008
            }
        {
            freeFlyCamera  = freeFlyCamera
            navigationMode = NavigationMode.FreeFly        
            exploreCenter  = V3d.Zero // make option  // RNO WHY?  
            orbitCamera    =  OrbitState.ofFreeFly 1.0 freeFlyCamera
        }

    let withView (view : CameraView) 
                 (m    : NavigationModel) =
        let orbitRadius = m.freeFlyCamera.view.Location.Distance m.exploreCenter
        {m with freeFlyCamera = (snd CameraControllerState.view_) view m.freeFlyCamera
                orbitCamera   = OrbitState.ofFreeFly orbitRadius m.freeFlyCamera
        }

    let view_ = 
        (fun (navModel : NavigationModel) -> navModel.view),
        (fun (view : CameraView) (navModel : NavigationModel) ->
            withView view navModel
        )

    let resetControllerState (m : NavigationModel) = 
        let freeFlyCamera = 
            { 
                m.freeFlyCamera with 
                    forward  = false
                    backward = false
                    left     = false
                    right    = false
                    isWheel  = false
                    zoom     = false
                    pan      = false 
                    look     = false
                    moveVec  = V3d.Zero
            }
        
        let orbitCamera =
            Optic.set OrbitState.panning_ false m.orbitCamera

        {m with freeFlyCamera = freeFlyCamera
                orbitCamera   = orbitCamera
        }
                    

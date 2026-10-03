namespace PRo3D.Composition

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.UI.Primitives
open FSharp.Data.Adaptive

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing

open Aether
open Aether.Operators

/// The lens configurations a host hands the Core sub-apps so they can read the camera,
/// reference-system and view-config values they need without knowing the host's model.
/// Both hosts keep `ViewConfigModel` and `ReferenceSystem`, so the lenses are shared.
module HostConfigs =

    let mrefConfig : MInnerConfig<AdaptiveViewConfigModel> =
        {
            getArrowLength    = fun (x:AdaptiveViewConfigModel) -> x.arrowLength.value
            getArrowThickness = fun (x:AdaptiveViewConfigModel) -> x.arrowThickness.value
            getNearDistance   = fun (x:AdaptiveViewConfigModel) -> x.nearPlane.value
            getHorizontalFieldOfView = fun (x:AdaptiveViewConfigModel) ->
                                            x.frustumModel.frustum
                                            |> AVal.map Frustum.horizontalFieldOfViewInDegrees
        }

    let drawingConfig : DrawingApp.SmallConfig<ReferenceSystem> =
        {
            up     = (ReferenceSystem.up_     >-> V3dInput.value_) |> Aether.toBase
            north  = (ReferenceSystem.northO_ |> Aether.toBase)
            planet = (ReferenceSystem.planet_ |> Aether.toBase)
        }

    let mdrawingConfig : DrawingApp.MSmallConfig<AdaptiveViewConfigModel> =
        {
            getNearPlane        = fun x -> x.nearPlane.value
            getHfov             = fun (x:AdaptiveViewConfigModel) -> x.frustumModel.frustum
                                                                     |> AVal.map Frustum.horizontalFieldOfViewInDegrees
            getArrowThickness   = fun (x:AdaptiveViewConfigModel) -> x.arrowThickness.value
            getArrowLength      = fun (x:AdaptiveViewConfigModel) -> x.arrowLength.value
            getDnsPlaneSize     = fun (x:AdaptiveViewConfigModel) -> x.dnsPlaneSize.value
            getOffset           = fun (x:AdaptiveViewConfigModel) -> AVal.constant(0.1)
            getPickingTolerance = fun (x:AdaptiveViewConfigModel) -> x.pickingTolerance.value
        }

    let navConf : Navigation.smallConfig<ViewConfigModel, ReferenceSystem> =
        {
            navigationSensitivity = ViewConfigModel.navigationSensitivity_ >-> NumericInput.value_ |> Aether.toBase
            up                    = ReferenceSystem.up_ >-> V3dInput.value_  |> Aether.toBase
            north                 = ReferenceSystem.north_ >-> V3dInput.value_ |> Aether.toBase
            northO                = ReferenceSystem.northO_ |> Aether.toBase
            frustum               = ViewConfigModel.frustumModel_ >-> FrustumModel.frustum_ |> Aether.toBase
            windowSize            = ViewConfigModel.frustumModel_ >-> FrustumModel.windowSize_ |> Aether.toBase
            planet                = (ReferenceSystem.planet_ |> Aether.toBase)
        }

    /// The navigation state a host starts with: PRo3D's camera sensitivities.
    let initialNavigation : NavigationModel =
        let init = PRo3D.Navigation2.NavigationModel.initial
        let init = Optic.set (NavigationModel.camera_ >-> CameraControllerState.sensitivity_) 3.0 init
        let init = Optic.set (NavigationModel.camera_ >-> CameraControllerState.panFactor_) 0.0008 init
        let init = Optic.set (NavigationModel.camera_ >-> CameraControllerState.zoomFactor_) 0.0008 init
        init

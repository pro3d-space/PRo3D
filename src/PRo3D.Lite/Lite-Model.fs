namespace PRo3D.Lite

open Aardvark.Base
open Aardvark.Rendering
open Aardvark.Application
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.UI.Primitives.Golden
open Aardvark.UI.Animation.Deprecated
open FSharp.Data.Adaptive
open Adaptify

open PRo3D
open PRo3D.Base
open PRo3D.Core
open PRo3D.Core.Drawing
open PRo3D.Core.Surface
open PRo3D.Composition

type LiteAction =
    // the composed Core sub-apps
    | NavigationMsg      of Navigation.Action
    | SurfacesMsg        of SurfaceAppAction
    | DrawingMsg         of DrawingAction
    | AnnotationPropsMsg of AnnotationProperties.Action
    | RefSystemMsg       of ReferenceSystemAction
    | ConfigMsg          of ConfigProperties.Action
    | AnimationMsg       of AnimationAction
    | GoldenMsg          of GoldenLayout.Message
    // picking and input
    | PickSurfaceHit  of SceneHit * string
    /// the cursor moved over a surface while a tool is armed
    | SurfaceHover
    | SetInteraction  of Interactions
    | KeyDownMsg         of Keys
    | KeyUpMsg           of Keys
    | Resize          of V2i
    // surfaces and camera
    | ImportOpcs      of list<string>
    | Home
    // files
    | OpenScene       of list<string>
    | SaveScene
    | SaveSceneAs     of string
    | LoadAnnotations of list<string>
    | SaveAnnotations of string
    | NoOp

/// The whole state of PRo3D Lite: the Core sub-models it composes plus a little UI state.
/// Persisted through `SceneCore` (scene) and `.pro3d.ann` (annotations).
[<ModelType>]
type LiteModel =
    {
        surfaces    : SurfaceModel
        drawing     : DrawingModel
        navigation  : NavigationModel
        refSystem   : ReferenceSystem
        config      : ViewConfigModel
        animations  : AnimationModel
        golden      : GoldenLayout

        /// the active tool; Lite offers the shared subset (see `LiteApp.tools`)
        interaction : Interactions
        ctrlFlag    : bool
        shiftFlag   : bool
        viewPortSize : V2i

        scenePath   : Option<string>
        /// one line of feedback for the status bar
        status      : string

        [<NonAdaptive>]
        userPrefs   : UserPreferences
        /// every key of the opened scene file Lite does not understand, written back on save
        [<NonAdaptive>]
        sceneExtras : Option<Chiron.Json>
        [<NonAdaptive>]
        kdCache     : SurfacePicking.KdTreeCache
    }

namespace PRo3D.Viewer

open Adaptify
open Aardvark.UI.Primitives.Golden

/// A layout found beside a scene that was just opened, waiting for the user to decide.
type PendingSceneLayout =
    {
        sceneName         : string
        layout            : WindowLayout
        /// the library already holds a layout with this arrangement
        alreadyInLibrary  : bool
        importIntoLibrary : bool
        apply             : bool
    }

[<RequireQualifiedAccess>]
type LayoutDialog =
    | None
    | SaveAs
    | Manage
    | Rename      of name : string
    | SceneLayout of PendingSceneLayout
    /// A message that must not go unseen (toasts vanish before a starting viewer shows),
    /// followed by the dialog that was pending behind it.
    | Notice      of message : string * next : LayoutDialog

/// The viewer's window layout. Runtime state of the local user, deliberately not part of
/// `Scene`: it is restored from AppData, and only written beside a scene as a sidecar.
/// See docs/WindowLayouts.md.
[<ModelType>]
type LayoutModel =
    {
        /// Golden Layout state. Its `DefaultLayout` tracks `current`, so a reloaded page
        /// boots into the layout the user left.
        golden     : GoldenLayout

        /// The layout as last reported by the browser (or last applied), sanitized.
        [<TreatAsValue>]
        current    : WindowLayout

        /// The browser has reported the arrangement last pushed to it. Until then, reported
        /// layouts may be stale events from before the push.
        pushConfirmed : bool

        /// Name of the dashboard or library layout that was applied last.
        activeName : string

        /// Arrangement (`LayoutOps.shape`) of that layout, to tell whether the user changed it.
        activeShape : string

        /// Names of the layouts in the user's library, sorted.
        [<TreatAsValue>]
        library    : list<string>

        [<TreatAsValue>]
        dialog     : LayoutDialog

        /// Text of the name field of the save-as and rename dialogs.
        nameInput  : string
    }

[<RequireQualifiedAccess>]
type LayoutAction =
    /// Serialized layout reported by Golden Layout after any change in the browser.
    | Changed            of json : string
    | ApplyDashboard     of name : string
    | ApplyLibrary       of name : string
    | ReopenPanel        of panelId : string
    | OpenDialog         of LayoutDialog
    | CloseDialog
    | SetNameInput       of string
    | SaveCurrentAs
    | Delete             of name : string
    | Rename
    | SetImportSceneLayout of bool
    | SetApplySceneLayout  of bool
    | ConfirmSceneLayout

namespace PRo3D
open PRo3D.SimulatedViews

type GuiMode =
  | NoGui
  | RenderViewOnly
  | CoreGui
  | CompleteGui


type StartupArgs = {
  opcPaths              : option<list<string>>
  objPaths              : option<list<string>>
  snapshotPath          : option<string>
  outFolder             : string
  snapshotType          : option<SnapshotType>
  guiMode               : GuiMode
  showExplorationPoint  : bool
  showReferenceSystem   : bool
  renderDepth           : bool
  renderMask            : bool
  exitOnFinish          : bool
  areValid              : bool
  verbose               : bool
  startEmpty            : bool
  useAsyncLoading       : bool
  magnificationFilter   : bool
  frameId               : option<int> // at whicht frame to start rendering
  frameCount            : option<int> // how many frames to render
} with 
  member args.hasValidAnimationArgs =
      (args.opcPaths.IsSome || args.objPaths.IsSome)
          && args.snapshotType.IsSome && args.areValid
  static member initArgs =
    {
        opcPaths              = None
        objPaths              = None
        snapshotPath          = None
        snapshotType          = None
        guiMode               = GuiMode.CompleteGui
        showExplorationPoint  = true
        showReferenceSystem   = true
        renderDepth           = false
        renderMask            = false
        exitOnFinish          = false
        areValid              = true
        verbose               = false
        startEmpty            = false
        useAsyncLoading       = true
        magnificationFilter   = false
        outFolder             = ""
        frameId               = None // at whicht frame to start rendering
        frameCount            = None // how many frames to render
    }
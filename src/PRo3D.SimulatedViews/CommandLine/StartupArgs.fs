﻿namespace PRo3D.SimulatedViews

type GuiMode =
  | NoGui
  | RenderViewOnly
  | CoreGui
  | CompleteGui


type CLStartupArgs = {
  opcPaths              : option<list<string>>
  objPaths              : option<list<string>>
  scenePath             : option<string>
  snapshotPath          : option<string>
  outFolder             : string
  snapshotType          : option<SnapshotType>
  showExplorationPoint  : bool
  showReferenceSystem   : bool
  renderDepth           : bool
  //renderDepthTif        : bool
  renderMask            : bool
  exitOnFinish          : bool
  areValid              : bool
  verbose               : bool
  startEmpty            : bool
  useAsyncLoading       : bool
  magnificationFilter   : bool
  frameId               : option<int> // at whicht frame to start rendering
  frameCount            : option<int> // how many frames to render
  remoteApp             : bool
  serverMode            : bool
} with 
  member args.hasValidAnimationArgs =
      (args.opcPaths.IsSome || args.objPaths.IsSome || args.scenePath.IsSome)
          && args.snapshotType.IsSome && args.areValid
  static member initArgs =
    {
        opcPaths              = None
        objPaths              = None
        scenePath             = None
        snapshotPath          = None
        snapshotType          = None
        showExplorationPoint  = true
        showReferenceSystem   = true
        renderDepth           = false
        //renderDepthTif        = false
        renderMask            = false
        exitOnFinish          = false
        areValid              = true
        verbose               = false
        startEmpty            = false
        useAsyncLoading       = false
        magnificationFilter   = true
        outFolder             = ""
        frameId               = None // at whicht frame to start rendering
        frameCount            = None // how many frames to render
        remoteApp             = false
        serverMode            = false
    }

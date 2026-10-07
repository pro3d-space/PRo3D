namespace PRo3D.Core

module Config =


  let mutable data_samples = "4"
  let mutable useMapping = "true"

  let mutable backgroundColor = "#222222"

  let mutable disableMultisampling = false

  let mutable title = "PRo3D"


  let mutable previewIntersections = true

  let diagnosticTimings = false

  /// Show the busy indicator once an update has been running this long, in
  /// milliseconds. 0 disables it (and the polling) entirely: `-nobusy`.
  /// See docs/BusyIndicator.md.
  let mutable busyIndicatorMilliseconds = 400

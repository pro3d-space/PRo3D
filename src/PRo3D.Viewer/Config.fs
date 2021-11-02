namespace PRo3D.Core

module Config =

  let useAsyncIntersections = false
  let sampleCount = 100

  let mutable configPath = "."
  let mutable colorPaletteStore = ".\palettes.js"

  let mutable besideExecuteable = "."

  let mutable data_samples = "4"
  let mutable useMapping = "true"

  let mutable backgroundColor = "#222222"

  let mutable disableMultisampling = false
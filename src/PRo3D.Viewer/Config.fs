namespace PRo3D.Core

module Config =

  let useAsyncIntersections = false
  let sampleCount = 100

  let mutable configPath = "."
  let mutable colorPaletteStore = ".\palettes.js"

  let mutable besideExecuteable = "."
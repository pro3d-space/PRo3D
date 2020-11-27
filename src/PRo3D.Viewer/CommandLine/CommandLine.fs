namespace PRo3D

open Aardvark.Base
open PRo3D.SimulatedViews
open PRo3D.CommandLineUtils

module CommandLine =
    /// parse commandline arguments
    let parseArguments (argv : array<string>) : StartupArgs =    

        let useAsyncLoading     = (argv |> hasFlag "sync" |> not)
        let startEmpty          = (argv |> hasFlag "empty")
        let magFilter           = not (argv |> hasFlag "noMagFilter")
        let showExplorationCentre = (argv |> Array.contains "--excentre")
        let args = 
            {
                startEmpty            = startEmpty
                useAsyncLoading       = false
                magnificationFilter   = magFilter
                showExplorationPoint  = showExplorationCentre
            }
        args

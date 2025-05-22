#nowarn "9"
open System
open Microsoft.FSharp.NativeInterop  
open Expecto
open JR
open System.IO

[<EntryPoint>]
let main args =
    // all testing moved to PRo3D.SPICE, remaining InstrumentPlatform components should move to PRo3D.SICE as well, keep this project for potential integration tests.
    0
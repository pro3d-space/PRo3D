namespace Tests

open Aardvark.Base

open FsUnit
open NUnit.Framework
open Microsoft.FSharp.NativeInterop

module Base =

    open PRo3D.Base

    [<Test>]
    let ``[PRo3D.Core.Surface.Files win]``() =
    
        let r = PRo3D.Core.Surface.Files.relativePath @"C:\user\ZCAM-0555-ZCAM08575-L-RAD-ALL-34-OPC-MULTI-SPHR-20220922\ZCAM-0555-ZCAM08575-L-RAD-ALL-34-OPC-MULTI-SPHR-20220922_000_000\patches\05-Patch-00001~0065\00-Patch-00020~0021-0.aakd" 4

        r.Value |> should equal "ZCAM-0555-ZCAM08575-L-RAD-ALL-34-OPC-MULTI-SPHR-20220922_000_000\patches\05-Patch-00001~0065\00-Patch-00020~0021-0.aakd"

    [<Test>]
    let ``[PRo3D.Core.Surface.Files osx]``() =
    
        let r = PRo3D.Core.Surface.Files.relativePath @"/home/Desktop/ZCAM-0555-ZCAM08575-L-RAD-ALL-34-OPC-MULTI-SPHR-20220922/ZCAM-0555-ZCAM08575-L-RAD-ALL-34-OPC-MULTI-SPHR-20220922_000_000/patches/05-Patch-00001~0065/00-Patch-00020~0021-0.aakd" 4

        r.Value |> should equal "ZCAM-0555-ZCAM08575-L-RAD-ALL-34-OPC-MULTI-SPHR-20220922_000_000\patches\05-Patch-00001~0065\00-Patch-00020~0021-0.aakd"
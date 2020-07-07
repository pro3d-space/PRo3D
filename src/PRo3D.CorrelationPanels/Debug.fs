module Debug

  let printSourceLocation() =
    printfn "Line: %s" __LINE__
    printfn "Source Directory: %s" __SOURCE_DIRECTORY__
    printfn "Source File: %s" __SOURCE_FILE__

  let warn (msg : string) =
    printfn "WARNING:"
    printfn "%s" msg
    printSourceLocation ()
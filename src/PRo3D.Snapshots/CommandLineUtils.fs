namespace PRo3D
open Aardvark.Base

module CommandLineUtils =
    /// check if the given flag is set; checks --flag -flag --FLAG and -FLAG
    let hasFlag (flag : string) (args : string[]) =
        let f = flag.Trim('-')
        (args |> Array.contains (sprintf "--%s" f))
            || (args |> Array.contains (sprintf "-%s" f))
            || (args |> Array.contains (sprintf "--%s" (f.ToUpper ()))
            || (args |> Array.contains (sprintf "-%s" (f.ToUpper ())))
        )

    /// check if flag is present in list and return corresponding argument
    let parseArg (flag : string) (args : string []) =
        try 
            if (args |> Array.contains flag) then
                let ind = Array.findIndex (fun s -> String.equals s flag) args
                if ind + 1 < Array.length args then
                    Array.item (ind + 1) args
                        |> Some
                else None
            else None
        with e ->
            Log.warn "Could not parse command line arguments"
            Log.warn "%s" e.Message
            None

    /// check if flag is present and returns the corresponding
    /// argument(s) in a list if it is
    let parseMultiple (flag : string) (delim : char) (args : string []) =
        let list = parseArg flag args
        match list with
        | Some s ->
            String.split delim s
                |> Some
        | None -> None
    
    /// check whether the given string is the path to an existing directory or file
    ///  logs an error and returns false if path does not exist
    let checkPath path = 
        try
            let path = 
                System.IO.Path.GetFullPath path
            let check = (System.IO.Directory.Exists path) || (System.IO.File.Exists path)
            if not check then
                Log.line "Could not find path %s" path
            check
        with e ->
            Log.line "[Arguments] Invalid path: %s" path
            Log.line "%s" e.Message
            false


    /// checks the validity of multiple paths
    ///  logs an error and returns false if any of the paths does not exist
    let checkPaths paths = 
        Option.map (fun lst -> lst 
                                    |> List.map checkPath
                                    |> List.reduce (fun b1 b2 -> b1 && b2) 
                    ) paths   


    /// checks whether the path leads to an existing directory,
    /// creates the directory if it does not exist
    /// logs a message and returns an empty string if path does not exist
    let checkAndCreateDir path = 
        match path with
            | Some f ->
                let outPathCheck = checkPath f
                match outPathCheck with
                | true -> f
                | false ->
                    try
                        let res = System.IO.Directory.CreateDirectory f
                        Log.line "Created directory %s" f
                        res.FullName
                    with ex ->
                        Log.line "Could not create directory %s" f
                        Log.line "Using default location."
                        System.IO.Path.GetFullPath "./"
            | None -> System.IO.Path.GetFullPath "./"
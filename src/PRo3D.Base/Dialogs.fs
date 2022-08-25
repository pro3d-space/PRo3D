namespace PRo3D.Base

open Aardvark.UI
open System

module Dialogs =    
    
    let onChooseFiles (chosen : list<string> -> 'msg) =
        let cb xs =
            match xs with
            | [] -> chosen []
            | x::[] when x <> null -> 
                x 
                |> Aardvark.Service.Pickler.json.UnPickleOfString 
                |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                |> chosen
            | _ -> 
                chosen []
        onEvent "onchoose" [] cb   

    let onChooseDirectory (id:Guid) (chosen : Guid * string -> 'msg) =
        let cb xs =
            match xs with
            | [] -> chosen (id, String.Empty)
            | x::[] when x <> null -> 
                let id = id
                let path = 
                    x 
                    |> Aardvark.Service.Pickler.json.UnPickleOfString 
                    |> List.map Aardvark.Service.PathUtils.ofUnixStyle 
                    |> List.tryHead
                match path with
                | Some p -> 
                  chosen (id, p)
                | None -> chosen (id,String.Empty)
            | _ -> 
                chosen (id,String.Empty)
        onEvent "onchoose" [] cb   

    let onSaveFile (chosen : string -> 'msg) =
        let cb xs =
            match xs with
            | x::[] when x <> null -> 
                x 
                |> Aardvark.Service.Pickler.json.UnPickleOfString 
                |> Aardvark.Service.PathUtils.ofUnixStyle 
                |> chosen
            | _ -> 
                chosen String.Empty //failwithf "onSaveFile: %A" xs
        onEvent "onsave" [] cb

    let onSaveFile1 (chosen : string -> 'msg) (path : Option<string>) =
        let cb xs =
            match path with
            | Some p-> p |> chosen
            | None ->
                match xs with
                | x::[] when x <> null -> 
                    x |> Aardvark.Service.Pickler.json.UnPickleOfString |> Aardvark.Service.PathUtils.ofUnixStyle |> chosen
                | _ -> 
                    String.Empty |> chosen
        onEvent "onsave" [] cb

    let jsSelectPathDialog =
        "top.aardvark.dialog.showOpenDialog({tile: 'Select directory', filters: [{ name: 'directories'}], properties: ['openDirectory']}).then(result => {aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"

    let jsSelectPathDialogWithPath startPath =
        let startPath =
            String.replace "\\" "\\\\" startPath
        sprintf "top.aardvark.dialog.showOpenDialog({tile: 'Select directory', defaultPath: '%s', filters: [{ name: 'directories'}], properties: ['openDirectory']}).then(result => {aardvark.processEvent('__ID__', 'onchoose', result.filePaths);});"
            startPath
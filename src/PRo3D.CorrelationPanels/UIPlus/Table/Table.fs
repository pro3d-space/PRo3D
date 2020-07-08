namespace UIPlus

open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.UI
open UIPlus
open UIPlus.TableTypes

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Table =

    let init (mapper : TableRow<'a,'b,'c,'d, 'e>) colHeadings : Table<'a,'b,'c,'d, 'e> =
        {
            mapper      = mapper
            colHeadings = colHeadings
        }
    
    let view  
        (guiModel  : Table<'dtype, 'arg, 'mtype, 'action, 'parentaction>)
        (data      : alist<'mtype>) 
        (args      : alist<'arg>) =

        let header = 
            guiModel.colHeadings
            |> List.map (fun str -> th[] [text str])
        
        let rows = 
            let zipped = DS.AList.zip data args
            alist {
                for tuple in zipped do
                    let (dat, arg) = tuple
                    let res = TableRow.view guiModel.mapper arg dat
                    yield! res
            }
        require (GUI.CSS.myCss) (
            table
                ([clazz "ui celled striped inverted table unstackable"; style "padding: 1px 5px 2px 5px"]) (
                    [
                        thead [][tr[] header]
                        Incremental.tbody  (AttributeMap.ofList []) rows
                    ]
                )
        )

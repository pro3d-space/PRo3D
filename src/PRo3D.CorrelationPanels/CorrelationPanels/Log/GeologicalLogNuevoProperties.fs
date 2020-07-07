namespace CorrelationDrawing.Nuevo

open Aardvark.Base
open System
open Aardvark.UI
open Aardvark.UI.Static.Svg
open Aardvark.Base.Incremental
open PRo3D
open PRo3D.Base

module GeologicalLogNuevoProperties =
    
    type Action =
    | SetName of string

    let update (action : Action) (log : GeologicalLogNuevo) =
        match action with
        | SetName name ->
            { log with name = name }

    let view (log : MGeologicalLogNuevo) =

        require GuiEx.semui (
            Html.table [                            
                Html.row "Name:"[Html.SemUi.textBox log.name SetName]
            ]
        )


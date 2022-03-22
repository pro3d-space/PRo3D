module Index

open Elmish
open Fable.Remoting.Client
open Shared

type Model = { Todos: Todo list; Input: string }

type Msg =
    | GotTodos of Todo list
    | SetInput of string
    | AddTodo
    | AddedTodo of Todo
    | CenterScene
    | Nop

let todosApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<Pro3d>

let init () : Model * Cmd<Msg> =
    let model = { Todos = []; Input = "" }

    let cmd = Cmd.OfAsync.perform todosApi.getTodos () GotTodos

    model, cmd

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Nop -> model, Cmd.Empty
    | CenterScene ->
        let cmd = Cmd.OfAsync.perform todosApi.centerScene () (fun _ -> Nop)

        { model with Input = "" }, cmd
    | GotTodos todos -> { model with Todos = todos }, Cmd.none
    | SetInput value -> { model with Input = value }, Cmd.none
    | AddTodo ->
        let todo = Todo.create model.Input

        let cmd = Cmd.OfAsync.perform todosApi.addTodo todo AddedTodo

        { model with Input = "" }, cmd
    | AddedTodo todo -> { model with Todos = model.Todos @ [ todo ] }, Cmd.none

open Feliz
open Feliz.Bulma

let navBrand =
    Bulma.navbarBrand.div [
        Bulma.navbarItem.a [
            prop.href "https://pro3d.space/"
            //navbarItem.isActive
            prop.children [
                //Html.img [
                //    prop.src "/pro3d-favicon.png"
                //    prop.alt "Logo"
                //]
                Html.div [prop.style [style.width 40]]
                Html.text "http://pro3d.space"
            ]
        ]
    ]

let containerBox (model: Model) (dispatch: Msg -> unit) =
    Bulma.box [
        Bulma.content [
            Html.ol [
                for todo in model.Todos do
                    Html.li [ prop.text todo.Description ]
            ]
        ]
        Bulma.field.div [
            field.isGrouped
            prop.children [
                Bulma.control.p [
                    control.isExpanded
                    prop.children [
                        Bulma.input.text [
                            prop.value model.Input
                            prop.placeholder "What needs to be done?"
                            prop.onChange (fun x -> SetInput x |> dispatch)
                        ]
                    ]
                ]
                Bulma.control.p [
                    Bulma.button.a [
                        color.isPrimary
                        prop.disabled (Todo.isValid model.Input |> not)
                        prop.onClick (fun _ -> dispatch AddTodo)
                        prop.text "Add"
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Bulma.hero [
        hero.isFullHeight
        color.isPrimary
        prop.style [
            style.backgroundSize "cover"
            style.backgroundImageUrl "./Mars_Valles_Marineris_EDIT.jpg"
            style.backgroundPosition "no-repeat center center fixed"
        ]
        prop.children [
            Bulma.heroHead [
                Bulma.navbar [
                navBrand
                //Bulma.container [ navBrand ]
                ]
            ]
            Bulma.heroBody [
                Html.div [
                    prop.style [
                        style.border(1, borderStyle.solid, "white")
                    ]
                    prop.className "wrap"
                    prop.children [
                        Html.iframe [prop.src "http://localhost:8085/render/?view=lite"; prop.width 800; prop.height 800; ]
                    ]
                ]
                Bulma.container [
                    Bulma.column [
                        column.is6
                        column.isOffset3
                        prop.children [
                            Bulma.title [
                                text.hasTextCentered
                                prop.text "PRo3D.Api"
                            ]
                            Bulma.field.div [
                                Bulma.button.a [
                                    color.isPrimary
                                    //prop.disabled (Todo.isValid model.Input |> not)
                                    prop.onClick (fun _ -> dispatch CenterScene)
                                    prop.text "Center Scene"
                                ]
                            ]
                            //containerBox model dispatch
                        ]
                    ]
                ]
            ]
        ]
    ]
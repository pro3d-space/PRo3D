namespace UIPlus
  open Aardvark.Base
  open Aardvark.Base.Incremental
  open Aardvark.UI
  open Aardvark.Application
  open UIPlus.KeyboardTypes

  module Keyboard =
    type Action =
      | KeyDown of key : Keys
      | KeyUp   of key : Keys     

    let init () : Keyboard<'a> =
      {
        altPressed        = false
        ctrlPressed       = false
        registeredKeyUp   = PList.empty
        registeredKeyDown = PList.empty
      }

    let registerKeyUp  (k : KeyConfig<'a>) 
                       (model : Keyboard<'a>) =
      let _reg = model.registeredKeyUp.Prepend(k)
      {model with registeredKeyUp = _reg}

    let registerKeyDown  (k : KeyConfig<'a>) 
                         (model : Keyboard<'a>) =
      let _reg = model.registeredKeyDown.Prepend(k)
      {model with registeredKeyDown = _reg}

    let registerKeyDownAndUp (down  : KeyConfig<'a>)
                             (up    : 'a -> 'a)
                             (model : Keyboard<'a>) =
      let _regDown = model.registeredKeyDown.Prepend(down)
      let _up = {down with update = up}
      let _regUp = model.registeredKeyUp.Prepend(_up)
      {model with registeredKeyUp  = _regUp
                  registeredKeyDown = _regDown}

    let register (k       : KeyConfig<'a>) 
                 (model   : Keyboard<'a>) =
      let _reg = model.registeredKeyDown.Prepend(k)
      {model with registeredKeyDown = _reg}

    let update (model : Keyboard<'a>) 
               (app   : 'a)
               (action : Action) =
       let _model = 
         match action with
           | KeyDown Keys.LeftAlt
           | KeyDown Keys.RightAlt ->
            {model with altPressed = true}
           | KeyUp Keys.LeftAlt
           | KeyUp Keys.RightAlt ->
            {model with altPressed = false}
           | KeyDown Keys.LeftCtrl
           | KeyDown Keys.RightCtrl ->
            {model with ctrlPressed = true}
           | KeyUp Keys.LeftCtrl
           | KeyUp Keys.RightCtrl ->
            {model with ctrlPressed = false}
           | KeyUp _ -> model
           | KeyDown _ -> model
        
       let _app = 
         match action with
          | KeyUp _ -> app
          | KeyDown k ->
            //Log.line "Key Pressed: %s" (k.ToString ())
            let _filtered = PList.filter (fun (c : KeyConfig<'a>) -> 
                                            c.check
                                                _model.ctrlPressed
                                                _model.altPressed
                                                k
                                         )
                                          _model.registeredKeyDown
            match _filtered.IsEmpty () with
             | true  -> app
             | false ->
               let config = _filtered.Item 0 //TODO: only taking first, could execute list
               config.update app
       (_model, _app)


    //let view (model : Keyboard<'a>) =
    //TODO create list view of registered keyboard actions with descriptions

           


      
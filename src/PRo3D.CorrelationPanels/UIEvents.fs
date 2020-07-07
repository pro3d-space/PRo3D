namespace Aardvark.UI

  open Aardvark.Base

  module Event =
      let processEvent (name : string) (id : string) (args : list<string>) =
        let args = sprintf "'%s'" id :: sprintf "'%s'" name :: args
        sprintf "aardvark.processEvent(%s); evt.preventDefault();" (String.concat ", " args)

      let toString (id : string) (name : string) (evt : Event<'msg>) =
        let send = processEvent name
        evt.clientSide send id

  
      let toggleAttribute (cb : list<string> -> 'msg) : list<Attribute<'msg>>  =
        //let js = "$('__ID__').toggleClass('active');aardvark.processEvent('__ID__','foobar');"
        [              
            onEvent "foobar" [] cb
            clientEvent "onclick" ("aardvark.processEvent('__ID__','foobar');")
            //clientEvent "onclick" (js)
        ] 

  module Svg =
    let mutable eventCounter = 0
    module Events =
      let inline onEvent (eventType : string) (args : list<string>) (cb : list<string> -> 'msg) : Attribute<'msg> = 
        eventType, AttributeValue.Event(Event.ofDynamicArgs args (cb >> Seq.singleton))

      //let inline onEvent' (eventType : string) (args : list<string>) (cb : list<string> -> seq<'msg>) : Attribute<'msg> = 
      //    eventType, AttributeValue.Event(Event.ofDynamicArgs args (cb))
      
      let onClick (cb : _ -> 'msg) : Attribute<'msg>  =  ////WORKS
          let args = ["{ X: evt.clientX, Y: evt.clientY  });evt.preventDefault();//"]
          "onclick", AttributeValue.Event(Event.ofDynamicArgs [] (cb >> Seq.singleton))

      let onClickToggleButton (cb : list<string> -> 'msg) : list<Attribute<'msg>>  =
        
        [ 
            onEvent "foobar" [] cb
            clientEvent "onclick" ("aardvark.processEvent('__ID__', 'foobar');$('#__ID__').state();")
        ] 




      let onClickAttributes (cbs : list<list<string> -> 'msg>) : list<Attribute<'msg>>  =
        let fNames = 
          seq {
            for i in 1..cbs.Length do
              yield sprintf "userfunction%i" eventCounter
              eventCounter <- eventCounter + 1
          } |> List.ofSeq

        let att (fname : string) (cb : list<string> -> 'msg) = 
          let evStr = sprintf "aardvark.processEvent('__ID__', '%s');" fname
          [
            onEvent fname [] cb
            clientEvent "onclick" evStr
          ]
         
        List.map2 (fun c s -> att s c) cbs fNames 
          |> List.concat
        
                   
        //  let cb = Pickler.json.UnPickleOfString >> args 
        //let args = ["{ X: evt.clientX, Y: evt.clientY  });evt.preventDefault();//"]
        //"onclick", AttributeValue.Event(Event.ofDynamicArgs args (cb >> Seq.singleton))
        //clientEvent "onclick"  ("aardvark.processEvent('__ID__', 'onclick', { X: event.clientX, Y: event.clientY  }); event.preventDefault(););") 

      //let onClick (cb : V2i -> 'msg) = 
      //    onEvent "onclick" 
      //            ["{ X: evt.clientX, Y: evt.clientY  });evt.preventDefault();//"]
      //            (List.head >> Pickler.json.UnPickleOfString >> cb)

      //let onClick (cb : V2i -> 'msg) = 
      //    onEvent "onclick" 
      //            ["{ X: evt.clientX, Y: evt.clientY  });evt.preventDefault();//"]
      //            (List.head >> Pickler.json.UnPickleOfString >> cb)

      //let onClick' (cb : V2i -> 'msg) = 
      //    onEvent "onclick" 
      //            ["{ X: boo.clientX, Y: boo.clientY  }"] 
      //            (List.head >> Pickler.json.UnPickleOfString >> cb)
              //clientEvent "onclick" ("aardvark.processEvent('__ID__', 'onclick', aardvark.dialog.showOpenDialog({properties: ['openFile', 'openDirectory', 'multiSelections']}));") 
        //let cb = Pickler.json.UnPickleOfString >> cb 


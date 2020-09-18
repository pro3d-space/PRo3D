namespace PRo3D.Base

open System
open System.IO

#nowarn "8989"

open MBrace.FsPickler
open MBrace.FsPickler.Json

open Chiron

module Serialization =
        
    let mutable registry : CustomPicklerRegistry = Unchecked.defaultof<_> //new CustomPicklerRegistry()    
    
    let mutable cache = Unchecked.defaultof<_> 
    
    let mutable binarySerializer = Unchecked.defaultof<_>
    let mutable jsonSerializer = Unchecked.defaultof<_> //  FsPickler.CreateJsonSerializer(indent=true) // 
    
    let init () = 
      registry <- new CustomPicklerRegistry()    
      cache <- PicklerCache.FromCustomPicklerRegistry registry
      binarySerializer <- FsPickler.CreateBinarySerializer(picklerResolver = cache)
      jsonSerializer <- FsPickler.CreateJsonSerializer(indent = true, picklerResolver = cache)
    
    let save path (d : 'a) =    
        let arr =  binarySerializer.Pickle d
        File.WriteAllBytes(path, arr);
        d
    
    let loadAs<'a> path : 'a =
        let arr = File.ReadAllBytes(path)
        binarySerializer.UnPickle arr
    
    let writeLinesToFile path (contents : list<string>) =
        System.IO.File.WriteAllLines(path, contents)
    
    let writeToFile path (contents : string) =
        System.IO.File.WriteAllText(path, contents)
    
    let readFromFile path =
        System.IO.File.ReadAllText(path)
    
    let saveJson path (d : 'a) =    
        let json =  jsonSerializer.PickleToString d
        writeToFile path json
        d
    
    let loadJsonAs<'a> path : 'a =
        let json = System.IO.File.ReadAllText path //readAllText path
        let test2 : 'a = jsonSerializer.UnPickleOfString<'a> json
        test2
        
    let fileExists filePath =
        match System.IO.File.Exists filePath with
        | true  -> Some filePath
        | false -> None

    let changeExtension (ext:string) (scenePath:string) = System.IO.Path.ChangeExtension(scenePath,ext)
        //let p = System.IO.Path.GetDirectoryName(scenePath)
        //let name = System.IO.Path.GetFileNameWithoutExtension(scenePath) + ext
        //p + "\\" + name

    module Chiron =
        let writeToFile path (contents : string) =
            System.IO.File.WriteAllText(path, contents)
        
        let readFromFile path =
            System.IO.File.ReadAllText(path)  
            
        //let loadAs<'a> path =
        //    let parsed : 'a =
        //        path
        //        |> readFromFile 
        //        |> Json.parse 
        //        |> Json.deserialize
        //    parsed

  //let writeAsJson<'a> path (data : 'a) =
  //  data |> Json.serialize |> Json.formatWith JsonFormattingOptions.SingleLine |> writeToFile path

namespace PRo3D.Viewer

open Aardvark.Base
open Adaptify
open Chiron
open PRo3D.Base

[<ModelType>]
type Traverse = 
    {
        version   : int
        positions : List<V3d>
    }

module Traverse =

    let current = 0 
    let read0 = 
        json {               
            let! positions   = Json.readWith Ext.fromJson<list<V3d>,Ext> "positions"

            return 
                {
                    version   = current
                    positions = positions
                }

        }

    let initial = 
        { 
            version = current
            positions = [] 
        }
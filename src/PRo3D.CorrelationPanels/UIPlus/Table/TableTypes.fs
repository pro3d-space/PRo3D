namespace UIPlus.TableTypes


open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open CorrelationDrawing


type TableItemId = {
  id            : System.Guid 
}

module TableItemId = 
    let newId unit : TableItemId  = 
        {
            id = System.Guid.NewGuid()
        }

type TableRowId = {
    id : System.Guid 
}

module TableRowId = 
    let newId unit : TableRowId  = 
        {
            id = System.Guid.NewGuid()
        }

type TableRow<'dtype, 'mtype, 'arg, 'action, 'parentaction> =
    {
        isSelected    : 'mtype -> IMod<bool>
        update        : 'dtype -> 'action -> 'dtype
        displayView   : 'arg   -> 'mtype -> list<DomNode<'action>>
        editView      : 'arg   -> 'mtype -> list<DomNode<'action>>
        onSelect      : 'action
        align         : 'mtype -> Alignment
        actionMapping : 'mtype -> DomNode<'action> -> DomNode<'parentaction>
    }

type Table<'dtype, 'mtype, 'arg, 'action, 'parentaction>  =
    {
        mapper      : TableRow<'dtype, 'mtype, 'arg, 'action, 'parentaction>
        colHeadings : list<string>
    }


 


namespace PRo3D

open Aardvark.Base
open Aardvark.UI
open PRo3D.Comparison
open FSharp.Data.Adaptive
open PRo3D.Base
open Aardvark.UI
open PRo3D.Core
open PRo3D.SurfaceUtils
open PRo3D.Core.Surface
open PRo3D.Base
open Aardvark.Rendering
open Adaptify.FSharp.Core
open PRo3D.Comparison
open System
open PRo3D.Base.Annotation


module AnnotationComparison =
    let compareAnnotationMeasurements (surface1     : string)
                                      (surface2     : string)
                                      (annotations  : HashMap<Guid, Annotation>) 
                                      (bookmarks    : HashMap<Guid, Bookmark>) =
        let annotationToMeasurement (annotation : Annotation) =
            match annotation.results with
            | Some results -> 
                {
                    annotationKey   = annotation.key
                    text            = annotation.text
                    length          = results.wayLength
                } |> Some
            | None -> 
                Log.warn "[Comparison] No annotation measurements available."
                None

        let toMeasurement bookmarkId (annotations : list<Annotation>) =
            let bookmark = HashMap.tryFind bookmarkId bookmarks
            match bookmark with
            | Some bookmark -> 
                let measurement1 = annotations 
                                     |> List.filter (fun annotation -> annotation.surfaceName = surface1)
                                     |> List.tryHead
                                     |> Option.bind annotationToMeasurement
                let measurement2 = annotations 
                                     |> List.filter (fun annotation -> annotation.surfaceName = surface2)
                                     |> List.tryHead
                                     |> Option.bind annotationToMeasurement

                let difference = Option.map2 (fun (m1 : AnnotationMeasurement)
                                                  (m2 : AnnotationMeasurement)
                                                    -> Math.Abs (m1.length - m2.length)) 
                                             measurement1 measurement2
                {
                    bookmarkName   = bookmark.name
                    bookmarkId     = bookmark.key
                    measurement1   = measurement1    
                    measurement2   = measurement2
                    difference     = difference
                } |> Some
            | None -> None

        let bookmarkIdAnnotations = 
            annotations
                |> HashMap.filter (fun id anno -> anno.bookmarkId.IsSome)
                |> HashMap.toValueList
                |> List.map (fun a -> a.bookmarkId.Value, a)
                |> List.groupBy (fun (bmId,a) -> bmId)
                |> List.map (fun (g, lst) -> g, List.map snd lst)

        bookmarkIdAnnotations
            |> List.map (fun (id, annotations) -> toMeasurement id annotations)
            |> List.filter Option.isSome
            |> List.map (fun x -> x.Value) 

    let view (surface1 : string) 
             (surface2 : string)
             (m : AnnotationComparison) =
        let surfaceRow (m1 : option<AnnotationMeasurement>) s1 =
            let value = 
                match m1 with
                | Some m1 -> sprintf "%f" m1.length
                | None    -> "-"
            Html.row s1 [text value]
        let differenceRow =
            let differenceString = 
                match m.difference with
                  | Some diff -> sprintf "%f" diff
                  | None    -> "-"
            Html.row "Difference" [text differenceString]
        let content m s1 s2 =
            require GuiEx.semui (
              Html.table [      
                Html.row "Bookmark" [text m.bookmarkName]
                surfaceRow m.measurement1 s1
                surfaceRow m.measurement2 s2
                differenceRow
            ])
        
        content m surface1 surface2
            
         

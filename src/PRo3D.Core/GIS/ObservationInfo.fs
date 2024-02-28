namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.Base.Gis
open Aether

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ObservationInfo =

    let update (m : ObservationInfo) (msg : ObservationInfoAction) =
        match msg with
        | ObservationInfoAction.CalendarMessage msg ->
            {m with time = Calendar.update m.time msg}
        | ObservationInfoAction.SetTarget target ->
            {m with target = target}
        | ObservationInfoAction.SetObserver observer ->
            {m with observer = observer}
        | ObservationInfoAction.SetTime time ->
            {m with time = {m.time with date = time}}
        | ObservationInfoAction.SetReferenceFrame frame ->
            {m with referenceFrame = frame}

    let view (m : AdaptiveObservationInfo)
             (entities : amap<EntitySpiceName, AdaptiveEntity>) 
             (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) =

        let observerDropdown =
            UI.dropDownWithEmptyText
                (entities 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map fst)
                m.observer
                (fun x -> SetObserver x)  
                (fun x -> x.Value)
                "Select Observer"
        let targetDropdown =
            UI.dropDownWithEmptyText
                (entities 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map fst)
                m.target
                (fun x -> SetTarget x)  
                (fun x -> x.Value)
                "Select Target"

        let frameDropdown =
            UI.dropDownWithEmptyText
                (referenceFrames 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map snd)
                m.referenceFrame
                (fun x -> ObservationInfoAction.SetReferenceFrame x)
                (fun x -> x.spiceName.Value)
                "Select Frame"

        require GuiEx.semui (
            Html.table [                                                
                Html.row "Observer:" [observerDropdown]
                Html.row "Target:"   [targetDropdown]
                Html.row "Time:"     
                    [
                        Calendar.view m.time false false 
                                      Calendar.CalendarType.DateTime
                    ] |> UI.map CalendarMessage
                Html.row "Reference Frame:" [frameDropdown]
            ]
        )

    let initial = 
        {
            target         = None
            observer       = None
            time           = Calendar.init
            referenceFrame = None
        }


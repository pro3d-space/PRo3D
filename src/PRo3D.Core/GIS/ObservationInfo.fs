namespace PRo3D.Core.Gis


open Aardvark.Base
open Aardvark.UI
open PRo3D.Base
open PRo3D.Core
open FSharp.Data.Adaptive
open PRo3D.Core.Surface
open PRo3D.Base.GisModels
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
             (entities : amap<System.Guid, Entity>) 
             (referenceFrames : amap<FrameSpiceName, ReferenceFrame>) =

        let entityDropdownText entity = 
            match Entity.spiceName entity with
            | Some name -> name
            | None      -> "---"

        let observerDropdown =
            UI.dropDownWithEmptyText
                (entities 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map snd)
                m.observer
                (fun x -> SetObserver x)  
                (fun x -> entityDropdownText x)
                "Select Observer"
        let targetDropdown =
            UI.dropDownWithEmptyText
                (entities 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map snd)
                m.target
                (fun x -> SetTarget x)  
                (fun x -> entityDropdownText x)
                "Select Target"

        let frameDropdown =
            UI.dropDownWithEmptyText
                (referenceFrames 
                    |> AMap.toASet
                    |> ASet.toAList
                    |> AList.map snd)
                m.referenceFrame
                (fun x -> SetReferenceFrame x)
                (fun x -> x.spiceName.Value)
                "Select Frame"

        require GuiEx.semui (
            Html.table [                                                
                Html.row "Observer:" [observerDropdown]
                Html.row "Target:"   [targetDropdown]
                //Html.row "Time:"     [Calendar.view m.time false false]
                //    |> UI.map CalendarMessage
                Html.row "Reference Frame:" [frameDropdown]
            ]
        )

    let inital = 
        {
            target         = None
            observer       = None
            time           = Calendar.init
            referenceFrame = None
        }


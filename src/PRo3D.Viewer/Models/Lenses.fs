namespace PRo3D

open Aardvark.UI.Primitives
open PRo3D.Core

open Aether
open Aether.Operators

module LenseConfigs =

    let referenceSystemConfig : ReferenceSystemConfig<ViewConfigModel> =
        { 
            arrowLength    = ViewConfigModel.arrowLength_    >-> NumericInput.value_
            arrowThickness = ViewConfigModel.arrowThickness_ >-> NumericInput.value_
            nearPlane      = ViewConfigModel.nearPlane_      >-> NumericInput.value_
        }
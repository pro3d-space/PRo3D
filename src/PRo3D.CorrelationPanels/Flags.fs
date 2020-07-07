namespace CorrelationDrawing

  [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
  module Flags =
    //let isSet (flag  : 'a when 'a : (static member (|||) : 'a * 'a -> 'a) and 'a : equality)
    //          (flags : 'a when 'a : (static member (|||) : 'a * 'a -> 'a) and 'a : equality) =
    let isSet flag flags =
      let flagVal = Microsoft.FSharp.Core.LanguagePrimitives.EnumToValue(flag)
      let flagsVal = Microsoft.FSharp.Core.LanguagePrimitives.EnumToValue(flags)
      (flagsVal ||| flagVal) = flagsVal

    let parse (t : System.Type) str = //TODO make safer
      //((System.Enum.Parse(typeof<'a>, str)) :?> 'a)
      ((System.Enum.Parse(t, str)) :?> 'a)

    let toggle (flag : 'a) (flags : 'a) = //(flag : 'a when 'a:enum<int32>) (flags : 'a when 'a : enum<int32>) : 'a when 'a : enum<int32> =
      let flagVal = Microsoft.FSharp.Core.LanguagePrimitives.EnumToValue(flag)
      let flagsVal = Microsoft.FSharp.Core.LanguagePrimitives.EnumToValue(flags)

      let toggled = 
        match (isSet flag flags) with
          | true  -> flagsVal &&& (~~~flagVal)
          | false -> flagVal ||| flagsVal
      let v : 'a = Microsoft.FSharp.Core.LanguagePrimitives.EnumOfValue toggled
      v





      ////WIP 
 ////TEST
//let flags = LogSvgFlags.YAxis
//let f1 = Flags.toggle LogSvgFlags.BorderColour flags
//let isSet = Flags.isSet LogSvgFlags.BorderColour f1
//let f2 = Flags.toggle LogSvgFlags.RadialDiagrams f1
//let isSet1 = Flags.isSet LogSvgFlags.BorderColour f2
//let isSet2 = Flags.isSet LogSvgFlags.RadialDiagrams f2
//let foo = f2 &&& (~~~LogSvgFlags.BorderColour)
//let f3 = Flags.toggle LogSvgFlags.BorderColour f2
//let isSet3 = Flags.isSet LogSvgFlags.BorderColour f3
//let isSet4 = Flags.isSet LogSvgFlags.RadialDiagrams f3
//let f4 = SgFlags.ShowLogCorrelations
//let isSet5 = Flags.isSet SgFlags.ShowLogCorrelations f4
//let a1 = FSharp.Core.LanguagePrimitives.EnumToValue f4

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

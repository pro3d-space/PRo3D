namespace PRo3D.Base

open Aardvark.Base

module TransferFunction =
    
    type TF = { pi : PixImage<byte> }

    let loadTexture (name : string) =
        lazy
            let stream = typeof<PRo3D.Base.ColorMaps.Marker>.Assembly.GetManifestResourceStream(sprintf "PRo3D.Base.resources.%s" name)
            PixImage.Load(stream)

    let plasmaTF = loadTexture "plasma.png"


    let transferPlasma (normalizedValue : float) : C3b =
        let img = plasmaTF.Value |> unbox<PixImage<byte>>
        let px = int ((float img.Size.X - 1.0) * normalizedValue)
        img.GetMatrix<C3b>()[px,0]
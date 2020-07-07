namespace Boxes

open Aardvark.Base

module Calc =

    //based on JAMA library for Java, public domain; and eig3.cpp by Connelly Barnes, public domain
    let n = 3
    let hypot2 (x : float, y : float) = System.Math.Sqrt(x*x+y*y)
    
    let tred2 (V : float[][]) (d : float[]) (e : float[]) =
        for j in 0 .. n-1 do
            d.[j] <- V.[n-1].[j]
        
        for i in n-1 .. -1 .. 1 do
            let mutable scale = 0.0
            let mutable h = 0.0

            for k in 0 .. i-1 do
                scale <- scale + System.Math.Abs(d.[k])
            
            if scale = 0.0
            then
                e.[i] <- d.[i-1]
                for j in 0 .. i-1 do
                    d.[j] <- V.[i-1].[j]
                    V.[i].[j] <- 0.0
                    V.[j].[i] <- 0.0
            else
                for k in 0 .. i-1 do
                    d.[k] <- d.[k] / scale
                    h <- h + d.[k]*d.[k]
                
                let mutable f = d.[i-1]
                let mutable g = System.Math.Sqrt h
                if f > 0.0 then g <- -g

                e.[i] <- scale * g
                h <- h - f * g
                d.[i-1] <- f - g
                for j in 0 .. i-1 do
                    e.[j] <- 0.0
                
                for j in 0 .. i-1 do
                    f <- d.[j]
                    V.[j].[i] <- f
                    g <- e.[j] + V.[j].[j] * f
                    for k in j+1 .. i-1 do
                        g <- g + V.[k].[j] * d.[k]
                        e.[k] <- e.[k] + V.[k].[j] * f
                    e.[j] <- g
                
                f <- 0.0
                for j in 0 .. i-1 do
                    e.[j] <- e.[j] / h
                    f <- f + e.[j] * d.[j]
                
                let mutable hh = f / (h+h)
                for j in 0 .. i-1 do
                    e.[j] <- e.[j] - hh * d.[j]
                
                for j in 0 .. i-1 do
                    f <- d.[j]
                    g <- e.[j]
                    for k in j .. i-1 do
                        V.[k].[j] <- V.[k].[j] - (f * e.[k] + g * d.[k])
                    d.[j] <- V.[i-1].[j]
                    V.[i].[j] <- 0.0
                
            d.[i] <- h
        
        for i in 0 .. n-2 do
            V.[n-1].[i] <- V.[i].[i]
            V.[i].[i] <- 1.0
            let mutable h = d.[i+1]

            if h <> 0.0
            then
                for k in 0 .. i do
                    d.[k] <- V.[k].[i+1] / h
                for j in 0 .. i do
                    let mutable g = 0.0
                    for k in 0 .. i do
                        g <- g + V.[k].[i+1] * V.[k].[j]
                    for k in 0 .. i do
                        V.[k].[j] <- V.[k].[j] - g * d.[k]
            
            for k in 0 .. i do
                V.[k].[i+1] <- 0.0
        
        for j in 0 .. n-1 do
            d.[j] <- V.[n-1].[j]
            V.[n-1].[j] <- 0.0
        
        V.[n-1].[n-1] <- 1.0
        e.[0] <- 0.0
    
    let tql2 (V : float[][]) (d : float[]) (e : float[]) =
        for i in 1 .. n-1 do
            e.[i-1] <- e.[i]
        e.[n-1] <- 0.0
        
        let mutable f = 0.0
        let mutable tst1 = 0.0
        let mutable eps = System.Math.Pow(2.0,-52.0)
        
        for l in 0 .. n-1 do
            tst1 <- System.Math.Max(tst1, System.Math.Abs(d.[l]) + System.Math.Abs(e.[l]))
            let mutable m = l
            let mutable brk = false
            while not brk do
                if (System.Math.Abs(e.[m]) <= eps*tst1)
                then brk <- true
                else m <- m + 1
            
            if m > l
            then
                let mutable iter = 0
                while (System.Math.Abs(e.[l]) > eps*tst1) || iter = 0 do
                    iter <- iter + 1
        
                    let mutable g = d.[l]
                    let mutable p = (d.[l+1] - g) / (2.0 * e.[l])
                    let mutable r = hypot2(p,1.0)
                    if p<0.0 then r <- -r
                    
                    d.[l] <- e.[l] / (p + r)
                    d.[l+1] <- e.[l] * (p + r)
                    let mutable dl1 = d.[l+1]
                    let mutable h = g - d.[l]
                    for i in l+2 .. n-1 do
                        d.[i] <- d.[i] - h
                    
                    f <- f + h
                    p <- d.[m]
                    let mutable c = 1.0
                    let mutable c2 = c
                    let mutable c3 = c
                    let mutable el1 = e.[l+1]
                    let mutable s = 0.0
                    let mutable s2 = 0.0
                    
                    for i in m-1 .. -1 .. l do
                        c3 <- c2
                        c2 <- c
                        s2 <- s
                        g <- c * e.[i]
                        h <- c * p
                        r <- hypot2(p, e.[i])
                        e.[i+1] <- s * r
                        s <- e.[i] / r
                        c <- p / r
                        p <- c * d.[i] - s * g
                        d.[i+1] <- h + s * (c * g + s * d.[i])
        
                        for k in 0 .. n-1 do
                            h <- V.[k].[i+1]
                            V.[k].[i+1] <- s * V.[k].[i] + c * h
                            V.[k].[i] <- c * V.[k].[i] - s * h
                    
                    p <- -s * s2 * c3 * el1 * e.[l] / dl1
                    e.[l] <- s * p
                    d.[l] <- c * p
            
            d.[l] <- d.[l] + f
            e.[l] <- 0.0
    
    let eigen_decomposition (A : float[][]) (V : float[][]) (d : float[]) =
        let mutable e = Array.create 3 0.0

        for i in 0 .. n-1 do
            for j in 0 .. n-1 do
                V.[i].[j] <- A.[i].[j]
        
        tred2 V d e
        tql2 V d e

module PCA =
    
    let mean (pts : V3d list) =
        let v = pts |> List.fold ( fun a v -> a + v ) V3d.OOO
        (1.0 / (float pts.Length)) * v
    
    let cov (pts : V3d list) =
        let avg = mean pts
        let factor = 1.0 / ((float pts.Length) - 1.0)
        let diff = pts  |> List.map  ( fun v -> v - avg )
        let mul = diff |> List.map  ( fun v -> M33d.OuterProduct(v,v) )
        let mat = mul  |> List.fold ( fun a m -> a + m ) M33d.Zero
        factor * mat
    
    let eig (m  : M33d) : (float list * V3d list) =
        let mutable A =
            [|
                [|m.M00; m.M01; m.M02|]
                [|m.M10; m.M11; m.M12|]
                [|m.M20; m.M21; m.M22|]
            |]
        
        let mutable V =
            [|
                [|0.0; 0.0; 0.0|]
                [|0.0; 0.0; 0.0|]
                [|0.0; 0.0; 0.0|]
            |]
        
        let mutable d = [|0.0; 0.0; 0.0|]
        
        Calc.eigen_decomposition A V d

        let e0 = d.[0]
        let e1 = d.[1]
        let e2 = d.[2]
        let v0 = V3d(V.[0].[0], V.[1].[0], V.[2].[0])
        let v1 = V3d(V.[0].[1], V.[1].[1], V.[2].[1])
        let v2 = V3d(V.[0].[2], V.[1].[2], V.[2].[2])
        let vals = [e0;e1;e2]
        let vecs = [v0;v1;v2]
        (vals, vecs)
    
    let pca (pts : V3d list) =
        let cm = cov pts
        let (vals, vecs) = eig cm
        vecs
    
    let fitBox (pts : V3d list) =
        let vecs = pca pts
        let rot = (Rot3d.FromFrame(vecs.[0], vecs.[1], vecs.[2])).GetEulerAngles()
        let rotation = Trafo3d.Rotation(rot)
        let invRotPts = pts |> List.map ( fun p -> rotation.Inverse.Forward.TransformPos(p) )
        let aaBox = Box3d(invRotPts |> List.toArray)
        let aaCenter = aaBox.Center
        let center = rotation.Forward.TransformPos(aaCenter)
        let size = aaBox.Size
        
        (center, size, rot)
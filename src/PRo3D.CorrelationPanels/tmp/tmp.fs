namespace tmp



  module tmp =
    open System

    let gs = [
              0.628    
              0.16     
              0.048    
              0.024    
              0.012    
              0.006    
              0.003    
              0.0015   
              0.00075  
              0.000375 
              0.0001875
              7e-05    
              9.4e-06  
              2.39e-06 
              1.2e-06  
            ]

    let logpow x = 
      //let xsvg = -Math.Log(x, 2.0)
      //let foo =  (21.0 + Math.Log(x,2.0)) * 20.0
      let foo =  (21.0 + Math.Log(x,2.0)) * 10.0
      //printfn "%f" xsvg
      printfn "%f" foo
      //let xlog = (Math.Pow (2.0, (foo - 21.0) * 0.02)) 
      let xlog = (Math.Pow (2.0, (foo * 0.1 - 21.0))) 
      printfn "%f" xlog
      //assert (x = xlog)
      ()

    for el in gs do
      logpow el



    

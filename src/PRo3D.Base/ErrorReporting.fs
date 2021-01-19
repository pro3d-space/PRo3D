namespace PRo3D

open System.Linq
open System
open System.Diagnostics

module CrashDumps = 

    open Microsoft.Diagnostics.Runtime


    let createDump () = 

        use dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
        use runtime = dt.ClrVersions.Single().CreateRuntime();

        for thread in runtime.Threads do
            printfn "thread id: %xd" thread.OSThreadId
            for frame in thread.EnumerateStackTrace() do
                printfn "%A" frame

        ()
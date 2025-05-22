module RunAdaptifyProcess

open System.IO

// adapted from here: https://fssnip.net/sw/title/RunProcess
module Process =
    open System
    open System.Diagnostics

    let runProc filename args startDir loggingName= 
        printfn "running process {%s} ('%s' with args '%s' in '%A')" loggingName filename args startDir
        let timer = Stopwatch.StartNew()
        let procStartInfo = 
            ProcessStartInfo(
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                FileName = filename,
                Arguments = args
            )
        match startDir with | Some d -> procStartInfo.WorkingDirectory <- d | _ -> ()

        let outputHandler f (_sender:obj) (args:DataReceivedEventArgs) = f args.Data
        let p = new Process(StartInfo = procStartInfo)
        p.OutputDataReceived.AddHandler(DataReceivedEventHandler (outputHandler (printfn "[%s] %s"loggingName)))
        p.ErrorDataReceived.AddHandler(DataReceivedEventHandler (outputHandler (printfn "[%s] %s"loggingName)))
        let started = 
            try
                p.Start()
            with | ex ->
                ex.Data.Add("filename", filename)
                reraise()
        if not started then
            failwithf "Failed to start process %s" filename
        printfn "Started %s with pid %i" p.ProcessName p.Id
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()
        timer.Stop()
        $"{loggingName} returned {p.ExitCode} in {timer.ElapsedMilliseconds}." 


let run (projFile : string) = 
    let repositoryRoot = Path.Combine(__SOURCE_DIRECTORY__, "..")
    Process.runProc "dotnet" $"adaptify --lenses --local --force {projFile}" (Some repositoryRoot) "RunAdaptifyProcess"
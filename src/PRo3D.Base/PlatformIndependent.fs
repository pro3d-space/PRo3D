namespace PRo3D.Base 

open System.Runtime.InteropServices
open System.Diagnostics
open Aardvark.Base

module PlatformIndependent =
    /// use this one to get path to self-contained exe (not temp expanded dll)
    let getPathBesideExecutable () =
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            System.Environment.GetCommandLineArgs().[0]
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
            System.Environment.GetCommandLineArgs().[0]
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Process.GetCurrentProcess().MainModule.FileName
        else 
            Log.warn "could not detect os platform.. assuming linux"
            System.Environment.GetCommandLineArgs().[0]



- readonly filesystem
```
try
    trees |> save cacheFile b |> ignore
with e -> 
    Log.warn "[KdTrees] could not save LazyKdTree to %s" cacheFile
    Log.warn "the exception is: %A" e.Message
    Log.warn "Maybe this is a readonly file system? We often see this with NTFS disks and macs. I will continue without the kdtree cache, but"
    Log.warn "be aware that reloading the surface requires re-creation of the LazyKdTree"
```

 9: there are missing Kd0Paths. The first one has the following path: K:\gardencity\MSL_Mastcam_Sol_925_id_48420\OPC_000_000\patches\03-Patch-00001~0035\00-Patch-00016~0005-0.aakd
 9: in total there are 24/24 of Kd0Paths missing
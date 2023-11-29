# The new opc-tool

The purpose of this tool is to generate KdTrees for OPC hierarchies.
Either run the tool directly from source or use a prebuilt version which is hosted on nuget and available as a dotnet tool.

Like any other dotnet tool it can be installed:
```
> dotnet tool install opc-tool --global
Sie können das Tool über den folgenden Befehl aufrufen: opc-tool
Das Tool "opc-tool" (Version 4.20.0-prerelease1) wurde erfolgreich installiert.

>opc-tool --help

.--. .--.     .--. .--.
|   )|   )        )|   :
|--' |--' .-.  --: |   |
|    |  \(   )    )|   ;
'    '   ``-' `--' '--'   opc-tool by pro3d-space.

* validates OPC directories.
* generates KdTrees.

opc-tool 4.10.0.0
PRo3D contributors.

  --verbose               Prints all messages to standard output.

  --forcekdtreerebuild    Forces rebuild and overwrites existing KdTrees

  --generatedds           Generate DDS

  --overwritedds          Overwrite DDS

  --help                  Display this help screen.

  --version               Display version information.

  value pos. 0            Surface Directory
```

For example: 
`opc-tool --forcekdtreerebuild "F:\pro3d\data\dimorphos"`

The `--forcekdtreerebuild` option forces a rebuild of the KdTrees and overwrites existing files.

The content of the given surface folder could look like:
```
09.08.2023  08:45               312 Dimorphos.opcx
09.08.2023  08:45               397 Dimorphos.opcx.json
09.08.2023  10:13    <DIR>          Dimorphos_000_000
```

Where `Dimorphos_000_000` is an OPC hierarchy containing the Images and Patches subfolder.

## Caveats

Currently the tool cannot create uncompressed DDS files. The files are stored with DXT1 compression.
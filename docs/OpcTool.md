# The new opc-tool

The purpose of this tool is to generate KdTrees for OPC hierarchies.
The source code is found in ./src/opc-tool. For now it is a simple command-line tool.

```
```

For example: 
`dotnet .\bin\Debug\net6.0 --force "F:\pro3d\data\dimorphos"

The `--force` option forces a rebuild of the KdTrees and overwrites existing files.

The content of the given surface folder could look like:
```
09.08.2023  08:45               312 Dimorphos.opcx
09.08.2023  08:45               397 Dimorphos.opcx.json
09.08.2023  10:13    <DIR>          Dimorphos_000_000
```

Where `Dimorphos_000_000` is an OPC hierarchy containing the Images and Patches subfolder.
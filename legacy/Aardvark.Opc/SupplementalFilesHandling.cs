using Aardvark.Base;
using Aardvark.Opc.PatchHierarchy;
using Aardvark.Parser.Aara;
using Aardvark.Rendering;
using Aardvark.Rendering.SlimDx;
using Aardvark.Runtime;
using Aardvark.VRVis;
using SlimDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Aardvark.Opc;
using Aardvark.Algodat;
using System.Windows.Media.Imaging;


namespace Aardvark.Opc
{
    public static class SupplementalFilesHandling
    {
        private static SlimDX.Direct3D10.Device device = new SlimDX.Direct3D10.Device(SlimDX.Direct3D10.DeviceCreationFlags.None);
        //private static SlimDX.Direct3D11.Device d = new SlimDX.Direct3D11.Device(SlimDX.Direct3D11.DriverType.Reference, SlimDX.Direct3D11.DeviceCreationFlags.None, new SlimDX.Direct3D11.FeatureLevel[] { SlimDX.Direct3D11.FeatureLevel.Level_9_1 });

        /// <summary>
        /// Converts .tif to .dds files in an opc directory for straight to gpu uploads of textures.
        /// </summary>
        public static int ConvertTiffsToDDSs(OpcPaths opcPaths, bool overrideExisting = false, IObserver<float> progress = null, CancellationToken cancelToken = default(CancellationToken))
        {
            var tiffs = StorageConfig.GetDirectories(opcPaths.ImagesSubDir)
                .SelectMany(x => StorageConfig.GetFiles(x))
                .Where(x => Path.GetExtension(x) == ".tif");

            if (tiffs.IsEmptyOrNull())
            {
                if (progress != null)
                {
                    progress.OnNext(1f);
                    progress.OnCompleted();
                }
                return 0;
            }

            var inc = 1f / tiffs.Count();
            foreach (var f in tiffs)
            {
                cancelToken.ThrowIfCancellationRequested();

                //  ConvertTiffToDDS2(f, overrideExisting);
                //  ConvertTiffToDDS(f, overrideExisting);
                ConvertTiffToDDS3(f, overrideExisting);

                if (progress != null)
                    progress.OnNext(inc);
            }

            if (progress != null)
                progress.OnCompleted();
            return tiffs.Count();
        }
        
        /// <summary>
        /// Converts .tif to .dds files in an opc directory for straight to gpu uploads of textures.
        /// tiffs are convert asynchrounos in parallel
        /// </summary>
        public static Task<int> ConvertTiffsToDDSsAsync(OpcPaths opcPaths, bool overrideExisting = false, IProgress<float> progress = null, CancellationToken cancelToken = default(CancellationToken))
        {
            var taskSource = new TaskCompletionSource<int>();

            var tiffs = StorageConfig.GetDirectories(opcPaths.ImagesSubDir)
                .SelectMany(x => StorageConfig.GetFiles(x))
                .Where(x => Path.GetExtension(x) == ".tif");

            if (tiffs.IsEmptyOrNull())
            {
                if (progress != null)
                    progress.Report(1f);

                taskSource.SetResult(0);
                return taskSource.Task;
            }

            var inc = 1f / tiffs.Count();
            var tasks = new List<Task>();
            foreach (var f in tiffs)
            {
                cancelToken.ThrowIfCancellationRequested();

                var task = Task.Run(() =>
                {
                    ConvertTiffToDDS3(f, overrideExisting);
                    if (progress != null)
                        progress.Report(inc);
                });
                tasks.Add(task);
            }

            Task.WhenAll(tasks).ContinueWith(_ => taskSource.SetResult(tiffs.Count()));

            return taskSource.Task;
        }
        
        /// <summary>
        /// Deletes *.tif in an opc directory (if *.dds exist).
        /// </summary>
        public static int DeleteTiffs(OpcPaths opcPaths, IProgress<Tup<float, string>> progress = null)
        {
            int deleteCounter = 0;

            var files = StorageConfig.GetDirectories(opcPaths.ImagesSubDir)
                .SelectMany(x => StorageConfig.GetFiles(x))
                .Where(x => Path.GetExtension(x) == ".tif");

            if (files.IsEmptyOrNull())
            {
                if (progress != null)
                    progress.Report(Tup.Create(1f, "No tif images found."));
                return 0;
            }

            var inc = 1f / files.Count();
            foreach (var f in files)
            {
                string ddsPath = Path.ChangeExtension(f, ".dds");
                var msg = "";

                if (StorageConfig.FileExists(ddsPath))
                {
                    File.Delete(f);
                    deleteCounter++;
                }
                else msg = "dds file not deleted, because it doesn't have a coresponding tif file (" + f + ")";

                if (progress != null)
                    progress.Report(Tup.Create(inc, msg));
            }

            return deleteCounter;
        }

        /// <summary>
        /// Deletes *.dds in an opc directory (if *.tif exists).
        /// </summary>
        public static int DeleteDDSs(OpcPaths opcPaths, IProgress<Tup<float, string>> progress = null)
        {
            int deleteCounter = 0;

            var files = StorageConfig.GetDirectories(opcPaths.ImagesSubDir)
                .SelectMany(x => StorageConfig.GetFiles(x))
                .Where(x => Path.GetExtension(x) == ".dds");

            if (files.IsEmptyOrNull())
            {
                if (progress != null)
                    progress.Report(Tup.Create(1f, "No dds images found."));
                return 0;
            }

            var inc = 1f / files.Count();
            foreach (var f in files)
            {
                string tifPath = Path.ChangeExtension(f, ".tif");
                var msg = "";

                if (StorageConfig.FileExists(tifPath))
                {
                    File.Delete(f);
                    deleteCounter++;
                }
                else msg = "dds file not deleted, because it doesn't have a coresponding tif file (" + f + ")";

                if (progress != null)
                    progress.Report(Tup.Create(inc, msg));
            }

            return deleteCounter;
        }

        /// <summary>
        /// Builds look up table for profile "intersection" in 2d data.
        /// </summary>
        public static void BuildLookUpTable(OpcPaths opcPaths, bool overrideExisting, IProgress<Tup<float, string>> progress, CancellationToken cancelToken = default(CancellationToken))
        {
            var sub = new Subject<Tup<float, string>>();
            sub.Subscribe(tup => progress.Report(tup));
            BuildLookUpTable(opcPaths, overrideExisting, sub, cancelToken);
        }

        /// <summary>
        /// Builds look up table for profile "intersection" in 2d data.
        /// </summary>
        public static void BuildLookUpTable(OpcPaths opcPaths, bool overrideExisting = false, IObserver<Tup<float, string>> progress = null, CancellationToken cancelToken = default(CancellationToken))
        {
            if (!overrideExisting && StorageConfig.FileExists(opcPaths.ProfileLutPath))
            {
                if (progress != null)
                {
                    progress.OnNext(Tup.Create(1f, ""));
                    progress.OnCompleted();
                }
                Report.Line("LookUpTable already exists at {0}", opcPaths.ProfileLutPath);
            }

            // PatchHierarchyInfo
            var info = PatchHierarchyInfo.BuildOrLoadCache(opcPaths);
            info.PatchTree.CreatePatchPaths(opcPaths.PatchesSubDir);

            // Level 0 Patches
            var lvl0Patches =
                info.RetrievePatchTreesOfLevel(0);

            if (progress != null)
                progress.OnNext(Tup.Create(.1f, ""));

            // group by BB-2d: Min-X, Max-X
            var patchesGroupedByBB = lvl0Patches
                .GroupBy(patchTree => Tup.Create(patchTree.Info.GlobalBoundingBox2d.Min.X, patchTree.Info.GlobalBoundingBox2d.Max.X)).ToArray();

            var entries = new List<ProfileLookUpTableEntry>();
            #region Create ProfileLookUpTableEntries

            for (int index = 0; index < patchesGroupedByBB.Count(); index++)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    if (progress != null)
                        progress.OnNext(Tup.Create(0f, "Building LookUpTabele cancelled."));
                    cancelToken.ThrowIfCancellationRequested();
                }

                //sort patches according to their b (sv_b_r) value
                var patchesGroupedBySvBR = patchesGroupedByBB[index]
                    .OrderBy(k => k.Info.GlobalBoundingBox2d.Min.Y);

                var fileHandleList = new List<PatchFileHandle>();
                #region build PatchFileHandles

                foreach (var patchTree in patchesGroupedBySvBR)
                {
                    if (patchTree.Info.Positions2d.IsNullOrEmpty())
                    {
                        Report.Warn("ProfileLutCreation: Skipping Patchtree {0}, because of missing 2d positions.", patchTree.Id);
                        if (progress != null)
                            progress.OnNext(Tup.Create(0f, "ProfileLutCreation: Skipping Patchtree " + patchTree.Id + ", because of missing 2d positions."));
                        continue;
                    }
                    var pos2dPath = patchTree.GetPositionPath(PositionsType.V2dPositions);

                    //CAUTION absolute path needs to be repaired during loading
                    var file = AaraData.FromFile(pos2dPath);
                    file.SourceFileName = string.Empty;

                    fileHandleList.Add(new PatchFileHandle()
                    {
                        PatchTree = patchTree,
                        FileLoader = file,
                    });
                }

                #endregion

                // Create ProfileLookupTableEntries
                var firstPatchBB = patchesGroupedBySvBR.First().Info.GlobalBoundingBox2d;
                entries.Add(new ProfileLookUpTableEntry()
                {
                    Index = index,
                    SvRange = new Range1d(firstPatchBB.Min.X, firstPatchBB.Max.X),
                    FileHandles = fileHandleList,
                });

                var progressInc = 0.8f / patchesGroupedByBB.Count();
                if (progress != null)
                    progress.OnNext(Tup.Create(progressInc, ""));
            }
            entries.Reverse();

            #endregion

            #region Save LUT

            var lut = new ProfileLookUpTable()
            {
                SvRange = new Range1d(info.PatchTree.Info.GlobalBoundingBox2d.Min.X, info.PatchTree.Info.GlobalBoundingBox2d.Max.X),
                Entries = entries,
                AvgGeomtrySize = info.AvgGeometrySizes.First()
            };

            lut.Save(opcPaths.ProfileLutPath);

            #endregion

            if (progress != null)
            {
                progress.OnNext(Tup.Create(0.1f, ""));
                progress.OnCompleted();
            }
        }

        /// <summary>
        /// Creates normals for the OPC in opcBasePath and saves them as Normals.aara.
        /// </summary>
        public static void BuildNormals(OpcPaths opcPaths, bool overrideExisting, IProgress<Tup<float, string>> progress, CancellationToken cancelToken = default(CancellationToken))
        {
            var sub = new Subject<Tup<float, string>>();
            sub.Subscribe(tup => progress.Report(tup));
            BuildNormals(opcPaths, overrideExisting, sub, cancelToken);
        }

        /// <summary>
        /// Creates normals for the OPC in opcBasePath and saves them as Normals.aara.
        /// </summary>
        public static void BuildNormals(OpcPaths opcPaths, bool overrideExisting = false, IObserver<Tup<float, string>> progress = null, CancellationToken cancelToken = default(CancellationToken))
        {
            var normalFileName = "Normals.aara";

            var posFiles = StorageConfig.GetDirectories(opcPaths.PatchesSubDir)
                .SelectMany(x => StorageConfig.GetFiles(x))
                .Where(fileWpath => fileWpath.EndsWith("Positions.aara", StringComparison.OrdinalIgnoreCase));

            foreach (var file in posFiles)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    if (progress != null)
                    {
                        progress.OnNext(Tup.Create(0f, "Building normals cancelled."));
                        progress.OnCompleted();
                    }
                    cancelToken.ThrowIfCancellationRequested();
                }

                var normalsFilePath = Path.Combine(Path.GetDirectoryName(file), normalFileName);

                if (overrideExisting || !StorageConfig.FileExists(normalsFilePath))
                {
                    var posAara = AaraData.FromFile(file);
                    var tileSize = new V2i(posAara.Size[0], posAara.Size[1]);

                    var posArray = AaraData.ConvertArrayToV3ds[posAara.DataTypeAsSymbol](posAara.LoadElements());

                    var invalidPoints = OpcIndices.GetInvalidPositions(posArray);
                    var indices = OpcIndices.ComputeIndexArray(tileSize, invalidPoints);
                    var normals = OpcIndices.GenerateVertexNormals(indices, posArray)
                        .Select(p => p.ToV3f()).ToArray();

                    WriteV3fArrayAsAara(normalsFilePath, normals, tileSize);
                }

                if (progress != null)
                    progress.OnNext(Tup.Create(1f / posFiles.Count(), ""));
            }

            if (progress != null)
                progress.OnCompleted();
        }

        /// <summary>
        /// Deletes *.dds in an opc directory.
        /// </summary>
        public static int DeleteNormals(OpcPaths opcPaths, IProgress<float> progress = null)
        {
            var files = StorageConfig.GetDirectories(opcPaths.PatchesSubDir)
                .SelectMany(x => StorageConfig.GetFiles(x))
                .Where(fileWpath => fileWpath.EndsWith("Normals.aara", StringComparison.OrdinalIgnoreCase));

            if (files.IsEmptyOrNull())
            {
                if (progress != null)
                    progress.Report(1f);
                return 0;
            }

            var inc = 1f / files.Count();
            foreach (var f in files)
            {
                File.Delete(f);
                if (progress != null)
                    progress.Report(inc);
            }
            return files.Count();
        }

        public static bool HasNormals(OpcPaths opcPaths)
        {
            var normalFileName = "Normals.aara";

            return StorageConfig.GetDirectories(opcPaths.PatchesSubDir)
                .All(dir => StorageConfig.FileExists(dir + "\\" + normalFileName));
        }

        #region helper methods

        public static void ConvertTiffToDDS(string tiffPath, bool overrideExisting = false)
        {
            string ddsPath = Path.ChangeExtension(tiffPath, ".dds");

            if (overrideExisting || !StorageConfig.FileExists(ddsPath))
            {
                var bitmap = new Bitmap(tiffPath);

                // Konvertierung
                var target = SlimDx9TextureConvertible.CreateFile(
                    new SlimDx9TextureConvertible.SlimDx9TextureParameters()
                    {
                        AardvarkFormat = Patch.GetTextureFormatFromPixelFormat(bitmap.PixelFormat),
                        AardvarkUsage = AardvarkUsage.None,
                        FileName = ddsPath,
                        Pool = Pool.Scratch,
                        MipMapLevels = 0
                    });

                var bitmapConvertible = new Convertible("BitmapMemory", bitmap);
                bitmapConvertible.ConvertInto(target);
            }
        }
        public static void ConvertTiffToDDS3(string tiffPath, bool overrideExisting = false)
        {
            var ddsPath = Path.ChangeExtension(tiffPath, ".dds");
            if (StorageConfig.FileExists(ddsPath) && !overrideExisting)
                return;

            var info = SlimDX.Direct3D10.ImageLoadInformation.FromDefaults();

            //var image = PixImage.Create(tiffPath, PixLoadOptions.UseSystemImage);
            //if (image.PixFormat == PixFormat.ByteGray)
            //    info.Format = SlimDX.DXGI.Format.A8_UNorm;
            
            var tex = SlimDX.Direct3D10.Texture2D.FromFile(device, tiffPath, info);
            SlimDX.Direct3D10.Texture2D.SaveTextureToFile(tex, SlimDX.Direct3D10.ImageFileFormat.Dds, ddsPath);
            tex.Dispose();
        }
        public static void ConvertTiffToDDS2(string tiffPath, bool overrideExisting = false)
        {
            string ddsPath = Path.ChangeExtension(tiffPath, ".dds");

            if (overrideExisting || !StorageConfig.FileExists(ddsPath))
            {
                var image = PixImage.Create(tiffPath, PixLoadOptions.UseSystemImage);
                // image.SaveAsImage(pngPath);

                var outFormat = image.PixFormat == PixFormat.ByteGray ?
                    Rendering.AardvarkFormat.L8 : Rendering.AardvarkFormat.Dxt5;

                var target = SlimDx9TextureConvertible.CreateFile(
                       new SlimDx9TextureConvertible.SlimDx9TextureParameters()
                       {
                           AardvarkFormat = outFormat,
                           AardvarkUsage = AardvarkUsage.None,
                           FileName = ddsPath,
                           Pool = Pool.Scratch,
                           MipMapLevels = 0
                       });

                var con = image.Convertible();
                con.ConvertInto(target);
            }
        }

        public static void WriteV3dArrayAsAara(string filePath, V3d[] elements, V2i sizeV)
        {
            var file = File.Open(filePath, FileMode.Create);

            using (var writer = new BinaryWriter(file, Encoding.UTF8))
            {
                string typeName = "V3d";
                byte dimensions = (byte)2;
                int[] size = { sizeV.X, sizeV.Y };

                writer.Write(typeName);
                writer.Write(dimensions);

                writer.Write(size[0]);
                writer.Write(size[1]);

                for (int i = 0; i < elements.Length; i++)
                {
                    writer.Write(elements[i].X);
                    writer.Write(elements[i].Y);
                    writer.Write(elements[i].Z);
                }

                writer.Flush();
            }
            file.Dispose();
        }
        public static void WriteV3fArrayAsAara(string filePath, V3f[] elements, V2i sizeV)
        {
            var file = File.Open(filePath, FileMode.Create);

            using (var writer = new BinaryWriter(file, Encoding.UTF8))
            {
                string typeName = "V3f";
                byte dimensions = (byte)2;
                int[] size = { sizeV.X, sizeV.Y };

                writer.Write(typeName);
                writer.Write(dimensions);

                writer.Write(size[0]);
                writer.Write(size[1]);

                for (int i = 0; i < elements.Length; i++)
                {
                    writer.Write(elements[i].X);
                    writer.Write(elements[i].Y);
                    writer.Write(elements[i].Z);
                }

                writer.Flush();
            }
            file.Dispose();
        }

        #endregion
    }
}
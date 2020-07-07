using Aardvark.Algodat;
using Aardvark.Base;
using Aardvark.Parser.Aara;
using Aardvark.Runtime;
using Aardvark.VRVis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aardvark.Opc.PatchHierarchy
{
    [RegisterTypeInfo]
    public class PatchFileHandle : Map
    {
        public const string Identifier = "PatchFileHandle";

        public static class Property
        {
            public const string PatchTree = "PatchTree";
            public const string FileLoader = "FileLoader";
        }

        public PatchFileHandle()
            : base(Identifier)
        {
        }

        public PatchFileHandle(PatchFileHandle pfh)
            : this()
        {
            PatchTree = pfh.PatchTree;
            FileLoader = new AaraData(pfh.FileLoader);
        }

        public PatchTree PatchTree
        {
            get { return Get<PatchTree>(Property.PatchTree); }
            set { this[Property.PatchTree] = value; }
        }

        public AaraData FileLoader
        {
            get { return Get<AaraData>(Property.FileLoader); }
            set { this[Property.FileLoader] = value; }
        }
    }

    [RegisterTypeInfo]
    public class ProfileLookUpTableEntry : Map, IAwakeable
    {
        public const string Identifier = "ProfileLookUpTableEntry";

        public static class Property
        {
            public const string Index = "Index";
            public const string SvRange = "SvRange";
            public const string FileHandles = "FileHandles";
        }

        public ProfileLookUpTableEntry()
            : base(Identifier)
        {
            FileHandles2d = new List<PatchFileHandle>();
        }

        public int Index
        {
            get { return Get<int>(Property.Index); }
            set { this[Property.Index] = value; }
        }

        public Range1d SvRange
        {
            get { return Get<Range1d>(Property.SvRange); }
            set { this[Property.SvRange] = value; }
        }

        public List<PatchFileHandle> FileHandles
        {
            get { return Get<List<PatchFileHandle>>(Property.FileHandles); }
            set { this[Property.FileHandles] = value; }
        }

        public List<PatchFileHandle> FileHandles2d { get; private set; }

        public void Awake(int codedVersion)
        {
            FileHandles2d = FileHandles.Select(x => new PatchFileHandle(x)).ToList();
        }
    }

    [RegisterTypeInfo]
    public class ProfileLookUpTable : Map
    {
        #region public members

        public const string Identifier = "ProfileLookUpTable";

        public static class Property
        {
            public const string SvRange = "SvRange";
            public const string Entries = "Entries";
            public const string AvgGeometrySize = "AvgGeometrySize";
        }

        public Range1d SvRange
        {
            get { return Get<Range1d>(Property.SvRange); }
            set { this[Property.SvRange] = value; }
        }

        public List<ProfileLookUpTableEntry> Entries
        {
            get { return Get<List<ProfileLookUpTableEntry>>(Property.Entries); }
            set { this[Property.Entries] = value; }
        }

        public double AvgGeomtrySize
        {
            get { return Get<double>(Property.AvgGeometrySize); }
            set { this[Property.AvgGeometrySize] = value; }
        }

        #endregion

        #region private members

        private double m_last_sv = double.NaN;
        private Pair<V3d[]> m_lastProfile;

        #endregion

        public ProfileLookUpTable()
            : base(Identifier)
        {
        }

        /// <summary>
        /// Construct absolute paths for FileHandle (PatchTree.Path, AaraData.SourceFileName) at runtime. 
        /// </summary>
        public void UpdateFilePaths(OpcPaths paths)
        {
            var basePatchesPath = paths.PatchesSubDir;

            foreach (var e in Entries)
            {
                foreach (var fileHandle in e.FileHandles)
                {
                    var patchTree = fileHandle.PatchTree;
                    patchTree.PatchPath = Path.Combine(basePatchesPath, fileHandle.PatchTree.Id);
                    fileHandle.FileLoader.SourceFileName = patchTree.GetPositionPath(PositionsType.V3dPositions);
                }

                foreach (var fileHandle in e.FileHandles2d)
                {
                    var patchTree = fileHandle.PatchTree;
                    patchTree.PatchPath = Path.Combine(basePatchesPath, fileHandle.PatchTree.Id);
                    fileHandle.FileLoader.SourceFileName = patchTree.GetPositionPath(PositionsType.V2dPositions);
                }
            }
        }

        static Telemetry.CpuTime s_cpu_LookUpProfile = new Telemetry.CpuTime().Register("Dibit: LookUpProfile", true, true);
        
        /// <summary>
        /// Loads tunnel profile from file.
        /// </summary>
        /// <param name="sv">Sv position to load the profile at.</param>
        /// <returns>XYZ and SvBR profile.</returns>
        public Pair<V3d[]> LookUpProfile(double sv)
        {
            //Requires.That(!double.IsNaN(sv));
            if (double.IsNaN(sv))
                return Pair.Create(new V3d[0], new V3d[0]);

            // sv didn't change that much, use last profile
            if (m_last_sv.ApproximateEquals(sv, AvgGeomtrySize))
                return m_lastProfile;

            //find patch row
            var entries = Entries
                .Where(x => x.SvRange.Contains(sv));

            // open files and read data
            var returnList = new List<V3d>();
            var returnList2d = new List<V3d>();

            using (s_cpu_LookUpProfile.Timer)
            {
                foreach (var entry in entries)
                {
                    // pair together XZY and SvBR handles
                    var handles = entry.FileHandles.Zip(entry.FileHandles2d, (xyz, svbr) => Pair.Create(xyz, svbr));

                    var rowMajorCount = entry.FileHandles.Count(h => h.PatchTree.Info.QuadVertexSortOrder == PatchFileInfo.QuadVertexSortOrderType.RowMajor);
                    // column major is default, so don't have to check for PatchFileInfo.Tag.ColumnMajor

                    if (rowMajorCount > 0 && rowMajorCount < entry.FileHandles.Count())
                    {
                        Report.Warn("ProfileLookUpTable: Can't handle mixed row/column-major patches.");
                        continue;
                    }
                    // resort patches to get a continuous line (only a problem in RowMajor data)
                    else if (rowMajorCount == entry.FileHandles.Count())
                    {
                        handles = handles.Reverse();
                    }

                    foreach (var handle in handles)
                    {
                        var pnts = LookUpProfile(handle, sv, entry.SvRange);
                        returnList.AddRange(pnts.E0);
                        returnList2d.AddRange(pnts.E1);
                    }
                }
            }
            m_last_sv = sv;

            return m_lastProfile = new Pair<V3d[]>() { E0 = returnList.ToArray(), E1 = returnList2d.ToArray() };
        }

        /// <summary>
        /// Loads XYZ and SvBR profile from file at SV position.
        /// Multiple reloads of SvBR data might be possible to adjust off indexing based on malformed data.
        /// </summary>
        /// <param name="handle">File handles for XYZ and SvBR data.</param>
        /// <param name="sv">Sv position to load data at.</param>
        /// <param name="svRange">SvRange of patch (needed to calculate index).</param>
        /// <returns>XYZ/SvBR profile</returns>
        private Pair<IEnumerable<V3d>> LookUpProfile(Pair<PatchFileHandle> handle, double sv, Range1d svRange)
        {
            // sv to array index
            var index =
                    handle.E0.PatchTree.Info.QuadVertexSortOrder == PatchFileInfo.QuadVertexSortOrderType.RowMajor ?
                    SvToArrayIndex(sv, svRange, handle.E0.FileLoader.NumOfRows) :
                    SvToArrayIndex(sv, svRange, handle.E0.FileLoader.NumOfColumns);

            // Load SvBR data. If indexing was not exact keep reloading neighboring profiles
            var pntsSvbr = new V3d[0];
            var localSv = sv - handle.E1.PatchTree.Info.Local2Global2d.M03;
            int increment = 0;
            do
            {
                // Load data
                pntsSvbr = LookUpProfile(handle.E1, index);

                // For some reason we couldn't load any more data.....
                if (pntsSvbr == null || pntsSvbr.IsEmpty())
                    break;

                // NaN line, try next one
                if (pntsSvbr.Where(p => !p.IsNaN).IsEmptyOrNull())
                {
                    if (increment == 0)
                        break;
                    else
                    {
                        index += increment;
                        continue;
                    }
                }

                var avgSv = pntsSvbr.Where(p => !p.IsNaN).Average(p => p.X);
                // Profile close enough?
                if (avgSv.ApproximateEquals(localSv, AvgGeomtrySize / 2))
                    break;
                else
                {   // nope, indexing must be off, try next one
                    if (increment == 0)
                        increment = (avgSv < localSv) ? 1 : -1;
                    // if increment flips, than me might have a bigger hole between lines than AvgGeoSize
                    else if (increment != ((avgSv < localSv) ? 1 : -1))
                        break;
                    index += increment;
                }
            } while (!pntsSvbr.IsEmptyOrNull());

            // Load XYZ data
            var pntsXyz = LookUpProfile(handle.E0, index);

            return Pair.Create(
                pntsXyz.Select(pt => handle.E0.PatchTree.Info.Local2Global.TransformPos(pt)),
                pntsSvbr.Select(pt => handle.E0.PatchTree.Info.Local2Global2d.TransformPos(pt))
                );
        }

        private V3d[] LookUpProfile(PatchFileHandle handle, int index)
        {
            var pntArray =
                handle.PatchTree.Info.QuadVertexSortOrder == PatchFileInfo.QuadVertexSortOrderType.RowMajor ?
                handle.FileLoader.LoadRow(index) :
                handle.FileLoader.LoadColumn(index);

            return AaraData.ConvertArrayToV3ds[handle.FileLoader.DataTypeAsSymbol](pntArray);
        }

        private int SvToArrayIndex(double sv, Range1d svRange, int numOfSvLinesInArray)
        {
            var idNorm = ((sv - svRange.Min) / svRange.Size);
            if (idNorm > 1)
            {
                Report.Warn("ProfileLookUpTable: Lookup intended to read out of patch size.");
                return -1;
            }

            return (int)System.Math.Round(idNorm * (numOfSvLinesInArray - 1));
        }
    }
}

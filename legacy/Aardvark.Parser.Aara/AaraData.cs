using Aardvark.Base;
using Aardvark.Runtime;
using Aardvark.SceneGraph;
using Aardvark.VRVis;
using System;
using System.Linq;

namespace Aardvark.Parser.Aara
{
    [RegisterTypeInfo(Version = 1)]
    public class AaraData : Instance
    {
        #region public properties and fields

        public const string Identifier = "AaraData";
        public static class Property
        {
            public const string DataType = "DataType";
            public const string DataTypeAsSymbol = "DataTypeAsSymbol";
            public const string Size = "Size";
            public const string ElementCount = "ElementCount";
            public const string SourceFileName = "SourceFileName";
            public const string HeaderSize = "HeaderSize";
        }

        public string SourceFileName
        {
            get { return this.Get<string>(Property.SourceFileName); }
            set { this[Property.SourceFileName] = value; }
        }

        public int ElementCount
        {
            get { return this.Get<int>(Property.ElementCount); }
            private set { this[Property.ElementCount] = value; }
        }

        public Symbol DataTypeAsSymbol
        {
            get { return this.Get<Symbol>(Property.DataTypeAsSymbol); }
            private set { this[Property.DataTypeAsSymbol] = value; }
        }
        public string DataType
        {
            get { return DataTypeAsSymbol.ToString(); }
            private set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// 1 to 3 dimensional. Number of columns / rows / pages.
        /// </summary>
        public int[] Size
        {
            get { return this.Get<int[]>(Property.Size); }
            private set { this[Property.Size] = value; }
        }
        public byte Dimensions
        {
            get { return (byte)Size.Count(); }
        }
        public int NumOfColumns
        {
            get { return Size[0]; }
        }
        public int NumOfRows
        {
            get { return Size[1]; }
        }

        /// <summary>
        /// Size of the header in bytes
        /// </summary>
        public int HeaderSize
        {
            get { return this.Get<int>(Property.HeaderSize); }
            private set { this[Property.HeaderSize] = value; }
        }

        public static SymbolDict<Func<Array, V3f[]>> ConvertArrayToV3fs = new SymbolDict<Func<Array, V3f[]>>()
        {
            {"V3d", (Array a) => a.CopyAndConvert(V3f.FromV3d)},
            {"V3f", (Array a) => a.To<V3f[]>()}
        };

        public static SymbolDict<Func<Array, V3d[]>> ConvertArrayToV3ds = new SymbolDict<Func<Array, V3d[]>>()
        {
            {"V3d", (Array a) => a.To<V3d[]>()},
            {"V3f", (Array a) => a.CopyAndConvert(V3d.FromV3f)},
        };

        public static SymbolDict<Func<Array>> CreateEmptyArray = new SymbolDict<Func<Array>>()
        {
            {"V3d", () => new V3d[0]},
            {"V3f", () => new V3f[0]},
        };

        #endregion

        #region private properties and fields

        private static SymbolDict<int> s_sizeTable =
        #region Definition
 new SymbolDict<int>
            {
                { "byte", sizeof(byte)},
                { "int", sizeof(int)},
                { "float", sizeof(float)},
                { "double", sizeof(double)},
                { "V2f", sizeof(float) * 2},
                { "V3f", sizeof(float) * 3},
                { "V2d", sizeof(double) * 2},
                { "V3d", sizeof(double) * 3},
                { "C3b", sizeof(byte) * 3},
                { "C3f", sizeof(float) * 3},
                { "C4b", sizeof(byte) * 4},
                { "C4f", sizeof(float) * 4}
            };
        #endregion

        private static SymbolDict<Func<StreamCodeReader, int, Array>> s_readBigFuncs =
        #region Definition
            new SymbolDict<Func<StreamCodeReader, int, Array>>
            {
                { "byte", (r,c) => { var a = new byte[c]; r.ReadArray(a,0,c); return a; } },
                { "int", (r,c) => { var a = new int[c]; r.ReadArray(a,0,c); return a; } },
                { "float", (r,c) => { var a = new float[c]; r.ReadArray(a,0,c); return a; } },
                { "double", (r,c) => { var a = new double[c]; r.ReadArray(a,0,c); return a; } },
                { "V2f", (r,c) => { var a = new V2f[c]; r.ReadArray(a,0,c); return a; } },
                { "V3f", (r,c) => { var a = new V3f[c]; r.ReadArray(a,0,c); return a; } },
                { "V2d", (r,c) => { var a = new V2d[c]; r.ReadArray(a,0,c); return a; } },
                { "V3d", (r,c) => { var a = new V3d[c]; r.ReadArray(a,0,c); return a; } },
                //{ "V3d", (r,c) => { var a = new V3d[c]; r.ReadBig(a,0,c);
                //    var b = a.CopyAndConvert(V3f.FromV3d);
                //    return b; } },
                { "C3b", (r,c) => { var a = new C3b[c]; r.ReadArray(a,0,c); return a; } },
                { "C3f", (r,c) => { var a = new C3f[c]; r.ReadArray(a,0,c); return a; } },
                { "C4b", (r,c) => { var a = new C4b[c]; r.ReadArray(a,0,c); return a; } },
                { "C4f", (r,c) => { var a = new C4f[c]; r.ReadArray(a,0,c); return a; } },
            };
        #endregion

        private static Telemetry.CpuTime s_cpu_AaraDataReadMemoryStream = new Telemetry.CpuTime().Register("AaraData: Loading cpu-time", true, true);

        #endregion

        #region constructors

        public AaraData()
            : base(Identifier) { }

        public AaraData(AaraData ad)
            : this()
        {
            DataTypeAsSymbol = ad.DataTypeAsSymbol;
            Size = ad.Size;
            ElementCount = ad.ElementCount;
            SourceFileName = ad.SourceFileName;
            HeaderSize = ad.HeaderSize;
            DataTypeAsSymbol = ad.DataTypeAsSymbol;
        }

        public AaraData(string fileName)
            : this()
        {
            InitializeFromFileHeader(fileName);
        }

        public static AaraData FromFile(string fileName)
        { return new AaraData(fileName); }

        #endregion

        #region Public Methods

        /// <summary>
        /// Reads all data from the aara file.
        /// </summary>
        /// <returns></returns>
        public Array LoadElements()
        {
            return LoadElements(ElementCount);
        }

        /// <summary>
        /// Reads data from the aara file.
        /// </summary>
        /// <param name="count">Number of elements to read.</param>
        /// <param name="startPos">Position of first element to read in the data block (header will be skipped).</param>
        /// <returns></returns>
        public Array LoadElements(int count, int startPos = 0)
        {
            using (s_cpu_AaraDataReadMemoryStream.Timer)
            {
                if ((count + startPos) > ElementCount)
                {
                    Report.Warn("AaraData tried reading over end of file '" + SourceFileName + "'");
                    return CreateEmptyArray[DataTypeAsSymbol]();
                } else if (startPos < 0)
                {
                    Report.Warn("AaraData tried indexing < 0 in file '" + SourceFileName + "'");
                    return CreateEmptyArray[DataTypeAsSymbol]();
                }

                var countInByte = count * s_sizeTable[DataTypeAsSymbol];
                var startPosInByte = startPos * s_sizeTable[DataTypeAsSymbol];
                var readFun = s_readBigFuncs[DataTypeAsSymbol];

                // load from file to memory
                var memStream = Load.AsMemoryStream(SourceFileName, HeaderSize + startPosInByte, countInByte);

                // convert from memory stream to actual data
                return readFun(new StreamCodeReader(memStream), count);
            }
        }

        /// <summary>
        /// Reads data from the aara file.
        /// </summary>
        /// <param name="count">Number of elements to read.</param>
        /// <param name="columnPos">Column to start reading.</param>
        /// <param name="rowPos">Row to start reading.</param>
        /// <returns></returns>
        public Array LoadElements(int count, int columnPos, int rowPos)
        {
            return LoadElements(count, columnPos + NumOfColumns * rowPos);
        }

        /// <summary>
        /// Reads data from the aara file.
        /// </summary>
        /// <param name="count">Number of elements to read.</param>
        /// <param name="columnPos">Column to start reading.</param>
        /// <param name="rowPos">Row to start reading.</param>
        /// <param name="pagePos">Page to start reading</param>
        /// <returns></returns>
        public Array LoadElements(int count, int columnPos, int rowPos, int pagePos)
        {
            return LoadElements(count, columnPos + NumOfColumns * (rowPos + NumOfRows * pagePos));
        }

        /// <summary>
        /// Returns entire row.
        /// </summary>
        /// <param name="rowPos">Row to read.</param>
        /// <returns></returns>
        public Array LoadRow(int rowPos)
        {
            return LoadElements(NumOfColumns, 0, rowPos);
        }

        /// <summary>
        /// Returns entire column. Warning: This is slow, because elemnts have to been seeked out.
        /// </summary>
        /// <param name="columnPos">Column to read.</param>
        /// <returns></returns>
        public Array LoadColumn(int columnPos)
        {
            return LoadColumn(columnPos, 0, NumOfRows);
        }

        /// <summary>
        /// Returns a column in the range of rowStart to rowEnd. Warning: This is slow, because elemnts have to been seeked out.
        /// </summary>
        /// <param name="columnPos">Column to read.</param>
        /// <param name="rowStart"></param>
        /// <param name="rowEnd"></param>
        /// <returns></returns>
        public Array LoadColumn(int columnPos, int rowStart, int rowEnd)
        {
            using (s_cpu_AaraDataReadMemoryStream.Timer)
            {
                var readFun = s_readBigFuncs[DataTypeAsSymbol];
                var byteSize = s_sizeTable[DataTypeAsSymbol];

                var rowCount = rowEnd - rowStart;
                var byteOffset = HeaderSize + byteSize * columnPos;
                var byteStride = byteSize * NumOfColumns;

                // load from file to memory
                var chunks = Enumerable.Range(0, rowCount).Select(i => Tup.Create((long)(byteOffset + i * byteStride), (long)byteSize));
                var memStream = Load.AsMemoryStream(SourceFileName, chunks);

                // convert from memory stream to actual data
                var r = readFun(new StreamCodeReader(memStream), rowCount);
                return r;
            }
        }

        

        #endregion

        #region implement IAwakeable

        public override void Awake(int codedVersion)
        {
            base.Awake(codedVersion);

            if (codedVersion == 0)
            {
                DataTypeAsSymbol = (string)this[Property.DataType];
                this.Remove(Property.DataType);
            }
        }

        #endregion

        #region private methods

        private void InitializeFromFileHeader(string fileName)
        {
            using (s_cpu_AaraDataReadMemoryStream.Timer)
            {
                var typeNameLength = Load.AsByteArray(fileName, 0, 1)[0];

                var dataType = new string(Load.AsByteArray(fileName, 1, typeNameLength)
                    .Select(b => (char)b).ToArray());

                var dimensions = Load.AsByteArray(fileName, 1 + typeNameLength, 1)[0];

                // Size
                var elementCount = 1;
                var size = new int[dimensions];
                for (var i = 0; i < dimensions; i++)
                    elementCount *= size[i] = BitConverter.ToInt32(
                        Load.AsByteArray(fileName, 2 + typeNameLength + i * sizeof(int), sizeof(int)),
                        0);

                // -- set AaraData --
                SourceFileName = fileName;
                DataTypeAsSymbol = dataType;
                ElementCount = elementCount;
                Size = size;
                HeaderSize = 1 + typeNameLength + 1 + dimensions * sizeof(int);
            }
        }

        #endregion
    }
}

using Aardvark.Base;
using Aardvark.Runtime;
using Aardvark.VRVis;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Aardvark.Opc.PatchHierarchy
{
    /// <summary>
    /// Aggregates Data from patch.xml in a class
    /// </summary>
    [RegisterTypeInfo(Version = 1)]
    public class PatchFileInfo : IFieldCodeable
    {
        public List<string> TagList;
        public string GeometryType;
        public string QuadVertexSortOrder = QuadVertexSortOrderType.ColumnMajor;

        public M44d Local2Global;
        public Box3d GlobalBoundingBox;
        public Box3d LocalBoundingBox;

        public M44d Local2Global2d;
        public Box3d GlobalBoundingBox2d;
        public Box3d LocalBoundingBox2d;

        public string Positions;
        public string Positions2d;
        public string Normals;
        public string Offsets;

        public bool IsValid;

        public List<string> Coordinates;
        public List<string> Textures;

        public static class QuadVertexSortOrderType
        {
            public static readonly string ColumnMajor = "ColumnMajor";
            public static readonly string RowMajor = "RowMajor";
        }

        public PatchFileInfo() { }

        /// <summary>
        /// Reads full patch.xml into and returns a PatchFileInfo
        /// </summary>
        public static PatchFileInfo FromFile(string patchFilePath)
        {
            if (Path.GetFileName(patchFilePath) != "patch.xml")
                patchFilePath = Path.Combine(patchFilePath, "patch.xml");

            //var patch = XElement.Load(patchFilePath, LoadOptions.None).Element("Patch");
            var patch = XElement.Load(Load.AsMemoryStream(patchFilePath)).Element("Patch");

            var info = new PatchFileInfo()
            {
                TagList = ReadStringList(patch, "TagList"),
                GeometryType = ReadString(patch, "GeometryType"),
                QuadVertexSortOrder = ReadString(patch, "QuadVertexSortOrder", PatchFileInfo.QuadVertexSortOrderType.ColumnMajor),

                Local2Global = ReadM44d(patch, "Local2Global"),
                GlobalBoundingBox = ReadBox3d(patch, "GlobalBoundingBox"),
                LocalBoundingBox = ReadBox3d(patch, "LocalBoundingBox"),

                Positions = ReadString(patch, "Positions"),  

                Coordinates = ReadStringList(patch, "Coordinates"),
                Textures = ReadStringList(patch, "Textures"),
            };

            if(HasNormalsAndOffsets(patch))
            {
               info.Normals = ReadString(patch, "Normals");
               info.Offsets = ReadString(patch, "Offsets");
            }

            //var x = patch.Element("Local2Global2D");
            //Report.Line(x.ToString());
            //   //e.Element("GlobalBoundingBox2d") != null &&
            //   //e.Element("LocalBoundingBox2d") != null &&
            //   //e.Element("Positions2d") != null;

            if (XmlHas2dData(patch))
            {
                info.Local2Global2d = ReadM44d(patch, "Local2Global2D");
                info.GlobalBoundingBox2d = ReadBox3d(patch, "GlobalBoundingBox2D");
                info.LocalBoundingBox2d = ReadBox3d(patch, "LocalBoundingBox2D");
                info.Positions2d = ReadString(patch, "Positions2D");
            }

            if (!info.LocalBoundingBox2d.IsValid)
                Report.Warn("LocalBoundingBox2d is not valid");

            return info;
        }

        public bool Has2dData
        {
            get
            {
                return this.Positions2d != null &&
                    LocalBoundingBox2d.IsValid &&
                    GlobalBoundingBox2d.IsValid;
            }
        }

        public Box3d GetGlobalBoundingBox(PositionsType posType)
        {
            if (posType == PositionsType.V3dPositions)
                return GlobalBoundingBox;
            else
                return GlobalBoundingBox2d;
        }

        public Box3d GetLocalBoundingBox(PositionsType posType)
        {
            if (posType == PositionsType.V3dPositions)
                return LocalBoundingBox;
            else
                return LocalBoundingBox2d;
        }

        public M44d GetLocal2Global(PositionsType posType)
        {
            if (posType == PositionsType.V3dPositions)
                return Local2Global;
            else
                return Local2Global2d;
        }

        #region Parse Helpers
        private static M44d ReadM44d(XElement e, string tagName)
        {
            return M44d.Parse(e.Element(tagName).Value.Trim());
        }

        private static Box3d ReadBox3d(XElement e, string tagName)
        {
            return Box3d.Parse(e.Element(tagName).Value.Trim());
        }

        private static List<string> ReadStringList(XElement e, string tagName)
        {
            return e.Element(tagName).Elements().Select(x => x.Value.Trim()).ToList();
        }

        private static string ReadString(XElement e, string tagName, string defaultValue = "")
        {
            return
                e.Element(tagName) == null
                ? defaultValue
                : e.Element(tagName).Value.Trim();
        }

        private static bool XmlHas2dData(XElement e)
        {
            return e.Element("Local2Global2D") != null &&
                e.Element("GlobalBoundingBox2D") != null &&
                e.Element("LocalBoundingBox2D") != null &&
                e.Element("Positions2D") != null;
        }

        private static bool HasNormalsAndOffsets(XElement e)
        {
            return e.Element("Normals") != null &&
                e.Element("Offsets") != null;
        }
        #endregion

        #region IFieldCodeable Members

        public IEnumerable<FieldCoder> GetFieldCoders(int coderVersion)
        {
            yield return new FieldCoder(0, "TagList", (c, o) => c.CodeList_of_String_(ref ((PatchFileInfo)o).TagList));
            yield return new FieldCoder(1, "GeometryType", (c, o) => c.CodeString(ref ((PatchFileInfo)o).GeometryType));
            yield return new FieldCoder(2, "QuadVertexSortOrder", 1, int.MaxValue, (c, o) => c.CodeString(ref ((PatchFileInfo)o).QuadVertexSortOrder));

            yield return new FieldCoder(3, "Local2Global", (c, o) => c.CodeM44d(ref ((PatchFileInfo)o).Local2Global));
            yield return new FieldCoder(4, "GlobalBoundingBox", (c, o) => c.CodeBox3d(ref ((PatchFileInfo)o).GlobalBoundingBox));
            yield return new FieldCoder(5, "LocalBoundingBox", (c, o) => c.CodeBox3d(ref ((PatchFileInfo)o).LocalBoundingBox));

            yield return new FieldCoder(6, "Local2Global2d", (c, o) => c.CodeM44d(ref ((PatchFileInfo)o).Local2Global2d));
            yield return new FieldCoder(7, "GlobalBoundingBox2d", (c, o) => c.CodeBox3d(ref ((PatchFileInfo)o).GlobalBoundingBox2d));
            yield return new FieldCoder(8, "LocalBoundingBox2d", (c, o) => c.CodeBox3d(ref ((PatchFileInfo)o).LocalBoundingBox2d));

            yield return new FieldCoder(9, "Positions", (c, o) => c.CodeString(ref ((PatchFileInfo)o).Positions));
            yield return new FieldCoder(10, "Positions2d", (c, o) => c.CodeString(ref ((PatchFileInfo)o).Positions2d));
            yield return new FieldCoder(11, "Normals", (c, o) => c.CodeString(ref ((PatchFileInfo)o).Normals));
            yield return new FieldCoder(12, "Offsets", (c, o) => c.CodeString(ref ((PatchFileInfo)o).Offsets));

            yield return new FieldCoder(13, "Coordinates", (c, o) => c.CodeList_of_String_(ref ((PatchFileInfo)o).Coordinates));
            yield return new FieldCoder(14, "Textures", (c, o) => c.CodeList_of_String_(ref ((PatchFileInfo)o).Textures));
        }
        #endregion
    }
}


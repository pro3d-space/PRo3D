using Aardvark.Base;
using Aardvark.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Aardvark.Opc.PatchHierarchy
{
    internal static class XmlTagsGlobal
    {
        public static string TagList = "TagList";
        public static string Item = "item";
        public static string Key = "key";
        public static string Val = "val";
    }

    public class PatchHierarchyXML
    {
        #region private properties and fields

        private static class XmlTags
        {
            public static string PatchHierarchy = "PatchHierarchy";
            public static string AcquisitionDate = "AcquisitionDate";
            public static string AvgGeometrySizes = "AvgGeometrySizes";
            public static string RootPatch = "RootPatch";
            public static string SubPatchMap = "SubPatchMap";
        }

        private XElement m_xmlPatchHierarchy = null;
        private OpcPaths m_paths;

        private XElement XmlPatchHierarchy
        {
            get
            {
                if (m_xmlPatchHierarchy == null)
                {
                    var xmlHierarchy = XElement.Load(Load.AsMemoryStream(m_paths.PatchHierarchyPath));
                    m_xmlPatchHierarchy = xmlHierarchy.Element(XmlTags.PatchHierarchy);
                }

                return m_xmlPatchHierarchy;
            }
        }

        #endregion

        #region public properties

        public OpcPaths OpcPaths
        {
            get
            {
                return m_paths;
            }
        }

        public List<string> TagList
        {
            get
            {
                var returnList = new List<string>();
                var tags = XmlPatchHierarchy.Elements(XmlTagsGlobal.TagList);
                if (tags.Count() > 0)
                {
                    string tagString = tags.First().Value.Trim();
                    if (!tagString.IsNullOrEmpty())
                    {
                        string subString = tagString.Substring(1, tagString.Length - 2);
                        var sep = new char[] { ',' };
                        string[] size = subString.Split(sep);
                        returnList = size.ToList();
                    }
                }
                return returnList;
            }
        }

        public string AcquisitionDate
        {
            get
            {
                var aquElements = XmlPatchHierarchy.Elements(XmlTags.AcquisitionDate);

                if (aquElements.Count() > 0)
                    return aquElements.First().Value.Trim();
                else
                    return String.Empty;
            }
        }

        public List<double> AverageGeometrySizes
        {
            get
            {
                var returnList = new List<double>();
                var avgGeometrySizes = XmlPatchHierarchy.Elements(XmlTags.AvgGeometrySizes);
                if (avgGeometrySizes.Count() > 0)
                {
                    string avgString = avgGeometrySizes.First().Value.Trim();
                    if (!avgString.IsNullOrEmpty())
                    {
                        string subString = avgString.Substring(1, avgString.Length - 2);
                        var sep = new char[] { ',' };
                        string[] size = subString.Split(sep);
                        returnList = size.Select(x => Double.Parse(x, Localization.FormatEnUS)).ToList();
                    }
                }
                return returnList;
            }
        }

        public string RootPatchName
        {
            get
            {
                return XmlPatchHierarchy.Element(XmlTags.RootPatch).Value.Trim();
            }
        }

        public string RootPatchPath
        {
            get
            {
                return Path.Combine(m_paths.PatchesSubDir, RootPatchName);
            }
        }

        public PatchTree PatchTree
        {
            get
            {
                var subPatchMapTag = XmlPatchHierarchy.Element(XmlTags.SubPatchMap);

                var subPatchMap = subPatchMapTag == null ? 
                    new Dictionary<string, IEnumerable<string>>() : XmlHelper.SubMap(subPatchMapTag);

                return PatchTreeFromPatchName(RootPatchName, m_paths.PatchesSubDir, subPatchMap);
            }
        }

        #endregion

        public PatchHierarchyXML(OpcPaths opcPaths)
        {
            m_paths = opcPaths;
        }

        public PatchHierarchyXML(string opcPath)
            : this(new OpcPaths(opcPath)) { }

        public static PatchHierarchyXML From(string opcPath)
        {
            return new PatchHierarchyXML(opcPath);
        }

        #region private methods

        /// <summary>
        /// Recursively traverses supplied subPatchMap dictionary and build up a PatchTree.
        /// </summary>
        /// <param name="subPatchMap">form of [patchName, List[subPatchName]]</param>
        private static PatchTree PatchTreeFromPatchName(string patchName, string patchesSubdir, Dictionary<string, IEnumerable<string>> subPatchMap)
        {
            IEnumerable<string> subPatches = null;
            var path = Path.Combine(patchesSubdir, patchName);

            //read patchfileinfo
            var info = new PatchFileInfo();

            try
            {
                info = PatchFileInfo.FromFile(path);
                info.IsValid = true;
            }
            catch (Exception e)
            {
                Report.Warn("PatchHierarchyXML: Parsing of patch.xml of path {0} threw an exception {1}", path, e.Message.ToString());
                info.IsValid = false;
            }

            //if name does not exist in subPatchMap a leaf is reached
            if (!subPatchMap.TryGetValue(patchName, out subPatches))
                return new PatchTree(patchName, info);

            //if subnodes exist recurse
            return new PatchTree(patchName, info,
                subPatches.Select(x => PatchTreeFromPatchName(x, patchesSubdir, subPatchMap))
                .Where(x => x.Info.IsValid)
                .ToList());
        }

        #endregion
    }

    public class ImagePyramidXML
    {
        #region private properties and fields

        private XElement m_xmlImagePyramid = null;
        private OpcPaths m_paths;

        private static class XmlTags
        {
            public static string ImagePyramid = "ImagePyramid";
            public static string AvgPixelSizes = "AvgPixelSizes";
            public static string RootTile = "RootTile";
            public static string SubTileMap = "SubTileMap";
        }

        private XElement XmlImagePyramid
        {
            get
            {
                if (m_xmlImagePyramid == null)
                    m_xmlImagePyramid = XElement.Load(
                        Load.AsMemoryStream(m_paths.ImagePyramidPaths.FirstOrDefault()))
                        .Element(XmlTags.ImagePyramid);

                return m_xmlImagePyramid;
            }
        }

        #endregion

        public ImagePyramidXML(OpcPaths opcPaths)
        {
            m_paths = opcPaths;
        }

        public ImagePyramidXML(string opcPath)
            : this (new OpcPaths(opcPath)) {}

        #region public properties

        public List<string> TagList
        {
            get
            {
                var returnList = new List<string>();
                var tags = XmlImagePyramid.Elements(XmlTagsGlobal.TagList);
                if (tags.Count() > 0)
                {
                    string tagString = tags.First().Value.Trim();
                    if (!tagString.IsNullOrEmpty())
                    {
                        string subString = tagString.Substring(1, tagString.Length - 2);
                        var sep = new char[] { ',' };
                        string[] size = subString.Split(sep);
                        returnList = size.ToList();
                    }
                }
                return returnList;
            }
        }

        public List<double> AveragePixelSizes
        {
            get
            {
                var returnList = new List<double>();
                var avgPixelSizes = XmlImagePyramid.Elements(XmlTags.AvgPixelSizes);
                if (avgPixelSizes.Count() > 0)
                {
                    string avgString = avgPixelSizes.First().Value.Trim();
                    if (!avgString.IsNullOrEmpty())
                    {
                        string subString = avgString.Substring(1, avgString.Length - 2);
                        var sep = new char[] { ',' };
                        string[] size = subString.Split(sep);
                        returnList =
                            size.Select(x => Double.Parse(x, Localization.FormatEnUS)).ToList();
                    }
                }
                return returnList;
            }
        }

        public string RootTileName
        {
            get
            {
                return XmlImagePyramid.Element(XmlTags.RootTile).Value.Trim();
            }
        }

        public Dictionary<string, IEnumerable<string>> SubTileMap
        {
            get
            {
                return XmlHelper.SubMap(XmlImagePyramid.Element(XmlTags.SubTileMap));
            }
        }

        #endregion
    }

    internal static class XmlHelper
    {
        public static Dictionary<string, IEnumerable<string>> SubMap(XElement submap)
        {
                var xmlSubPatchMap = submap.Elements(XmlTagsGlobal.Item);
                return xmlSubPatchMap.ToDictionary(
                    x => x.Element(XmlTagsGlobal.Key).Value.Trim(),
                    y => y.Element(XmlTagsGlobal.Val).Elements().Select(z => z.Value.Trim())
                    );
        }
    }
}

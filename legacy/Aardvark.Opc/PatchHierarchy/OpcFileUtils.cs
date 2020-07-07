using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Aardvark.Runtime;

namespace Aardvark.Opc.PatchHierarchy
{
    public static class OpcFileUtils
    {
        /// <summary>
        /// Retrieves folderpaths within given directory which correspond to opc layout
        /// </summary>
        public static IEnumerable<string> OpcPathsFromFolder(string opcFolderPath)
        {
            var opcFolders = StorageConfig.GetDirectories(opcFolderPath).Where(x => OpcFileUtils.IsOpc(x));
            var resultList = new List<string>();

            foreach (var opc in opcFolders)
            {
                yield return opc;
            }
        }

        /// <summary>
        /// Gets the filepath to root patch.xml from opc folder
        /// </summary>
        public static string GetRootPatchPath(string inFilePath)
        {
            var filePath = Path.Combine(inFilePath, "patches\\patchhierarchy.xml");

            var xmlHierarchy = XElement.Load(filePath, LoadOptions.None);
            var patchHierarchy = xmlHierarchy.Elements("PatchHierarchy");
            var rootPatchname = patchHierarchy.Elements("RootPatch").First().Value.Trim();

            return Path.Combine(inFilePath, "patches\\" + rootPatchname + "\\patch.xml");
        }

        /// <summary>
        /// Checks for a given directory if the filestructure corresponds to VilmaSpec
        /// </summary>
        public static bool IsOpc(string filepath)
        {
            string imagePath = Path.Combine(filepath, "images");
            string patchPath = Path.Combine(filepath, "patches");

            bool dirsExist = StorageConfig.DirectoryExists(imagePath) && StorageConfig.DirectoryExists(patchPath);
            if (!dirsExist)
                return false;

            return StorageConfig.FileExists(patchPath + "\\patchhierarchy.xml");
        }


        /// <summary>
        /// Checks for a given directory if it contains surfaces
        /// </summary>
        public static bool IsSurfaceFolder(string dir)
        {
            bool isSurfaceFolder = false;
            var subfolders = Directory.GetDirectories(dir);
            foreach (var s in subfolders)
            {
                isSurfaceFolder |= IsOpc(s);
            }

            return isSurfaceFolder;
        }

        /// <summary>
        /// Takes first OPC in opcDirectory and returns its root patch info. This
        /// is typically needed to retrieve rendering offsets to prevent rounding errors.
        /// </summary>
        public static PatchFileInfo GetFirstPatchFileInfo(string opcDirectory, PositionsType posType)
        {
            var paths = OpcFileUtils.OpcPathsFromFolder(opcDirectory);
            var rootPatchPath = OpcFileUtils.GetRootPatchPath(paths.First());

            return PatchFileInfo.FromFile(rootPatchPath);
        }
    }
}


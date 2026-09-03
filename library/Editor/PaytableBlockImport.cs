using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CGS.PaytableLibrary
{
    /// <summary>
    /// Copies the block library into the game's own bundle before assembly, so the finished
    /// paytable nests LOCAL prefabs instead of ones inside a read-only package.
    ///
    /// WHY. A git-resolved package lives under <c>Library/PackageCache/</c>, which is read-only and
    /// wiped on every re-resolve. Assembling straight from there leaves a shipped prefab whose
    /// nested instances point into a package: overrides cannot be applied back, the asset bundle
    /// depends on something outside itself, and anyone without the package opens a prefab full of
    /// missing references. The previous way out was to UNPACK the whole prefab — one game went from
    /// 143 KB and structured to 933 KB of 305 inline objects, and lost every link to the library in
    /// the process. Copying first costs a folder and keeps the nesting.
    ///
    /// WHAT IT IS NOT. The copies are a snapshot. A later fix to a library block does not reach a
    /// game that already imported — re-run this to pick it up. That is the same staleness unpacking
    /// had, except re-importing is mechanical where re-laying-out was not.
    ///
    /// THE PART THAT IS NOT JUST A FILE COPY. The blocks reference each other —
    /// <c>GridPage -> GridCell -> IconSlot, PayRows</c> and
    /// <c>StackPage -> SpecialPanel -> PanelRow -> IconSlot, PayRows</c> — so copied blocks still
    /// point at the PACKAGE originals for their own children. Left alone, half the tree would be
    /// local and half still in the package, which is worse than either. Every intra-library GUID in
    /// the copies is therefore rewritten to the local copy's GUID, and
    /// <see cref="Verify"/> fails the import if a single package reference survives.
    /// </summary>
    public static class PaytableBlockImport
    {
        public const string FolderName = "Nested";

        public sealed class Report
        {
            public string Folder;
            public int Copied;
            public int Reused;
            public int GuidsRewritten;
            public readonly List<string> Warnings = new List<string>();
            public bool Ok => Warnings.Count == 0;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Ok ? "IMPORT OK" : "IMPORT FAILED").Append("  ");
                sb.Append($"{Copied} copied, {Reused} already present (GUID kept), ");
                sb.Append($"{GuidsRewritten} reference(s) repointed  ->  {Folder}");
                foreach (var w in Warnings) sb.Append("\n  ! ").Append(w);
                return sb.ToString();
            }
        }

        /// <summary>
        /// <paramref name="paytableFolder"/> is the game's own paytable folder, e.g.
        /// <c>Assets/Bundles/_gel/_games/&lt;slot&gt;/Prefabs/Paytable</c>. The blocks land in a
        /// <c>Nested</c> subfolder of it.
        ///
        /// Re-running is safe and is the way to pick up a library change: a block that is already
        /// there is overwritten IN PLACE, keeping its GUID, so an assembled paytable stays linked
        /// to it instead of being orphaned.
        /// </summary>
        public static Report Import(string paytableFolder)
        {
            var report = new Report();

            var packageRoot = PaytablePaths.PackageRoot;
            if (string.IsNullOrEmpty(packageRoot))
            {
                report.Warnings.Add("the block library package is not resolved in this project");
                return report;
            }
            var sourceDir = Path.Combine(packageRoot, "Blocks");
            if (!Directory.Exists(sourceDir))
            {
                report.Warnings.Add("no Blocks/ folder at " + sourceDir);
                return report;
            }

            if (string.IsNullOrEmpty(paytableFolder) || !paytableFolder.StartsWith("Assets/"))
            {
                report.Warnings.Add("paytableFolder must be a project path under Assets/, got: " +
                                    paytableFolder);
                return report;
            }

            var dstDir = paytableFolder.TrimEnd('/') + "/" + FolderName;
            report.Folder = dstDir;
            if (!AssetDatabase.IsValidFolder(dstDir))
                AssetDatabase.CreateFolder(paytableFolder.TrimEnd('/'), FolderName);

            // Package path -> project path. AssetDatabase understands "Packages/<name>/..." for a
            // resolved package, which is what CopyAsset needs; the filesystem path under
            // Library/PackageCache is not an asset path.
            var pkgAssetDir = "Packages/" + PaytablePaths.PackageName + "/Blocks";

            var map = new Dictionary<string, string>();   // package GUID -> local GUID
            var localPaths = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*.prefab"))
                {
                    var name = Path.GetFileName(file);
                    var src = pkgAssetDir + "/" + name;
                    var dst = dstDir + "/" + name;

                    var srcGuid = AssetDatabase.AssetPathToGUID(src);
                    if (string.IsNullOrEmpty(srcGuid))
                    {
                        report.Warnings.Add("could not resolve " + src + " as an asset");
                        continue;
                    }

                    if (File.Exists(dst))
                    {
                        // Overwrite the CONTENT and leave the .meta alone. AssetDatabase.CopyAsset
                        // onto an existing path would give the copy a new GUID and silently orphan
                        // every paytable already nesting it.
                        File.Copy(file, dst, true);
                        report.Reused++;
                    }
                    else
                    {
                        if (!AssetDatabase.CopyAsset(src, dst))
                        {
                            report.Warnings.Add("CopyAsset failed: " + src + " -> " + dst);
                            continue;
                        }
                        report.Copied++;
                    }
                    localPaths.Add(dst);
                    map[srcGuid] = null;   // filled after the batch, once the .meta exists
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            // Now that the copies are imported, each has a GUID to point at.
            foreach (var file in Directory.GetFiles(sourceDir, "*.prefab"))
            {
                var name = Path.GetFileName(file);
                var srcGuid = AssetDatabase.AssetPathToGUID(pkgAssetDir + "/" + name);
                var dstGuid = AssetDatabase.AssetPathToGUID(dstDir + "/" + name);
                if (!string.IsNullOrEmpty(srcGuid) && !string.IsNullOrEmpty(dstGuid))
                    map[srcGuid] = dstGuid;
            }

            foreach (var path in localPaths)
            {
                var text = File.ReadAllText(path);
                var rewritten = 0;
                foreach (var kv in map)
                {
                    if (string.IsNullOrEmpty(kv.Value) || kv.Key == kv.Value) continue;
                    var before = text;
                    text = text.Replace(kv.Key, kv.Value);
                    if (!ReferenceEquals(before, text) && before != text)
                        rewritten += CountOccurrences(before, kv.Key);
                }
                if (rewritten > 0)
                {
                    File.WriteAllText(path, text);
                    report.GuidsRewritten += rewritten;
                }
            }
            AssetDatabase.Refresh();

            var leaks = Verify(dstDir, map);
            if (leaks.Count > 0)
                report.Warnings.AddRange(leaks);
            return report;
        }

        /// <summary>
        /// Every reference that still points into the package. Non-empty means the import left the
        /// tree half local and half packaged, which is the one outcome worse than not importing —
        /// so callers must treat it as a failure, not a note.
        /// </summary>
        public static List<string> Verify(string dstDir, Dictionary<string, string> map)
        {
            var problems = new List<string>();
            if (!Directory.Exists(dstDir)) return problems;
            foreach (var path in Directory.GetFiles(dstDir, "*.prefab"))
            {
                var text = File.ReadAllText(path);
                foreach (var kv in map)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    var n = CountOccurrences(text, kv.Key);
                    if (n > 0)
                        problems.Add($"{Path.GetFileName(path)} still references the package copy " +
                                     $"{kv.Key} ({n}x) — the nesting would straddle the package");
                }
            }
            return problems;
        }

        static int CountOccurrences(string haystack, string needle)
        {
            var n = 0;
            for (var i = haystack.IndexOf(needle, System.StringComparison.Ordinal);
                 i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal))
                n++;
            return n;
        }

        /// <summary>The local block to instantiate, by block name (e.g. "GridPage").</summary>
        public static GameObject Block(string paytableFolder, string blockName)
        {
            var path = paytableFolder.TrimEnd('/') + "/" + FolderName + "/" + blockName + ".prefab";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
                Debug.LogError($"[paytable] {blockName} not found at {path}. Run the block import " +
                               "before assembly — instantiating from the package instead is what " +
                               "forces the prefab to be unpacked later.");
            return go;
        }
    }
}

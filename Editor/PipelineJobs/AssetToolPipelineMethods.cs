﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using BundleKit.Assets;
using BundleKit.Utility;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ThunderKit.Common.Logging;
using ThunderKit.Core.Data;
using UnityEditor;

namespace BundleKit.PipelineJobs
{
    public static class AssetToolPipelineMethods
    {
        public static void InitializePaths(DefaultAsset bundle, string outputAssetBundlePath, out string resourcesFilePath, out string ggmPath, out string fileName, out string bundlePath, out string dataDirectoryPath)
        {
            var settings = ThunderKitSetting.GetOrCreateSettings<ThunderKitSettings>();
            dataDirectoryPath = Path.Combine(settings.GamePath, $"{Path.GetFileNameWithoutExtension(settings.GameExecutable)}_Data");
            resourcesFilePath = Path.Combine(dataDirectoryPath, "resources.assets");
            ggmPath = Path.Combine(dataDirectoryPath, "globalgamemanagers");
            fileName = Path.GetFileName(outputAssetBundlePath);
            bundlePath = AssetDatabase.GetAssetPath(bundle);
        }
        public static void InitializeAssetTools(string resourcesFilePath, string path, ref AssetsManager am, out BundleFileInstance bun, out AssetsFileInstance bundleAssetsFile, out AssetsFileInstance resourcesInst, out AssetFileInfoEx assetBundleAsset, out AssetExternal assetBundleExtAsset, out AssetTypeValueField bundleBaseField)
        {
            am = new AssetsManager();
            //Load bundle file and its AssetsFile
            bun = am.LoadBundleFile(path, true);
            bundleAssetsFile = am.LoadAssetsFileFromBundle(bun, 0);

            // Load assets files from Resources
            resourcesInst = am.LoadAssetsFile(resourcesFilePath, true);

            //Load AssetBundle asset from Bundle AssetsFile so that we can update its data later
            assetBundleAsset = bundleAssetsFile.table.GetAssetsOfType((int)AssetClassID.AssetBundle)[0];
            assetBundleExtAsset = am.GetExtAsset(bundleAssetsFile, 0, assetBundleAsset.index);

            //load data for classes
            var classDataPath = Path.Combine("Packages", "com.passivepicasso.bundlekit", "Library", "classdata.tpk");
            am.LoadClassPackage(classDataPath);
            am.LoadClassDatabaseFromPackage(resourcesInst.file.typeTree.unityVersion);

            bundleBaseField = assetBundleExtAsset.instance.GetBaseField();
        }
        public static void InitializeContainers(string[] regexFilters, out List<AssetsReplacer> assetsReplacers, out List<string> contexts, out List<AssetTypeValueField> newContainerChildren, out Regex[] nameRegex)
        {
            assetsReplacers = new List<AssetsReplacer>();
            contexts = new List<string>();
            newContainerChildren = new List<AssetTypeValueField>();
            nameRegex = regexFilters.Select(filter => new Regex(filter)).ToArray();
        }

        public static IEnumerable<AssetData> CollectAssets(AssetsManager am, AssetsFileInstance resourcesInst, Regex[] nameRegex, AssetClassID[] classes, ProgressBar progressBar)
        {
            // Iterate over all requested Class types and collect the data required to copy over the required asset information
            // This step will recurse over dependencies so all required assets will become available from the resulting bundle
            progressBar.Update(title: "Mapping PathIds to Resource Paths");
            progressBar.Update(title: "Collecting Assets");
            var clss = new HashSet<uint>(classes.Select(cls => (uint)cls));
            var visited = new HashSet<AssetID>();
            var targetAssets = Enumerable.Empty<AssetData>();
            foreach (var cls in classes)
            {
                var fileInfos = resourcesInst.table.GetAssetsOfType((int)cls);
                for (var x = 0; x < fileInfos.Count; x++)
                {
                    var assetFileInfo = resourcesInst.table.assetFileInfo[x];
                    var name = AssetHelper.GetAssetNameFast(resourcesInst.file, am.classFile, assetFileInfo);
                    progressBar.Update($"[Loading] ({(AssetClassID)assetFileInfo.curFileType}) {name}", $"Collecting Assets ({x} / {fileInfos.Count})", progress: x / (float)fileInfos.Count);
                    if (!clss.Contains(assetFileInfo.curFileType)) continue;

                    // If a name Regex filter is applied, and it does not match, continue
                    int i = 0;
                    for (; i < nameRegex.Length; i++)
                        if (nameRegex[i] != null && nameRegex[i].IsMatch(name))
                            break;
                    if (i == nameRegex.Length) continue;

                    var ext = am.GetExtAsset(resourcesInst, 0, assetFileInfo.index);
                    //if (ext.file.name.Contains("unity_builtin_extra"))
                    //    continue;

                    // Find name, path and fileId of each asset referenced directly and indirectly by assetFileInfo including itself
                    targetAssets = targetAssets.Concat(resourcesInst.GetDependentAssetIds(visited, ext.instance.GetBaseField(), am, progressBar)
                                               .Prepend((ext, name, resourcesInst.name, 0, assetFileInfo.index, 0)));
                }
            }
            return targetAssets;
        }


        public static void AddDependency(this AssetsFileInstance assetsFileInst, string assetsFilePath)
        {
            var dependencies = assetsFileInst.file.dependencies.dependencies;

            dependencies.Add(
                new AssetsFileDependency
                {
                    assetPath = assetsFilePath,
                    originalAssetPath = assetsFilePath,
                    bufferedPath = string.Empty
                }
            );
            assetsFileInst.file.dependencies.dependencyCount = dependencies.Count;
            assetsFileInst.file.dependencies.dependencies = dependencies;
        }

        public static void UpdateAssetBundleDependencies(AssetTypeValueField bundleBaseField, List<AssetsFileDependency> dependencies)
        {
            var dependencyArray = bundleBaseField.GetField("m_Dependencies/Array");
            var dependencyFieldChildren = new List<AssetTypeValueField>();

            foreach (var dep in dependencies)
            {
                var depTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(dependencyArray);
                depTemplate.GetValue().Set(dep.assetPath);
                dependencyFieldChildren.Add(depTemplate);
            }
            dependencyArray.SetChildrenList(dependencyFieldChildren.ToArray());
        }
    }
}
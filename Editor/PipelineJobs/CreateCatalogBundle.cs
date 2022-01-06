using AssetsTools.NET;
using AssetsTools.NET.Extra;
using BundleKit.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ThunderKit.Common.Logging;
using ThunderKit.Core.Data;
using ThunderKit.Core.Pipelines;
using UnityEditor;
using UnityEngine;
using static BundleKit.Utility.Extensions;

namespace BundleKit.PipelineJobs
{
    [PipelineSupport(typeof(Pipeline))]
    public class CreateCatalogBundle : PipelineJob
    {
        public DefaultAsset templateBundle;
        public string outputAssetBundlePath;
        public Filter[] filters;

        public override Task Execute(Pipeline pipeline)
        {
            var am = new AssetsManager();
            var assetsReplacers = new List<AssetsReplacer>();

            using (var progressBar = new ProgressBar("Constructing AssetBundle"))
                try
                {
                    void Log(string title = null, string message = null, float progress = -1, bool log = true, params string[] context)
                    {
                        if (log && (message ?? title) != null)
                            pipeline.Log(LogLevel.Information, message ?? title, context);

                        progressBar.Update(message, title, progress);
                    }

                    var templateBundlePath = AssetDatabase.GetAssetPath(templateBundle);

                    var settings = ThunderKitSetting.GetOrCreateSettings<ThunderKitSettings>();
                    var gameName = Path.GetFileNameWithoutExtension(settings.GameExecutable);

                    var dataDirectoryPath = Path.Combine(settings.GamePath, $"{gameName}_Data");
                    var classDataPath = Path.Combine("Packages", "com.passivepicasso.bundlekit", "Library", "classdata.tpk");

                    var sharedAssetsFiles = Directory.EnumerateFiles(dataDirectoryPath, "sharedassets*.assets").ToArray();
                    var levelFiles = Directory.EnumerateFiles(dataDirectoryPath, "level*").Where(file => Path.GetExtension(file) == string.Empty).ToArray();
                    var resourcesFilePath = Path.Combine(dataDirectoryPath, "resources.assets");
                    var ggmAssetsPath = Path.Combine(dataDirectoryPath, "globalgamemanagers.assets");
                    var ggmPath = Path.Combine(dataDirectoryPath, "globalgamemanagers");

                    var targetFiles = Enumerable.Empty<string>().Concat(sharedAssetsFiles).Prepend(resourcesFilePath).Prepend(ggmAssetsPath).ToArray();

                    am.LoadClassPackage(classDataPath);
                    am.LoadClassDatabaseFromPackage(Application.unityVersion);
                    am.PrepareNewBundle(templateBundlePath, out var bun, out var bundleAssetsFile, out var assetBundleExtAsset);

                    var bundleBaseField = assetBundleExtAsset.instance.GetBaseField();

                    var containerArray = bundleBaseField.GetField("m_Container/Array");
                    var dependencyArray = bundleBaseField.GetField("m_Dependencies/Array");
                    var preloadTableArray = bundleBaseField.GetField("m_PreloadTable/Array");

                    var preloadChildren = new List<AssetTypeValueField>();
                    var dependencyChildren = new List<AssetTypeValueField>();
                    var mContainerChildren = new List<AssetTypeValueField>();
                    var dependencies = new Dictionary<string, int>();
                    var streamReaders = new Dictionary<string, Stream>();

                    var tempResSFilePath = $"Temp/ThunderKit/resS/{bundleAssetsFile.name}.resS";
                    Directory.CreateDirectory(Path.GetDirectoryName(tempResSFilePath));
                    var resSFileStream = File.Create(tempResSFilePath);
                    string resSInternalPath = $"archive:/{bundleAssetsFile.name}/{bundleAssetsFile.name}.resS";

                    bundleAssetsFile.file.dependencies.dependencies.Clear();
                    bundleAssetsFile.file.dependencies.dependencyCount = 0;
                    bundleAssetsFile.dependencies.Clear();

                    var compiledFilters = filters.Select(f => (assetClass: f.assetClass, nameRegex: f.nameRegex.Select(reg => new Regex(reg)).ToArray())).ToArray();
                    IEnumerable<AssetTree> treeEnumeration =
                        targetFiles.SelectMany(p =>
                            compiledFilters.SelectMany(filter =>
                                am.GetAssetsInst(p).CollectAssetTrees(am, filter.nameRegex, filter.assetClass, Log)
                            )
                        );

                    var felledTree = treeEnumeration.SelectMany(tree => tree.Flatten(true));
                    var localGroups = felledTree.GroupBy(tree => tree).ToArray();
                    var localIdMap = new Dictionary<AssetTree, long>();
                    var reverseMap = new Dictionary<AssetTree, Dictionary<(int, long), (int, long)>>();
                    Log($"Generating Tree Map");
                    for (long i = 0; i < localGroups.Length; i++)
                    {
                        var assetTree = localGroups[i].First();
                        localIdMap[assetTree] = i + 2;
                        Log(message: $"{assetTree.name} = {i + 2}");
                    }
                    Log($"Writing Assets");
                    foreach (var group in localGroups)
                    {
                        var assetTree = group.First();
                        var localId = localIdMap[group.Key];
                        var asset = assetTree.assetExternal;
                        var baseField = assetTree.assetExternal.instance.GetBaseField();

                        Log(message: $"Remapping ({baseField.GetFieldType()}) {assetTree.name} PPts");
                        var remap = assetTree.Children.Distinct().ToDictionary(child => (child.FileId, child.PathId), child => (0, localIdMap[child]));
                        //reverseMap.Add(assetTree, remap.ToDictionary(map => map.Value, map => map.Key));
                        //var pptrs = baseField.FindFieldType(typeName => typeName.StartsWith("PPtr<") && typeName.EndsWith(">"));
                        //foreach (var pptr in pptrs)
                        //{
                        //    var assetId = asset.file.ConvertToAssetID(pptr.GetValue("m_FileID").AsInt(), pptr.GetValue("m_PathID").AsInt64());

                        //}
                        baseField.RemapPPtrs(remap);

                        var tableData = assetTree.Flatten(true).Distinct().ToArray();
                        var preloadIndex = preloadChildren.Count;
                        foreach (var data in tableData)
                        {
                            var entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(preloadTableArray);
                            entry.SetValue("m_FileID", 0);
                            entry.SetValue("m_PathID", localIdMap[data]);
                            preloadChildren.Add(entry);
                        }
                        switch (baseField.GetFieldType())
                        {
                            case "Texture2D":
                                {
                                    TextureFile texFile = TextureFile.ReadTextureFile(baseField);

                                    var position = resSFileStream.Length;
                                    texFile.CopyTextureData(streamReaders, resSFileStream, dataDirectoryPath, resSInternalPath);
                                    texFile.WriteTextureFile(baseField, false);
                                    var assetBytes = asset.instance.WriteToByteArray();
                                    var currentAssetReplacer = new AssetsReplacerFromMemory(0, localId, (int)asset.info.curFileType,
                                                                                            AssetHelper.GetScriptIndex(asset.file.file, asset.info),
                                                                                            assetBytes);
                                    assetsReplacers.Add(currentAssetReplacer);
                                }
                                break;
                            default:
                                {
                                    var assetBytes = asset.instance.WriteToByteArray();
                                    var currentAssetReplacer = new AssetsReplacerFromMemory(0, localId, (int)asset.info.curFileType,
                                                                                            AssetHelper.GetScriptIndex(asset.file.file, asset.info),
                                                                                            assetBytes);
                                    assetsReplacers.Add(currentAssetReplacer);
                                }
                                break;
                        }
                        mContainerChildren.Add(containerArray.CreateEntry(assetTree.name, 0, localId, preloadIndex, preloadChildren.Count - preloadIndex));
                    }
                    //var maps = reverseMap.Select(map => (map.Key, map.Value.Select(kvp => (kvp.Key, kvp.Value)).ToArray())).ToArray();

                    foreach (var dependency in dependencies.OrderBy(dep => dep.Value).Select(dep => dep.Key))
                    {
                        string path = Path.Combine(dataDirectoryPath, dependency);
                        bundleAssetsFile.AddDependency(path);

                        var depTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(dependencyArray);
                        depTemplate.GetValue().Set(path);
                        dependencyChildren.Add(depTemplate);
                    }

                    preloadTableArray.SetChildrenList(preloadChildren.ToArray());
                    containerArray.SetChildrenList(mContainerChildren.ToArray());
                    dependencyArray.SetChildrenList(dependencyChildren.ToArray());

                    var newAssetBundleBytes = bundleBaseField.WriteToByteArray();
                    assetsReplacers.Insert(0, new AssetsReplacerFromMemory(0, assetBundleExtAsset.info.index, (int)assetBundleExtAsset.info.curFileType, 0xFFFF, newAssetBundleBytes));

                    foreach (var stream in streamReaders)
                        stream.Value.Dispose();

                    byte[] newAssetData;
                    using (var bundleStream = new MemoryStream())
                    using (var writer = new AssetsFileWriter(bundleStream))
                    {
                        bundleAssetsFile.file.Write(writer, 0, assetsReplacers, 0);
                        newAssetData = bundleStream.ToArray();
                    }

                    resSFileStream.Position = 0;
                    byte[] resFileBytes;
                    using (var memStream = new MemoryStream())
                    {
                        resSFileStream.CopyTo(memStream);
                        resFileBytes = memStream.ToArray();
                    }

                    using (var file = File.OpenWrite(outputAssetBundlePath))
                    using (var writer = new AssetsFileWriter(file))
                        bun.file.Write(writer, new List<BundleReplacer> {
                        new BundleReplacerFromMemory(bundleAssetsFile.name, bundleAssetsFile.name, true, newAssetData, -1),
                        new BundleReplacerFromMemory($"{bundleAssetsFile.name}.resS", $"{bundleAssetsFile.name}.resS", false, resFileBytes, -1)
                    });

                    resSFileStream.Dispose();
                }
                finally
                {
                    am.UnloadAll(true);
                }
            return Task.CompletedTask;
        }


    }
}

﻿using AssetsTools.NET.Extra;
using BundleKit.Assets;
using BundleKit.PipelineJobs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThunderKit.Common.Package;
using UnityEditor;
using UnityEngine;
using UnityEditor.Experimental.AssetImporters;
using BundleKit.Utility;

namespace BundleKit.Bundles
{
    using static HideFlags;
    [ScriptedImporter(15, new[] { Extension })]
    public class AssetsReferenceImporter : ScriptedImporter
    {
        public const string Extension = "assetsreference";

        public MaterialDefinition[] customMaterialDefinitions;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var am = new AssetsManager();
            var classDataPath = Path.Combine("Packages", "com.passivepicasso.bundlekit", "Library", "classdata.tpk");
            am.LoadClassPackage(classDataPath);
            am.LoadClassDatabaseFromPackage(Application.unityVersion);

            am.PrepareNewBundle(ctx.assetPath, out var bun, out var bundleAssetsFile, out var assetBundleExtAsset);

            var bundleBaseField = assetBundleExtAsset.instance.GetBaseField();
            var dependencyArray = bundleBaseField.GetField("m_Dependencies/Array");
            var dependencies = dependencyArray.GetChildrenList().Select(dep => dep.GetValue().AsString()).ToArray();
            var bundleName = bundleBaseField.GetValue("m_AssetBundleName").AsString();

            am.UnloadAll();
            for (int i = 0; i < dependencies.Length; i++)
            {
                var dependency = dependencies[i];
                if (dependency == Extensions.unityBuiltinExtra || dependency == Extensions.unityDefaultResources)
                    continue;

                dependency = Path.Combine(Path.GetDirectoryName(ctx.assetPath), dependency).Replace("\\", "/");
                ctx.DependsOnSourceAsset(dependency);
                dependencies[i] = dependency;
            }
            var dependencyReferences = dependencies.Select(AssetDatabase.LoadAssetAtPath<AssetsReferenceBundle>).Where(arb => arb != null).ToArray();

            var loadedBundles = AssetBundle.GetAllLoadedAssetBundles();
            var bundle = loadedBundles.FirstOrDefault(bnd => bundleName.Equals(bnd.name));
            bundle?.Unload(true);
            try
            {
                bundle = AssetBundle.LoadFromFile(ctx.assetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load: {ctx.assetPath}");
            }
            bundle.hideFlags = HideAndDontSave | DontSaveInBuild;

            var bundleAsset = ScriptableObject.CreateInstance<AssetsReferenceBundle>();
            bundleAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset(bundleAsset.name, bundleAsset);
            ctx.SetMainObject(bundleAsset);
            bundleAsset.dependencies = dependencyReferences;
            bundleAsset.dependencyNames = dependencies.ToArray();
            Debug.Log($"Importing Bundle: {ctx.assetPath}", bundleAsset);

            var allLoadedAssets = bundle.LoadAllAssets();
            bundleAsset.Assets = allLoadedAssets.OrderBy(a =>
            {
                var foundInfo = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(a, out string guid, out long localId);
                return localId;
            }).ToArray();
            bundleAsset.LocalIds = new long[bundleAsset.Assets.Length];

            var textureLookup = new Dictionary<long, Texture>();
            for (int i = 0; i < bundleAsset.Assets.Length; i++)
            {
                var asset = bundleAsset.Assets[i];
                var foundInfo = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
                bundleAsset.LocalIds[i] = localId;
                if (asset is Shader shader)
                {
                    ShaderUtil.RegisterShader(shader);
                    continue;
                }
                if (asset is Texture2D)
                {
                    if (foundInfo)
                        textureLookup[localId] = asset as Texture;
                }
                ctx.AddObjectToAsset(asset.name, asset);
            }
            if (customMaterialDefinitions != null && customMaterialDefinitions.Any())
            {
                var validDefinitions = customMaterialDefinitions.Where(md => !string.IsNullOrEmpty(md.name) && !string.IsNullOrEmpty(md.shader)).ToArray();
                var customMaterials = new Material[validDefinitions.Length];
                for (int i = 0; i < validDefinitions.Length; i++)
                {
                    customMaterials[i] = new Material(Shader.Find(validDefinitions[i].shader))
                    {
                        name = $"{validDefinitions[i].name} (Custom Asset)",
                        hideFlags = None
                    };

                    var nameHash = PackageHelper.GetStringHash(customMaterials[i].name);
                    var metaDataPath = Path.Combine("Library", "BundleKitMetaData", $"{nameHash}.json");
                    if (File.Exists(metaDataPath))
                    {
                        var jsonData = File.ReadAllText(metaDataPath);
                        var shaderData = JsonUtility.FromJson<SerializableMaterialData>(jsonData);
                        shaderData.Apply(customMaterials[i], textureLookup);
                    }
                    ctx.AddObjectToAsset(customMaterials[i].name, customMaterials[i]);
                }
                bundleAsset.CustomMaterials = customMaterials;
            }
        }
    }
}
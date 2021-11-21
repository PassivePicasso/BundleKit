﻿using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Collections.Generic;

namespace BundleKit.Assets
{
    internal struct AssetData
    {
        public readonly AssetExternal AssetExt;
        public readonly AssetTypeValueField PPtr;
        public readonly string AssetName;
        public readonly int FileId;
        public readonly long PathId;
        public readonly int Depth;

        public AssetData(AssetExternal ext, AssetTypeValueField field, string name, int fileId, long pathId, int depth)
        {
            this.AssetExt = ext;
            this.PPtr = field;
            this.AssetName = name;
            this.FileId = fileId;
            this.PathId = pathId;
            this.Depth = depth;
        }

        public override bool Equals(object obj)
        {
            return obj is AssetData other &&
                   EqualityComparer<AssetExternal>.Default.Equals(AssetExt, other.AssetExt) &&
                   EqualityComparer<AssetTypeValueField>.Default.Equals(PPtr, other.PPtr) &&
                   AssetName == other.AssetName &&
                   FileId == other.FileId &&
                   PathId == other.PathId &&
                   Depth == other.Depth;
        }

        public override int GetHashCode()
        {
            int hashCode = -359814420;
            hashCode = hashCode * -1521134295 + AssetExt.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<AssetTypeValueField>.Default.GetHashCode(PPtr);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(AssetName);
            hashCode = hashCode * -1521134295 + FileId.GetHashCode();
            hashCode = hashCode * -1521134295 + PathId.GetHashCode();
            hashCode = hashCode * -1521134295 + Depth.GetHashCode();
            return hashCode;
        }

        public void Deconstruct(out AssetExternal ext, out AssetTypeValueField field, out string name, out int fileId, out long pathId, out int depth)
        {
            ext = this.AssetExt;
            field = this.PPtr;
            name = this.AssetName;
            fileId = this.FileId;
            pathId = this.PathId;
            depth = this.Depth;
        }

        public static implicit operator (AssetExternal ext, AssetTypeValueField field, string name, int fileId, long pathId, int depth)(AssetData value)
        {
            return (value.AssetExt, value.PPtr, value.AssetName, value.FileId, value.PathId, value.Depth);
        }

        public static implicit operator AssetData((AssetExternal ext, AssetTypeValueField field, string name, int fileId, long pathId, int depth) value)
        {
            return new AssetData(value.ext, value.field, value.name, value.fileId, value.pathId, value.depth);
        }
    }
}
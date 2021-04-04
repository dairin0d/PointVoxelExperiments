﻿using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

using dairin0d.Data.Points;
using dairin0d.Data.Voxels;

namespace dairin0d.Rendering.Octree2 {
    public class Model3D {
        public string Name;
        public Bounds Bounds; // for camera frustum culling
        public ModelBone[] Bones;
        public ModelCage Cage;
        public ModelAttributeInfo[] AttributeInfos; // color, normal, UV coords...
        public ModelAttributeData[] AttributeDatas;
        public ModelPart[] Parts;
        public ModelGeometry[] Geometries;
    }

    public class ModelBone {
        public string Name;
        public int Parent; // index in Bones
        public Matrix4x4 Bindpose;
    }

    public struct ModelWeight {
        public int Index;
        public float Weight;
    }

    public class ModelCage {
        public Vector3[] Positions;
        public int[] WeightCounts;
        public ModelWeight[] Weights; // bindpose weights
    }

    public class ModelPoints {
        public int[] WeightCounts;
        public ModelWeight[] Weights; // cage weights
        public int[] Attributes; // indices in AttributeInfos
    }

    public class ModelPart {
        // Note: if vertices are not specified, then this part is not bouned by a cage volume
        // Otherwise, 4 (for tetrahedron) or 8 (for cube) vertices are expected
        public int[] Vertices; // cage vertex indices
        public int[] Corners; // cube corner indices
        public int[] Geometries; // indices in Geometries (can be multiple if there are animation frames)
    }

    public class ModelAttributeInfo {
        public string Name;
        public int Dimension;
        public int ElementSize;
        public int TotalSize;
        // Data type? (int/float, signed/unsigned, custom struct...)
    }

    public class ModelAttributeData {
        public int InfoIndex;
        public byte[] Data;
    }

    public abstract class ModelGeometry {
    }

    public enum ModelPointType {
        Rect, Circle, Cube
    }

    public interface IModelGeometrySplattable {
        Vector3 PointScale {get;}
        ModelPointType PointType {get;}
    }

    public abstract class ModelGeometryMesh : ModelGeometry {
        public ModelPoints Points;
    }

    public abstract class ModelGeometryVolume : ModelGeometry {
        public int[] Palettes; // indices in AttributeInfos
    }

    public abstract class ModelGeometryOctree : ModelGeometryVolume, IModelGeometrySplattable {
        public int MaxLevel;

        public Vector3 PointScale {get; private set;}
        public ModelPointType PointType {get; private set;}
    }

    public class ModelGeometryPointerOctree : ModelGeometryOctree {
        public int[] Nodes;
        public byte[] Data;
    }
}
// MIT License
//
// Copyright (c) 2017 dairin0d
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using UnityEngine;
using UnityEditor;

namespace dairin0d.Data.Conversion {
    [CustomEditor(typeof(Voxelizer)), CanEditMultipleObjects]
    public class VoxelizerEditor : Editor {
        protected virtual void OnSceneGUI() {
            var voxelizer = (Voxelizer)target;
            var m = Handles.matrix;
            var c = Handles.color;
            Handles.matrix = voxelizer.transform.localToWorldMatrix;
            Handles.color = Color.white;
            for (int z = -1; z <= 1; z++) {
                int az = System.Math.Abs(z);
                for (int y = -1; y <= 1; y++) {
                    int ay = System.Math.Abs(y);
                    for (int x = -1; x <= 1; x++) {
                        int ax = System.Math.Abs(x);
                        int sum = ax+ay+az;
                        if (sum == 0) continue;
                        if ((sum == 1) & !voxelizer.handles1D) continue;
                        if ((sum == 2) & !voxelizer.handles2D) continue;
                        if ((sum == 3) & !voxelizer.handles3D) continue;
                        if (voxelizer.colorizeHandles) {
                            Handles.color = new Color(ax, ay, az, 0.5f);
                        }
                        SizeHandle(voxelizer, new Vector3(x, y, z));
                    }
                }
            }
            Handles.color = c;
            Handles.matrix = m;
        }

        void SizeHandle(Voxelizer voxelizer, Vector3 direction) {
            Vector3 size = Vector3.Max(voxelizer.size, Vector3.zero);
            Vector3 hpos = Vector3.Scale(size, direction)*0.5f;
            Vector3 mask = Vector3.Max(direction, -direction);
            Vector3 inv_pos = voxelizer.transform.TransformPoint(-hpos);
            EditorGUI.BeginChangeCheck();
            hpos = Vector3.Scale(PointHandle(hpos), mask);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(voxelizer, "Change voxelizer bounds");
                Undo.RecordObject(voxelizer.transform, "Change voxelizer bounds");
                var masked_size = Vector3.Scale(hpos, direction)*2f;
                voxelizer.size = Vector3.Scale(size, Vector3.one - mask) + masked_size;
                Vector3 delta = inv_pos - voxelizer.transform.TransformPoint(-hpos);
                voxelizer.transform.position += delta;
            }
        }

        Vector3 PointHandle(Vector3 p) {
            float size = HandleUtility.GetHandleSize(p) * 0.05f;
            return Handles.FreeMoveHandle(p, Quaternion.identity, size, Vector3.zero, Handles.DotHandleCap);
        }

        [MenuItem("Voxel Tools/Add Voxelizer", priority=1)]
        static void AddVoxelizer() {
            Voxelizer.AddVoxelizer(Selection.activeTransform);
        }
    }
}
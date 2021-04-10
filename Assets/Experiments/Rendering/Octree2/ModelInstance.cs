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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace dairin0d.Rendering.Octree2 {
    public class ModelInstance : MonoBehaviour {
        private static LinkedList<ModelInstance> allInstances = new LinkedList<ModelInstance>();
        public static IEnumerable<ModelInstance> All => allInstances;

        public Model3D Model;
        public Transform[] Bones;

        private Bounds bounds;
        public Bounds Bounds => bounds; // in world space

        private Transform cachedTransform;
        public Transform CachedTransform => cachedTransform;

        private LinkedListNode<ModelInstance> listNode;

        private Model3D cachedModel;

        void Awake() {
            cachedTransform = transform;
        }

        void OnEnable() {
            listNode = allInstances.AddLast(this);
            cachedModel = null;
            bounds = default;
        }

        void OnDisable() {
            allInstances.Remove(listNode);
            cachedModel = null;
            bounds = default;
        }

        void LateUpdate() {
            if ((Model == cachedModel) && !cachedTransform.hasChanged) return;
            
            cachedModel = Model;
            
            if (Model == null) {
                bounds = default;
                return;
            }
            
            var matrix = cachedTransform.localToWorldMatrix;
            var center = Model.Bounds.center;
            var size = Model.Bounds.size;
            
            var newCenter = new Vector3 {
                x = matrix.m00*center.x + matrix.m01*center.y + matrix.m02*center.z + matrix.m03,
                y = matrix.m10*center.x + matrix.m11*center.y + matrix.m12*center.z + matrix.m13,
                z = matrix.m20*center.x + matrix.m21*center.y + matrix.m22*center.z + matrix.m23,
            };
            
            var X = new Vector3 {x = matrix.m00*size.x, y = matrix.m10*size.x, z = matrix.m20*size.x};
            var Y = new Vector3 {x = matrix.m01*size.y, y = matrix.m11*size.y, z = matrix.m21*size.y};
            var Z = new Vector3 {x = matrix.m02*size.z, y = matrix.m12*size.z, z = matrix.m22*size.z};
            var newSize = new Vector3 {
                x = (X.x < 0 ? -X.x : X.x) + (Y.x < 0 ? -Y.x : Y.x) + (Z.x < 0 ? -Z.x : Z.x),
                y = (X.y < 0 ? -X.y : X.y) + (Y.y < 0 ? -Y.y : Y.y) + (Z.y < 0 ? -Z.y : Z.y),
                z = (X.z < 0 ? -X.z : X.z) + (Y.z < 0 ? -Y.z : Y.z) + (Z.z < 0 ? -Z.z : Z.z),
            };
            
            bounds = new Bounds(newCenter, newSize);
            cachedTransform.hasChanged = false;
        }
    }
}
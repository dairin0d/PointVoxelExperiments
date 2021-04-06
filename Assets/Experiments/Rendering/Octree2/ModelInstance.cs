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
        public Model3D Model;
        public Transform[] Bones;

        public Bounds BoundingBox {get; private set;} // in world space

        private static HashSet<ModelInstance> allInstances = new HashSet<ModelInstance>();
        public static IEnumerable<ModelInstance> All => allInstances;

        void Start() {
            
        }

        void OnEnable() {
            allInstances.Add(this);
        }

        void OnDisable() {
            allInstances.Remove(this);
        }

        void LateUpdate() {
            if (Model == null) return;
            
            var matrix = transform.localToWorldMatrix;
            var center = Model.Bounds.center;
            var extents = Model.Bounds.extents;
            
            var bounds = new Bounds(matrix.MultiplyPoint3x4(center), Vector3.zero);
            
            for (int z = -1; z <= 1; z += 2) {
                for (int y = -1; y <= 1; y += 2) {
                    for (int x = -1; x <= 1; x += 2) {
                        var p = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        p = matrix.MultiplyPoint3x4(p);
                        bounds.Encapsulate(p);
                    }
                }
            }
            
            BoundingBox = bounds;
        }
    }
}
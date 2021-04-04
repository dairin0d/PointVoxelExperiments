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
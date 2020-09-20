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

namespace dairin0d.MeshLoading {
	public class PointCloudObject : MonoBehaviour {
		public PointCloudModel model;

		PointCloudModel prev_model = null;

		public bool is_visible { get; set; }
		public int resume_index { get; set; }
		public Matrix4x4 mvp_matrix;

		public Renderer viz_renderer;
		public Transform viz_transform;

		void Load() {
			if (model == prev_model) return;
			if (prev_model) {
				var prev_child = transform.Find("Viz");
				if (prev_child) Destroy(prev_child.gameObject);
			}

			if (!model) return;
			if (!model.mesh) return;

			prev_model = model;

			var size = model.mesh.bounds.size;
			var max_size = Mathf.Max(Mathf.Max(size.x, size.y), size.z);
			var scale = 1f / max_size;

			var child = new GameObject("Viz");
			var mesh_renderer = child.AddComponent<MeshRenderer>();
			var mesh_filter = child.AddComponent<MeshFilter>();
			var box_collider = child.AddComponent<BoxCollider>();

			child.transform.SetParent(transform, false);
			child.transform.localScale = Vector3.one * scale;
			child.transform.localPosition = -model.mesh.bounds.center * scale;

			mesh_filter.sharedMesh = model.mesh;
			mesh_renderer.sharedMaterial = new Material(Shader.Find("Unlit/UnlitPointsShader"));

			mesh_renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			mesh_renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			mesh_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mesh_renderer.receiveShadows = false;
			mesh_renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

			box_collider.center = model.mesh.bounds.center;
			box_collider.size = model.mesh.bounds.size;

			mesh_renderer = GetComponent<MeshRenderer>();
			if (mesh_renderer) mesh_renderer.enabled = false;

			viz_renderer = mesh_renderer;
			viz_transform = child.transform;
		}

		void Start() {
			var prev_child = transform.Find("Viz");
			if (prev_child) prev_model = model;
			Load();
		}
		
		void Update() {
			if (model != prev_model) Load();
		}
	}
}
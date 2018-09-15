﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
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
using System.IO;
using UnityEditor;
using UnityEngine;

// Note: PCD seems like a good existing format for uncompressed storage/manipulation
// http://pointclouds.org/documentation/tutorials/pcd_file_format.php

public class ModelConverterWizard : EditorWindow {
	public static ModelConverterWizard window = null;

	static bool initialized {
		get { return (window != null); }
	}

	ModelConverterWizard() {
		window = this;
	}

	// Static constructor is called if the class has [InitializeOnLoad] attribute
	// Or if window is opened as a part of default editor layout
	static ModelConverterWizard() {
		Init(true);
	}

	[MenuItem("Voxel Tools/Model Converter", priority=1)]
	static void InitFromMenu() {
		Init(false);
	}
	static void Init(bool window_exists=false) {
		if (initialized) return;

		if (!window_exists) {
			// If GetWindow() is called from the static constructor
			// while the window is still open, Unity throws an error!
			window = GetWindow<ModelConverterWizard>();
			window.titleContent = new GUIContent("Model Converter");
			window.autoRepaintOnSceneChange = true; // maybe disable later
		}

		//SceneView.onSceneGUIDelegate += OnSceneGUI;
	}

	string src_path = "", dst_path = "";

	Transform localCoords = null;
	float spatialResolution = 1f;
	bool includeChildren = true;

	bool writeBinary = false;
	bool writeCompressed = false;

	void OnGUI() {
		if (!initialized) return;

		float labelWidth = EditorGUIUtility.labelWidth;

		EditorGUIUtility.labelWidth = 40;
		EditorGUILayout.BeginHorizontal(); {
			src_path = EditorGUILayout.TextField("Load", src_path);
			if (GUILayout.Button("...", GUILayout.ExpandWidth(false))) {
				src_path = EditorUtility.OpenFilePanel("Source file", Path_GetDirectoryName(src_path), "");
				Repaint();
			}
		} EditorGUILayout.EndHorizontal();
		EditorGUILayout.BeginHorizontal(); {
			dst_path = EditorGUILayout.TextField("Save", dst_path);
			if (GUILayout.Button("...", GUILayout.ExpandWidth(false))) {
				string dst_dir = Path_GetDirectoryName(dst_path);
				if (string.IsNullOrEmpty(dst_dir)) dst_dir = Path_GetDirectoryName(src_path);
				string dst_file = Path.GetFileName(dst_path);
				if (string.IsNullOrEmpty(dst_file)) dst_file = Path.GetFileNameWithoutExtension(src_path);
				dst_path = EditorUtility.SaveFilePanel("Destination file", dst_dir, dst_file, "");
				Repaint();
			}
		} EditorGUILayout.EndHorizontal();

		EditorGUIUtility.labelWidth = labelWidth;

		Transform active_tfm = Selection.activeTransform;
		Voxelizer voxelizer = (active_tfm ? active_tfm.GetComponent<Voxelizer>() : null);

		if (string.IsNullOrEmpty(src_path)) {
			if (!voxelizer) {
				GUI.enabled = (bool)active_tfm;
				localCoords = (Transform)EditorGUILayout.ObjectField("Local coords", localCoords, typeof(Transform), true);
				spatialResolution = EditorGUILayout.FloatField("Spatial resolution", spatialResolution);
				includeChildren = EditorGUILayout.Toggle("Include children", includeChildren);
				GUI.enabled = true;
			} else {
				localCoords = (Transform)EditorGUILayout.ObjectField("Local coords", localCoords, typeof(Transform), true);
			}
		} else {
			string ext = Path.GetExtension(src_path).ToLowerInvariant();
			if (ext == ".ply") {
			} else if (ext == ".pcd") {
			} else if (ext == ".csv") {
			} else if (ext == ".txt") {
			} else {
				EditorGUILayout.HelpBox("Unknown input format: "+ext, MessageType.Error);
			}
		}

		if (!string.IsNullOrEmpty(dst_path)) {
			string ext = Path.GetExtension(dst_path).ToLowerInvariant();
			if (ext == ".ply") {
				writeBinary = EditorGUILayout.Toggle("Binary", writeBinary);
			} else if (ext == ".pcd") {
				writeBinary = EditorGUILayout.Toggle("Binary", writeBinary);
				writeCompressed = EditorGUILayout.Toggle("Compressed", writeCompressed);
			} else if (ext == ".csv") {
			} else if (ext == ".txt") {
			} else {
				EditorGUILayout.HelpBox("Unknown output format: "+ext, MessageType.Error);
			}
		}

		EditorGUILayout.Space();

		if (File.Exists(src_path)) {
			if (GUILayout.Button("Convert File")) Convert_File();
		} else if (!string.IsNullOrEmpty(src_path)) {
			GUI.enabled = false;
			GUILayout.Button("Convert File");
			GUI.enabled = true;
		} else {
			if (!voxelizer) {
				GUI.enabled = (bool)active_tfm;
				if (GUILayout.Button("Convert Selection (mesh sampling)")) Convert_Selection();
				GUI.enabled = true;
			} else {
				if (GUILayout.Button("Convert Selection (render-voxelizer)")) Convert_Selection();
			}
		}
	}

	static string Path_GetDirectoryName(string path) {
		if (string.IsNullOrEmpty(path)) return "";
		return Path.GetDirectoryName(path);
	}

	// For some reason, EditorGUILayout's Toggle has incorrect rectangle
	static bool Checkbox(bool value) {
		var r = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.MinWidth(8));
		value = EditorGUI.Toggle(r, value);
		return value;
	}

	static bool ToggleButton(string label, bool value) {
		var r = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.MinWidth(8));
		value = EditorGUI.Toggle(r, value, EditorStyles.miniButton);
		var alignment = EditorStyles.label.alignment;
		EditorStyles.label.alignment = TextAnchor.MiddleCenter;
		EditorGUI.LabelField(r, label);
		EditorStyles.label.alignment = alignment;
		return value;
	}

	//static void OnSceneGUI(SceneView sceneview) {
	//	if (!initialized) return;
	//}

	// Called 100 times per second on all visible windows.
	//void Update() {
	//	if (!initialized) return;
	//}

	void OnDestroy() {
		if (!initialized) return;

		// Somewhy OnSceneGUI would still continue to be invoked
		//SceneView.onSceneGUIDelegate -= OnSceneGUI;

		window = null;
	}

	// =========================================================== //

	void Convert_File() {
		using (var pcr = new PointCloudFile.Reader(src_path)) {
			using (var writer = new PointCloudFile.Writer(dst_path, writeBinary, writeCompressed)) {
				Vector3 pos; Color32 color; Vector3 normal;
				while (pcr.Read(out pos, out color, out normal)) {
					writer.Write(pos, color, normal);
				}
			}
		}
	}

	void Convert_Selection() {
		if (!string.IsNullOrEmpty(dst_path)) {
			using (var writer = new PointCloudFile.Writer(dst_path, writeBinary, writeCompressed)) {
				Convert_Selection(writer.Write);
			}
		} else {
			var vertices = new List<Vector3>();
			var colors = new List<Color32>();
			var normals = new List<Vector3>();
			Convert_Selection((pos, color, normal) => {
				vertices.Add(pos); colors.Add(color); normals.Add(normal);
			});

			var indices = new int[vertices.Count];
			for (int i = 0; i < indices.Length; i++) { indices[i] = i; }

			var mesh = new Mesh();
			mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			mesh.SetVertices(vertices);
			mesh.SetColors(colors);
			mesh.SetNormals(normals);
			mesh.SetIndices(indices, MeshTopology.Points, 0, true);

			var gameObject = new GameObject("PointCloud");
			var mesh_filter = gameObject.AddComponent<MeshFilter>();
			mesh_filter.sharedMesh = mesh;
			var mesh_renderer = gameObject.AddComponent<MeshRenderer>();
			mesh_renderer.sharedMaterial = new Material(Shader.Find("Voxel/PointCloudShader"));

			if (localCoords) {
				var tfm = gameObject.transform;
				tfm.SetParent(localCoords.parent);
				tfm.localPosition = localCoords.localPosition;
				tfm.localRotation = localCoords.localRotation;
				tfm.localScale = localCoords.localScale;
			}
		}
	}

	void Convert_Selection(System.Action<Vector3, Color32, Vector3> callback) {
		Transform active_tfm = Selection.activeTransform;
		Voxelizer voxelizer = (active_tfm ? active_tfm.GetComponent<Voxelizer>() : null);
		if (voxelizer) {
			Voxelize_Selection(voxelizer, callback);
		} else {
			var sel_mode = SelectionMode.Unfiltered;
			if (includeChildren) sel_mode = SelectionMode.Deep;
			foreach (var tfm in Selection.GetTransforms(sel_mode)) {
				Sample_Selection(tfm, callback);
			}
		}
	}

	void Voxelize_Selection(Voxelizer voxelizer, System.Action<Vector3, Color32, Vector3> callback) {
		EditorUtility.ClearProgressBar();
		string title = "Voxelizing...", info = "";
		EditorUtility.DisplayCancelableProgressBar(title, info, 0);
		var matrix = voxelizer.transform.localToWorldMatrix;
		if (localCoords) matrix = localCoords.worldToLocalMatrix * matrix;
		bool use_matrix = (voxelizer.transform != localCoords);
		var grid_size = voxelizer.gridSize;
		var bounds = voxelizer.actualBounds;
		int slices_count = voxelizer.slicesCount;
		for (int slice_id = 0; slice_id < slices_count; slice_id++) {
			float progress = slice_id / (float)slices_count;
			if (EditorUtility.DisplayCancelableProgressBar(title, info, progress)) break;
			voxelizer.RenderSlice(slice_id, (p_i, c, n_i) => {
				Vector3 p = p_i, n = n_i;
				p = (p + Vector3.one*0.5f) + n * 0.125f;
				p.x /= grid_size.x; p.y /= grid_size.y; p.z /= grid_size.z;
				p = bounds.min + Vector3.Scale(bounds.size, p);
				if (use_matrix) {
					var p_old = p;
					p = matrix.MultiplyPoint3x4(p);
					n = (matrix.MultiplyPoint3x4(p_old+n) - p).normalized;
				}
				callback(p, c, n);
			});
		}
		voxelizer.Cleanup();
		EditorUtility.ClearProgressBar();
	}

	void Sample_Selection(Transform tfm, System.Action<Vector3, Color32, Vector3> callback) {
		Mesh mesh; Material[] mats;
		GetMeshAndMaterials(tfm, out mesh, out mats);
		if (!mesh || (mats == null)) return;
		var matrix = tfm.localToWorldMatrix;
		if (localCoords) matrix = localCoords.worldToLocalMatrix * matrix;
		var verts = mesh.vertices;
		var normals = VertAttrArrayOrDefault(mesh.normals, verts.Length);
		var uvs = VertAttrArrayOrDefault(mesh.uv, verts.Length);
		var colors = VertAttrArrayOrDefault(mesh.colors32, verts.Length, Color.white);
		for (int vi = 0; vi < verts.Length; vi++) {
			var p = verts[vi];
			verts[vi] = matrix.MultiplyPoint3x4(p);
			normals[vi] = (matrix.MultiplyPoint3x4(p+normals[vi]) - verts[vi]).normalized;
		}
		for (int smi = 0; smi < mesh.subMeshCount; smi++) {
			Material mat = (smi < mats.Length ? mats[smi] : null);
			if (!mat) continue;
			Color cm = mat.color;
			var tex = mat.mainTexture as Texture2D;
			int tw = 0, th = 0;
			Color32[] pixels = null;
			if (tex) {
				try {
					tw = tex.width; th = tex.height;
					pixels = tex.GetPixels32();
				} catch (System.Exception exc) {
					Debug.LogError(exc.ToString());
				}
			}
			var topology = mesh.GetTopology(smi);
			if (topology == MeshTopology.Points) {
				var indices = mesh.GetIndices(smi, true);
				for (int i = 0; i < indices.Length; i++) {
					int i0 = indices[i];
					Vector3 p0 = verts[i0];
					Vector3 n0 = normals[i0];
					Color c0 = colors[i0]*cm;
					Vector2 uv0 = uvs[i0];
					if (pixels != null) {
						c0 *= GetPixel(tex, uv0);
					}
					if (c0.a < 0.95f) continue;
					callback(p0, (Color32)c0, n0);
				}
			} else if ((topology == MeshTopology.Lines) | (topology == MeshTopology.LineStrip)) {
				var indices = mesh.GetIndices(smi, true);
				int icount = indices.Length, di = 2;
				if (topology == MeshTopology.LineStrip) { icount--; di = 1; }
				for (int i = 0; i < icount; i += di) {
					int i0 = indices[i], i1 = indices[i+1];
					Vector3 p0 = verts[i0], p1 = verts[i1];
					Vector3 n0 = normals[i0], n1 = normals[i1];
					Color c0 = colors[i0]*cm, c1 = colors[i1]*cm;
					Vector2 uv0 = uvs[i0], uv1 = uvs[i1];
					SampleLine(spatialResolution, p0, p1, (p, t) => {
						Vector3 nt = Vector3.Lerp(n0, n1, t);
						Color ct = Color.Lerp(c0, c1, t);
						if (pixels != null) {
							ct *= GetPixel(tex, Vector2.Lerp(uv0, uv1, t));
						}
						if (ct.a < 0.95f) return;
						callback(p, (Color32)ct, nt);
					});
				}
			} else { // tris, quads
				var tris = mesh.GetTriangles(smi, true);
				for (int ti = 0; ti < tris.Length; ti += 3) {
					int i0 = tris[ti], i1 = tris[ti+1], i2 = tris[ti+2];
					Vector3 p0 = verts[i0], p1 = verts[i1], p2 = verts[i2];
					Vector3 n0 = normals[i0], n1 = normals[i1], n2 = normals[i2];
					Color c0 = colors[i0]*cm, c1 = colors[i1]*cm, c2 = colors[i2]*cm;
					Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];
					SampleTriangle(spatialResolution, p0, p1, p2, (p, b) => {
						Vector3 nb = (n0*b.x + n1*b.y + n2*b.z);
						Color cb = (c0*b.x + c1*b.y + c2*b.z);
						if (pixels != null) {
							cb *= GetPixel(tex, (uv0*b.x + uv1*b.y + uv2*b.z));
						}
						if (cb.a < 0.95f) return;
						callback(p, (Color32)cb, nb);
					});
				}
			}
		}
	}
	static Color GetPixel(Texture2D tex, Vector2 uv) {
		if (tex.filterMode == FilterMode.Point) {
			return tex.GetPixel((int)(uv.x*tex.width), (int)(uv.y*tex.height));
		} else {
			return tex.GetPixelBilinear(uv.x, uv.y);
		}
	}
	static void GetMeshAndMaterials(Transform tfm, out Mesh mesh, out Material[] mats) {
		mesh = null; mats = null;
		var skin_renderer = tfm.GetComponent<SkinnedMeshRenderer>();
		if (skin_renderer) {
			mesh = new Mesh();
			skin_renderer.BakeMesh(mesh);
			mesh.hideFlags = HideFlags.HideAndDontSave;
			mesh.name = "BakedMesh:Delete";
			mats = skin_renderer.sharedMaterials;
			return;
		}
		var mesh_filter = tfm.GetComponent<MeshFilter>();
		if (mesh_filter) {
			mesh = mesh_filter.sharedMesh;
			var mesh_renderer = tfm.GetComponent<MeshRenderer>();
			if (mesh_renderer) mats = mesh_renderer.sharedMaterials;
			return;
		}
	}
	static T[] VertAttrArrayOrDefault<T>(T[] arr, int L, T default_value=default(T)) {
		if ((arr == null) || (arr.Length < L)) {
			arr = new T[L];
			for (int i = 0; i < arr.Length; i++) arr[i] = default_value;
		}
		return arr;
	}

	static void SampleLine(float s, Vector3 p0, Vector3 p1, System.Action<Vector3, float> callback) {
		int n = Mathf.CeilToInt((p1 - p0).magnitude/s);
		float factor = (n > 0 ? 1f/n : 1f);
		for (int i = 0; i <= n; i++) {
			float t = i * factor;
			callback(Vector3.Lerp(p0, p1, t), t);
		}
	}
	static void SampleTriangle(float s, Vector3 p0, Vector3 p1, Vector3 p2, System.Action<Vector3, Vector3> callback) {
		float m01 = (p1 - p0).magnitude;
		float m12 = (p2 - p1).magnitude;
		float m20 = (p0 - p2).magnitude;
		var b0 = new Vector3(1, 0, 0);
		var b1 = new Vector3(0, 1, 0);
		var b2 = new Vector3(0, 0, 1);
		if ((m01 >= m12) & (m01 >= m20)) {
			SubSampleTriangle(s, p0, p1, p2, b0, b1, b2, callback);
		} else if ((m12 > m01) & (m12 >= m20)) {
			SubSampleTriangle(s, p1, p2, p0, b1, b2, b0, callback);
		} else {
			SubSampleTriangle(s, p2, p0, p1, b2, b0, b1, callback);
		}
	}
	static void SubSampleTriangle(float s, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 b0, Vector3 b1, Vector3 b2, System.Action<Vector3, Vector3> callback) {
		var n01 = (p1 - p0).normalized;
		var proj = p0 + n01*Vector3.Dot(p2 - p0, n01);
		int n = Mathf.CeilToInt((p2 - proj).magnitude/s);
		float factor = (n > 0 ? 1f/n : 1f);
		for (int i = 0; i <= n; i++) {
			float t = i * factor;
			var _p0 = Vector3.Lerp(p0, p2, t);
			var _p1 = Vector3.Lerp(p1, p2, t);
			var _b0 = Vector3.Lerp(b0, b2, t);
			var _b1 = Vector3.Lerp(b1, b2, t);
			SampleLine(s, _p0, _p1, (p, _t) => {
				var b = Vector3.Lerp(_b0, _b1, _t);
				callback(p, b);
			});
		}
	}
}

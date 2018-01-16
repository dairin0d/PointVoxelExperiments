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

public class PointCloudTest : MonoBehaviour {
	public string path;
	public float scale = 1;
	public bool autoscale = true;
	public float viz_amount = 1;

	System.Diagnostics.Stopwatch stopwatch;

	public bool discretize = false;

	public float voxel_size = -1;

	void Start() {
		if (discretize) {
			DiscretizeAndSave();
		} else {
			BuildMesh();
		}
	}

	void BuildMesh() {
		var vertices = new List<Vector3>();
		var colors = new List<Color32>();
		var normals = new List<Vector3>();
		Bounds bounds = new Bounds();
		int full_count = 0;
		using (var pcr = new PointCloudFile.Reader(path)) {
			Vector3 pos; Color32 color; Vector3 normal;
			while (pcr.Read(out pos, out color, out normal)) {
				full_count++;
				if (float.IsNaN(pos.x)|float.IsNaN(pos.y)|float.IsNaN(pos.y)) {
					Debug.Log(vertices.Count+" is NaN");
					continue;
				}
				if (float.IsInfinity(pos.x)|float.IsInfinity(pos.y)|float.IsInfinity(pos.y)) {
					Debug.Log(vertices.Count+" is inf");
					continue;
				}
				if (Random.value > viz_amount) return;
				vertices.Add(pos*scale); colors.Add(color); normals.Add(normal);
				if (vertices.Count == 1) {
					bounds = new Bounds(vertices[0], Vector3.zero);
				} else {
					bounds.Encapsulate(vertices[vertices.Count-1]);
				}
			}
		}
		Debug.Log("Read "+path+" : "+full_count+" -> "+vertices.Count);

		Debug.Log("Bounds = "+bounds);

		if (autoscale) {
			float norm = scale/Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
			for (int i = 0; i < vertices.Count; i++) {
				vertices[i] = (vertices[i] - bounds.center)*norm;
			}
		}

		var indices = new int[vertices.Count];
		for (int i = 0; i < indices.Length; i++) {
			indices[i] = i;
		}

		var mesh = new Mesh();
		mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		mesh.SetVertices(vertices);
		mesh.SetColors(colors);
		mesh.SetNormals(normals);
		mesh.SetIndices(indices, MeshTopology.Points, 0, true);

		var mesh_filter = GetComponent<MeshFilter>();
		mesh_filter.sharedMesh = mesh;
	}

	void DiscretizeAndSave() {
		var discretizer = new PointCloudDiscretizer();

		stopwatch = new System.Diagnostics.Stopwatch();

		stopwatch.Start();
		Bounds bounds = new Bounds();
		using (var pcr = new PointCloudFile.Reader(path)) {
			Vector3 pos; Color32 color; Vector3 normal;
			while (pcr.Read(out pos, out color, out normal)) {
				discretizer.Add(pos, color, normal);
			}
		}
		stopwatch.Stop();
		Debug.Log("File load: "+stopwatch.ElapsedMilliseconds+" ms");

		discretizer.Discretize(voxel_size);

		string dst_path = path.Substring(0, path.Length - System.IO.Path.GetExtension(path).Length);
		dst_path += "_int.csv";
		Debug.Log(dst_path);
		using (var pcw = new PointCloudFile.Writer(dst_path)) {
			foreach (var voxinfo in discretizer.EnumerateVoxels()) {
				pcw.Write((Vector3)voxinfo.pos, voxinfo.color, voxinfo.normal);
			}
		}
	}
}

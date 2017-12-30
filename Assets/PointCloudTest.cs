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

	void Start() {
		var vertices = new List<Vector3>();
		var colors = new List<Color32>();
		var normals = new List<Vector3>();
		Bounds bounds = new Bounds();
		int full_count = 0;
		PointCloudFile.Read(path, (pos,  color, normal) => {
			full_count++;
			if (float.IsNaN(pos.x)|float.IsNaN(pos.y)|float.IsNaN(pos.y)) {
				Debug.Log(vertices.Count+" is NaN");
				return;
			}
			if (float.IsInfinity(pos.x)|float.IsInfinity(pos.y)|float.IsInfinity(pos.y)) {
				Debug.Log(vertices.Count+" is inf");
				return;
			}
			if (Random.value > viz_amount) return;
			vertices.Add(pos*scale); colors.Add(color); normals.Add(normal);
			if (vertices.Count == 1) {
				bounds = new Bounds(vertices[0], Vector3.zero);
			} else {
				bounds.Encapsulate(vertices[vertices.Count-1]);
			}
		});
		Debug.Log("Read "+path+" : "+full_count+" -> "+vertices.Count);

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
		mesh.SetVertices(vertices);
		mesh.SetColors(colors);
		mesh.SetNormals(normals);
		mesh.SetIndices(indices, MeshTopology.Points, 0, true);

		var mesh_filter = GetComponent<MeshFilter>();
		mesh_filter.sharedMesh = mesh;
	}
}

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

public struct PointData {
	public ushort x,y,z;
	public byte r,g,b;
}

public class PointCloudModel : MonoBehaviour {
	public TextAsset data;

	public int iX=0, iY=1, iZ=2, iR=3, iG=4, iB=5;

	public bool shuffle = true;
	public int maxcount = -1;

	PointData[] _points;
	public PointData[] points {
		get {
			if (_points == null) Load();
			return _points;
		}
	}

	Mesh _mesh;
	public Mesh mesh {
		get {
			if (!_mesh) LoadMesh();
			return _mesh;
		}
	}

	static string[] SplitLines(string s, bool remove_empty=false) {
		var split_options = System.StringSplitOptions.None;
		if (remove_empty) split_options = System.StringSplitOptions.RemoveEmptyEntries;
		return s.Split(new string[]{"\r\n", "\n", "\r"}, split_options);
	}

	static void Shuffle<T>(IList<T> list) {
		int n = list.Count;
//		while (n > 1) {
//			n--;
//			int k = Random.Range(0, n+1);
//			T value = list[k];
//			list[k] = list[n];
//			list[n] = value;
//		}
		for (int t = 0; t < n; t++) {
			var tmp = list[t];
			int r = Random.Range(t, n);
			list[t] = list[r];
			list[r] = tmp;
		}
	}

	void Load() {
		if (!data) return;
		var points_list = new List<PointData>();
		var seps = new char[]{',', ' ', '\t'};
		foreach (var line in SplitLines(data.text, true)) {
			if (line.Trim().Length == 0) continue;
			var values = line.Split(seps, System.StringSplitOptions.RemoveEmptyEntries);
			var point = new PointData();
			point.x = ushort.Parse(values[iX].Trim());
			point.y = ushort.Parse(values[iY].Trim());
			point.z = ushort.Parse(values[iZ].Trim());
			point.r = byte.Parse(values[iR].Trim());
			point.g = byte.Parse(values[iG].Trim());
			point.b = byte.Parse(values[iB].Trim());
			points_list.Add(point);
		}
		if (shuffle) Shuffle(points_list);
		_points = points_list.ToArray();
	}

	void LoadMesh() {
		if (!data) return;
		Load();
		var vertices = new List<Vector3>();
		var colors = new List<Color32>();
		var indices = new List<int>();
		foreach (var point in _points) {
			if ((maxcount > 0) && (vertices.Count >= maxcount)) break;
			vertices.Add(new Vector3(point.x, point.y, point.z));
			colors.Add(new Color32(point.r, point.g, point.b, 255));
			indices.Add(indices.Count);
		}
		_mesh = new Mesh();
		_mesh.SetVertices(vertices);
		_mesh.SetColors(colors);
		_mesh.SetIndices(indices.ToArray(), MeshTopology.Points, 0, true);
	}
}

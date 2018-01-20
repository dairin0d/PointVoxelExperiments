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
	public bool save_discretized = false;
	public bool flood_fill = false;
	PointCloudDiscretizer discretizer;

	public float voxel_size = -1;

	public byte alpha_empty=64, alpha_occupied=64;

	Vector3 initial_scale;

	public ColorSpaces color_space = ColorSpaces.GammaRGB;

	public float limitsDY0 = 0;

	string PrintNode<T>(LeafOctree<T> octree, Vector3Int pos, OctreeAccess access) {
		var info = octree.GetInfo(pos, access);
		if (info.node != null) {
			return (pos+" | "+access+": "+info.node.data.ToString()+"; info.pos="+info.pos+", info.level="+info.level);
		} else {
			return (pos+" | "+access+": "+"Node not found!");
		}
	}

	void Start() {
		stopwatch = new System.Diagnostics.Stopwatch();

		initial_scale = transform.localScale;

		if (discretize) {
			DiscretizeAndSave();
			if (flood_fill) {
				var seeds = new List<Vector3Int>();
				var min = discretizer.voxelBounds.min;
				var max = discretizer.voxelBounds.max;
				var mid = Vector3Int.RoundToInt(discretizer.voxelBounds.center);
				seeds.Add(new Vector3Int(mid.x, max.y+1, mid.z));
				var bounds_size = discretizer.voxelBounds.size;
				var limits = discretizer.voxelBounds;
				limits.xMin -= 1;
				limits.yMin -= 1;
				limits.zMin -= 1;
				limits.xMax += 1;
				limits.yMax += 1;
				limits.zMax += 1;
				limits.yMin += (int)(limitsDY0 * bounds_size.y);
				discretizer.FloodFill(seeds, limits);
			}
		}
		BuildMesh();
	}

	void OnDrawGizmos() {
		if (discretizer != null) {
			var prev_matrix = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			discretizer.Draw(alpha_empty, alpha_occupied);
			Gizmos.matrix = prev_matrix;
		}
	}

	void BuildMesh() {
		var vertices = new List<Vector3>();
		var colors = new List<Color32>();
		var normals = new List<Vector3>();
		Bounds bounds = new Bounds();
		int full_count = 0;

		if (discretizer == null) {
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
		} else {
			foreach (var voxel_info in discretizer.EnumerateVoxels(color_space)) {
				var pos = discretizer.VoxelCenter(voxel_info.pos);
				var color = voxel_info.color;
				var normal = voxel_info.normal;
				full_count++;
				vertices.Add(pos); colors.Add(color); normals.Add(normal);
				if (vertices.Count == 1) {
					bounds = new Bounds(vertices[0], Vector3.zero);
				} else {
					bounds.Encapsulate(vertices[vertices.Count-1]);
				}
			}
		}

		Debug.Log("Bounds = "+bounds);

		if (autoscale) {
			Vector3 norm = initial_scale / Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
			transform.position -= Vector3.Scale(bounds.center, norm);
			transform.localScale = norm;
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

	public Color colorA = Color.red;
	public Color colorB = Color.green;
	public int QuadExtents = 64;

	void DiscretizeAndSave() {
		discretizer = new PointCloudDiscretizer();

		stopwatch.Start();
		using (var pcr = new PointCloudFile.Reader(path)) {
			Vector3 pos; Color32 color; Vector3 normal;
			while (pcr.Read(out pos, out color, out normal)) {
				discretizer.Add(pos, color, normal);
			}
		}
		stopwatch.Stop();
		Debug.Log("File load: "+stopwatch.ElapsedMilliseconds+" ms");

//		for (int y = -QuadExtents; y < QuadExtents; y++) {
//			for (int x = -QuadExtents; x < QuadExtents; x++) {
//				var pos = new Vector3(x, y, 0);
//				var color = ((x & 1) != (y & 1) ? colorA : colorB);
//				var normal = Vector3.zero;
//				discretizer.Add(pos, color, normal);
//			}
//		}

//		for (int z = -QuadExtents; z < QuadExtents; z++) {
//			for (int x = -QuadExtents; x < QuadExtents; x++) {
//				var pos = new Vector3(x, x+z, z);
//				var color = ((x & 1) != (z & 1) ? colorA : colorB);
//				var normal = Vector3.zero;
//				discretizer.Add(pos, color, normal);
//			}
//		}
//		for (int z = -QuadExtents; z < QuadExtents; z++) {
//			for (int x = -QuadExtents; x < QuadExtents; x++) {
//				var pos = new Vector3(x, 0, z);
//				var color = ((x & 1) != (z & 1) ? colorA : colorB);
//				var normal = Vector3.zero;
//				discretizer.Add(pos, color, normal);
//			}
//		}

		discretizer.Discretize(voxel_size);

		if (save_discretized) {
			string dst_path = path.Substring(0, path.Length - System.IO.Path.GetExtension(path).Length);
			dst_path += "_int.ply";
			Debug.Log(dst_path);
			using (var pcw = new PointCloudFile.Writer(dst_path)) {
				foreach (var voxinfo in discretizer.EnumerateVoxels(color_space)) {
					pcw.Write((Vector3)voxinfo.pos, voxinfo.color, voxinfo.normal);
				}
			}
		}
	}
}

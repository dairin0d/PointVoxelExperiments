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
using System.Runtime.InteropServices;
using UnityEngine;

public class PointCloudTest : MonoBehaviour {
	public string path;
	public float scale = 1;
	public bool autoscale = true;
	public float viz_amount = 1;

	System.Diagnostics.Stopwatch stopwatch;

	Vector3 initial_scale;

	void Start() {
		stopwatch = new System.Diagnostics.Stopwatch();

		initial_scale = transform.localScale;

		BuildMesh();
	}

	void BuildMesh() {
		if (!File.Exists(path)) { Debug.LogError("File does not exist"); return; }

		string ext = Path.GetExtension(path).ToLowerInvariant();
		string cached_path = (ext == ".cached" ? path : path+".cached");
		if (File.Exists(cached_path)) {
			if (File.GetLastWriteTime(cached_path) >= File.GetLastWriteTime(path)) {
				BuildMesh_Cached(cached_path);
				return;
			}
		}

		BuildMesh_Uncached(cached_path);
	}

	void BuildMesh_Cached(string cached_path) {
		stopwatch.Start();
		var stream = new FileStream(cached_path, FileMode.Open, FileAccess.Read);
		var br = new BinaryReader(stream);
		int vCount = br.ReadInt32();
		var vBytes = br.ReadBytes(vCount*3*4);
		int cCount = br.ReadInt32();
		var cBytes = br.ReadBytes(cCount*4*1);
		int nCount = br.ReadInt32();
		var nBytes = br.ReadBytes(nCount*3*4);
		stream.Close();
		stream.Dispose();
		var vertices = ArrayCaster<Vector3>.Convert(vBytes);
		var colors = ArrayCaster<Color32>.Convert(cBytes);
		var normals = ArrayCaster<Vector3>.Convert(nBytes);
		stopwatch.Stop();
		Debug.Log("Read "+cached_path+" : "+vertices.Length+" ("+stopwatch.ElapsedMilliseconds+" ms)");
		if (colors.Length == 0) colors = null;
		if (normals.Length == 0) normals = null;
		BuildMesh(vertices, colors, normals);
	}

	// http://markheath.net/post/wavebuffer-casting-byte-arrays-to-float
	// https://github.com/naudio/NAudio/blob/master/NAudio/Wave/WaveOutputs/WaveBuffer.cs
	[StructLayout(LayoutKind.Explicit, Pack = 2)]
	struct ArrayCaster<T> where T : struct {
		[FieldOffset(0)] byte[] bytes;
		[FieldOffset(0)] T[] casted;
		public byte[] Bytes { get { return bytes; } }
		public int Length { get { return bytes.Length; } }
		public T[] Casted { get { return casted; } }
		public int Count { get { return bytes.Length / Marshal.SizeOf(default(T)); } }
		public ArrayCaster(byte[] bytes) {
			casted = null;
			this.bytes = bytes;
		}
		public T[] ToArray() {
			var array = new T[Count];
			// Array.Copy() throws error due to mismatching types
			for (int i = 0; i < array.Length; ++i) {
				array[i] = casted[i];
			}
			return array;
		}
		public static implicit operator byte[](ArrayCaster<T> caster) {
			return caster.bytes;
		}
		public static implicit operator T[](ArrayCaster<T> caster) {
			return caster.casted;
		}
		public static T[] Convert(byte[] bytes) {
			return (new ArrayCaster<T>(bytes)).ToArray();
		}
	}

	void BuildMesh_Uncached(string cached_path) {
		var vertices = new List<Vector3>();
		var colors = new List<Color32>();
		var normals = new List<Vector3>();
		int full_count = 0;

		stopwatch.Start();
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
			}
		}
		stopwatch.Stop();
		Debug.Log("Read "+path+" : "+full_count+" -> "+vertices.Count+" ("+stopwatch.ElapsedMilliseconds+" ms)");

		WriteCachedMesh(cached_path, vertices, colors, normals);

		BuildMesh(vertices, colors, normals);
	}

	void WriteCachedMesh(string cached_path, IList<Vector3> vertices, IList<Color32> colors, IList<Vector3> normals) {
		var stream = new FileStream(cached_path, FileMode.Create, FileAccess.Write);
		var bw = new BinaryWriter(stream);
		bw.Write(vertices.Count);
		for (int i = 0; i < vertices.Count; i++) {
			var item = vertices[i];
			bw.Write(item.x); bw.Write(item.y); bw.Write(item.z);
		}
		if (colors != null) {
			bw.Write(colors.Count);
			for (int i = 0; i < colors.Count; i++) {
				var item = colors[i];
				bw.Write(item.r); bw.Write(item.g); bw.Write(item.b); bw.Write(item.a);
			}
		} else {
			bw.Write((int)0);
		}
		if (normals != null) {
			bw.Write(normals.Count);
			for (int i = 0; i < normals.Count; i++) {
				var item = normals[i];
				bw.Write(item.x); bw.Write(item.y); bw.Write(item.z);
			}
		} else {
			bw.Write((int)0);
		}
		bw.Flush();
		stream.Flush();
		stream.Close();
		stream.Dispose();
		Debug.Log("Cached version saved: "+cached_path);
	}

	void BuildMesh(IList<Vector3> vertices, IList<Color32> colors, IList<Vector3> normals) {
		var vA = vertices as Vector3[]; var vL = vertices as List<Vector3>;
		var cA = colors as Color32[]; var cL = colors as List<Color32>;
		var nA = normals as Vector3[]; var nL = normals as List<Vector3>;

		var indices = new int[vertices.Count];
		for (int i = 0; i < indices.Length; i++) {
			indices[i] = i;
		}

		var mesh = new Mesh();
		mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		if (vA != null) { mesh.vertices = vA; } else if (vL != null) { mesh.SetVertices(vL); }
		if (cA != null) { mesh.colors32 = cA; } else if (cL != null) { mesh.SetColors(cL); }
		if (nA != null) { mesh.normals = nA; } else if (nL != null) { mesh.SetNormals(nL); }
		mesh.SetIndices(indices, MeshTopology.Points, 0, true);

		var mesh_filter = GetComponent<MeshFilter>();
		mesh_filter.sharedMesh = mesh;

		var mesh_renderer = GetComponent<MeshRenderer>();
		mesh_renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
		mesh_renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		mesh_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		mesh_renderer.receiveShadows = false;
		mesh_renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

		if (autoscale) {
			var bounds = mesh.bounds;
			Vector3 norm = initial_scale / Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
			transform.position -= Vector3.Scale(bounds.center, norm);
			transform.localScale = norm;
		}
	}
}

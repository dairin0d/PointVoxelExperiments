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

public interface IVoxelCloud<T> : IEnumerable<KeyValuePair<Vector3Int,T>> {
	int Count { get; }

	T this[Vector3Int pos] { get; set; }

	bool Query(Vector3Int pos);
	bool Query(Vector3Int pos, out T data);

	void Erase(Vector3Int pos);
	void Erase();
}

public class HashCloud<T> : IVoxelCloud<T> {
	Dictionary<Vector3Int, T> hashtable;
	T default_value;

	public HashCloud(int capacity=0, T default_value=default(T)) {
		hashtable = new Dictionary<Vector3Int, T>(capacity);
		this.default_value = default_value;
	}

	public int Count {
		get { return hashtable.Count; }
	}

	public IEnumerator<KeyValuePair<Vector3Int,T>> GetEnumerator() {
		return hashtable.GetEnumerator();
	}
	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
		return hashtable.GetEnumerator();
	}

	public T this[Vector3Int pos] {
		get { T data; return (hashtable.TryGetValue(pos, out data) ? data : default_value); }
		set { hashtable[pos] = value; }
	}

	public bool Query(Vector3Int pos) {
		return hashtable.ContainsKey(pos);
	}
	public bool Query(Vector3Int pos, out T data) {
		return hashtable.TryGetValue(pos, out data);
	}

	public void Erase(Vector3Int pos) {
		hashtable.Remove(pos);
	}
	public void Erase() {
		hashtable.Clear();
	}
}

public class OctreeNode<T> {
	// Not using array to avoid extra allocations
	public OctreeNode<T> n000;
	public OctreeNode<T> n001;
	public OctreeNode<T> n010;
	public OctreeNode<T> n011;
	public OctreeNode<T> n100;
	public OctreeNode<T> n101;
	public OctreeNode<T> n110;
	public OctreeNode<T> n111;

	public T data;

	public OctreeNode<T> this[int i] {
		get {
			switch (i) {
			case 0: return n000;
			case 1: return n001;
			case 2: return n010;
			case 3: return n011;
			case 4: return n100;
			case 5: return n101;
			case 6: return n110;
			case 7: return n111;
			default: throw new System.IndexOutOfRangeException("Invalid node index "+i);
			}
		}
		set {
			switch (i) {
			case 0: n000 = value; break;
			case 1: n001 = value; break;
			case 2: n010 = value; break;
			case 3: n011 = value; break;
			case 4: n100 = value; break;
			case 5: n101 = value; break;
			case 6: n110 = value; break;
			case 7: n111 = value; break;
			default: throw new System.IndexOutOfRangeException("Invalid node index "+i);
			}
		}
	}

	public int Count {
		get {
			int n = 0;
			if (n000 != null) ++n;
			if (n001 != null) ++n;
			if (n010 != null) ++n;
			if (n011 != null) ++n;
			if (n100 != null) ++n;
			if (n101 != null) ++n;
			if (n110 != null) ++n;
			if (n111 != null) ++n;
			return n;
		}
	}

	public bool IsEmpty {
		get {
			return (
				(n000 == null) &
				(n001 == null) &
				(n010 == null) &
				(n011 == null) &
				(n100 == null) &
				(n101 == null) &
				(n110 == null) &
				(n111 == null)
			);
		}
	}

	public void Clear() {
		n000 = null;
		n001 = null;
		n010 = null;
		n011 = null;
		n100 = null;
		n101 = null;
		n110 = null;
		n111 = null;
	}

	public struct Info {
		public Vector3Int pos;
		public int level;
		public OctreeNode<T> node;
		public Info(Vector3Int pos, int level, OctreeNode<T> node) {
			this.pos = pos; this.level = level; this.node = node;
		}
	}
}

/// <summary>
/// Leaf octree. Leaves stay fixed size, while the octree can expand
/// to encompass any coordinate within int32 range.
/// </summary>
public class LeafOctree<T> : IVoxelCloud<T> {
	public int Count { get; protected set; }

	public IEnumerator<KeyValuePair<Vector3Int,T>> GetEnumerator() {
		foreach (var node_info in EnumerateNodes(0)) {
			yield return new KeyValuePair<Vector3Int,T>(node_info.pos, node_info.node.data);
		}
	}
	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
		return GetEnumerator();
	}

	public T this[Vector3Int pos] {
		get {
			var node = GetNode(pos, 0);
			return (node != null ? node.data : default(T));
		}
		set {
			var node = GetNode(pos, 1);
			node.data = value;
		}
	}

	public bool Query(Vector3Int pos) {
		return (GetNode(pos, 0) != null);
	}
	public bool Query(Vector3Int pos, out T data) {
		var node = GetNode(pos, 0);
		data = (node != null ? node.data : default(T));
		return node != null;
	}

	public void Erase(Vector3Int pos) {
		RemoveNode(pos);
	}
	public void Erase() {
		Root = null;
		Levels = 0;
		Count = 0;
	}

	public OctreeNode<T> Root;// { get; protected set; }
	public int Levels;// { get; protected set; }
	public int Size {
		get { return 1 << Levels; }
	}

	public LeafOctree() {
		Root = null; Levels = 0; Count = 0;
	}
	public LeafOctree(OctreeNode<T> root, int levels=-1, int count=-1) {
		if (root == null) levels = 0;
		this.Root = root; this.Levels = levels; this.Count = count;
		if ((levels < 0) | (count < 0)) CalcLevels(root, 0);
	}
	void CalcLevels(OctreeNode<T> node, int level) {
		if (level > Levels) {
			Levels = level;
			Count = 1;
		} else if (level == Levels) {
			Count++;
		}
		if (level >= 31) return; // int32 range limits
		for (int iXYZ = 0; iXYZ < 8; iXYZ++) {
			var subnode = node[iXYZ];
			if (subnode != null) CalcLevels(subnode, level+1);
		}
	}

	[System.ThreadStatic]
	static OctreeNode<T>[] node_stack = new OctreeNode<T>[32];
	[System.ThreadStatic]
	static int[] index_stack = new int[32];

	// Modes differ in how they deal with non-existing coordinates:
	// -1 returns the closest intermediate node if leaf node was not reached
	// 0 returns null if the specified leaf node was not found
	// 1 creates new node if it didn't exist (also expands the tree)
	public OctreeNode<T> GetNode(Vector3Int p, int mode=0) {
		int sz2 = Size >> 1;
		while ((p.x < -sz2) | (p.x >= sz2) | (p.y < -sz2) | (p.y >= sz2) | (p.z < -sz2) | (p.z >= sz2)) {
			if (mode <= 0) return null;
			if (Root == null) {
				Root = new OctreeNode<T>();
			} else {
				InitOne(ref Root.n000, 1|2|4);
				InitOne(ref Root.n001, 0|2|4);
				InitOne(ref Root.n010, 1|0|4);
				InitOne(ref Root.n011, 0|0|4);
				InitOne(ref Root.n100, 1|2|0);
				InitOne(ref Root.n101, 0|2|0);
				InitOne(ref Root.n110, 1|0|0);
				InitOne(ref Root.n111, 0|0|0);
			}
			sz2 = 1 << Levels; // Note: sz2 <<= 1 would fail if sz2 was zero
			Levels++;
		}
		var node = Root;
		while (node != null) {
			sz2 >>= 1;
			int iXYZ = 0;
			if (p.x >= 0) { iXYZ |= 1; p.x -= sz2; } else { p.x += sz2; }
			if (p.y >= 0) { iXYZ |= 2; p.y -= sz2; } else { p.y += sz2; }
			if (p.z >= 0) { iXYZ |= 4; p.z -= sz2; } else { p.z += sz2; }
			var subnode = node[iXYZ];
			if (subnode == null) {
				if (mode > 0) {
					subnode = new OctreeNode<T>();
					node[iXYZ] = subnode;
					if (sz2 == 0) ++Count;
				} else if (mode < 0) {
					return node;
				}
			}
			node = subnode;
			if (sz2 == 0) return node;
		}
		return null;
	}
	static void InitOne(ref OctreeNode<T> node, int iXYZ) {
		if (node == null) return;
		var parent = new OctreeNode<T>();
		parent[iXYZ] = node;
		node = parent;
	}

	public void RemoveNode(Vector3Int p) {
		int sz2 = Size >> 1;
		if ((p.x < -sz2) | (p.x >= sz2) | (p.y < -sz2) | (p.y >= sz2) | (p.z < -sz2) | (p.z >= sz2)) return;
		int i_stack = 0;
		var node = Root;
		while (node != null) {
			sz2 >>= 1;
			int iXYZ = 0;
			if (p.x >= 0) { iXYZ |= 1; p.x -= sz2; } else { p.x += sz2; }
			if (p.y >= 0) { iXYZ |= 2; p.y -= sz2; } else { p.y += sz2; }
			if (p.z >= 0) { iXYZ |= 4; p.z -= sz2; } else { p.z += sz2; }
			index_stack[i_stack] = iXYZ;
			node_stack[i_stack++] = node;
			node = node[iXYZ];
			if (sz2 == 0) break;
		}
		--i_stack;
		if ((sz2 == 0) & (node != null)) {
			// leaf node found, remove it and empty parents
			node.Clear();
			--Count;
			while (i_stack >= 0) {
				int iXYZ = index_stack[i_stack];
				if (node_stack[i_stack][iXYZ].IsEmpty) {
					node_stack[i_stack][iXYZ] = null;
				}
				node_stack[i_stack--] = null;
			}
		} else {
			// leaf node not found, clean up the references
			while (i_stack >= 0) {
				node_stack[i_stack--] = null;
			}
		}
	}

	// Modes differ in how they deal with intermediate/empty nodes:
	// -1 enumerates over leaf and non-leaf nodes
	// 0 enumerates only over leaf nodes
	// 1 enumerates over leaf, non-leaf and empty nodes
	public IEnumerable<OctreeNode<T>.Info> EnumerateNodes(int mode=0) {
		if (Root == null) yield break;

		int i_stack = 0;
		var node_stack = new OctreeNode<T>[32];
		var index_stack = new int[32];
		var pos_stack = new Vector3Int[32];

		Vector3Int pos = default(Vector3Int);
		int _sz2 = Size >> 2, sz2 = _sz2;
		var node = Root;
		int iXYZ = 0;
		int level = Levels;
		if (mode != 0) yield return new OctreeNode<T>.Info(pos, level, node);
		--level;
		while (true) {
			while (iXYZ < 8) {
				int _iXYZ = iXYZ;
				var subnode = node[iXYZ++];
				if ((subnode != null) | (mode > 0)) {
					var subpos = pos;
					if (sz2 == 0) {
						if ((_iXYZ & 1) == 0) { subpos.x -= 1; }
						if ((_iXYZ & 2) == 0) { subpos.y -= 1; }
						if ((_iXYZ & 4) == 0) { subpos.z -= 1; }
						yield return new OctreeNode<T>.Info(subpos, level, subnode);
					} else {
						if ((_iXYZ & 1) == 0) { subpos.x -= sz2; } else { subpos.x += sz2; }
						if ((_iXYZ & 2) == 0) { subpos.y -= sz2; } else { subpos.y += sz2; }
						if ((_iXYZ & 4) == 0) { subpos.z -= sz2; } else { subpos.z += sz2; }
						if (mode != 0) yield return new OctreeNode<T>.Info(subpos, level, subnode);
						if (subnode != null) {
							index_stack[i_stack] = iXYZ;
							node_stack[i_stack] = node;
							pos_stack[i_stack] = pos;
							++i_stack;
							sz2 = _sz2 >> i_stack;
							node = subnode;
							pos = subpos;
							iXYZ = 0;
							--level;
						}
					}
				}
			}
			if (i_stack <= 0) break;
			--i_stack;
			sz2 = _sz2 >> i_stack;
			iXYZ = index_stack[i_stack];
			node = node_stack[i_stack];
			pos = pos_stack[i_stack];
			++level;
		}
	}
}

/// <summary>
/// Root octree. Root is considered 100% size, and child nodes
/// can subdivide this unit cube to arbitrary precision.
/// </summary>
//public class RootOctree<T> {
//	public OctreeNode<T> Root { get; protected set; }
//}

public class PointCloudDiscretizer {
	class LinkedPoint {
		public int index;
		public Vector3 pos;
		public Color32 color;
		public Vector3 normal;
		public float dist;
		public LinkedPoint next;

		public LinkedPoint(Vector3 pos, Color32 color, Vector3 normal, int index=0) {
			this.pos = pos; this.color = color; this.normal = normal; this.index = index;
			dist = float.MaxValue;
		}
	}

	struct LinkedPoints {
		public LinkedPoint first, last;
		public int count;

		// Loop condition is by counter because
		// last.next may intentionally be not null
		// (e.g. if this is a part of a larger list)
		public IEnumerable<LinkedPoint> Enumerate() {
			var p = first;
			for (int i = 0; i < count; i++) {
				var next_p = p.next;
				yield return p; // p.next may change outside
				p = next_p;
			}
		}
	}

	Bounds bounds = default(Bounds);
	OctreeNode<LinkedPoints> root = new OctreeNode<LinkedPoints>();

	public float voxelSize { get; private set; }
	public Vector3 offset { get; private set; }
	LeafOctree<LinkedPoints> octree = null;

	public void Add(Vector3 pos, Color32 color, Vector3 normal) {
		if (root == null) throw new System.InvalidOperationException("Cannot add points after discretization");

		AddLPoint(root, new LinkedPoint(pos, color, normal, root.data.count));
		if (root.data.count == 1) {
			bounds = new Bounds(pos, Vector3.zero);
		} else {
			bounds.Encapsulate(pos);
		}
	}

	public void Discretize(float voxel_size=-1, float D_stop=1) {
		if (root == null) throw new System.InvalidOperationException("Model is already discretized");

		voxelSize = (voxel_size <= float.Epsilon ? EstimateVoxelSize(root, bounds, D_stop) : voxel_size);

		int nx = Mathf.RoundToInt(bounds.size.x / voxelSize) + 1;
		int ny = Mathf.RoundToInt(bounds.size.y / voxelSize) + 1;
		int nz = Mathf.RoundToInt(bounds.size.z / voxelSize) + 1;
		offset = bounds.center - (new Vector3(nx % 2, ny % 2, nz % 2))*(voxelSize*0.5f);

		float scale = 1f/voxelSize;
		octree = new LeafOctree<LinkedPoints>();
		foreach (var p in root.data.Enumerate()) {
			var ipos = Vector3Int.FloorToInt((p.pos - offset) * scale);
			AddLPoint(octree.GetNode(ipos, 1), p);
		}

		root = null; // all points are sorted into octree now
	}

	public Vector3 VoxelCenter(Vector3Int pos) {
		return offset + (new Vector3(pos.x+0.5f, pos.y+0.5f, pos.y+0.5f))*voxelSize;
	}

	public struct VoxelInfo {
		public Vector3Int pos;
		public Color32 color;
		public Vector3 normal;
	}
	public IEnumerable<VoxelInfo> EnumerateVoxels() {
		if (octree == null) yield break;
		var voxinfo = default(VoxelInfo);
		foreach (var node_info in octree.EnumerateNodes()) {
			var p = node_info.node.data.first;
			voxinfo.pos = node_info.pos;
			voxinfo.color = p.color;
			voxinfo.normal = p.normal;
			yield return voxinfo;
		}
	}

	// Can't move this method to LPoints (since it's a struct)
	static void AddLPoint(OctreeNode<LinkedPoints> node, LinkedPoint p) {
		if (node.data.last == null) {
			node.data.last = p;
		}
		p.next = node.data.first;
		node.data.first = p;
		node.data.count++;
	}

	#region Voxel size estimation
	static float EstimateVoxelSize(OctreeNode<LinkedPoints> root, Bounds bounds, float D_stop=1) {
		// We cannot use number of points in node as a stopping criterion,
		// since it can't handle the case of coniciding points.
		// However, we can use fractal dimension.
		int levels = SubdivideFractalOctree(root, bounds, D_stop);

		float max_bounds = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
		float min_dist = max_bounds / (1 << levels);

		// Remove 2 lowest levels to make sure that node size is bigger
		// than point cloud's spatial resolution
		if (levels > 0) { Unsibdivide(root); levels--; }
		if (levels > 0) { Unsibdivide(root); levels--; }

		var octree = new LeafOctree<LinkedPoints>(root);
		FindNeighbors(octree, ManhattanMetric, min_dist);

		float d_min = float.MaxValue, d_max = 0;
		foreach (var p in root.data.Enumerate()) {
			if (p.dist < d_min) d_min = p.dist;
			if (p.dist > d_max) d_max = p.dist;
		}

		root.Clear(); // clean up

		return Mathf.Min(d_min*2, d_max);
	}

	static void FindNeighbors(LeafOctree<LinkedPoints> octree, System.Func<Vector3, float> metric, float min_dist=0) {
		foreach (var node_info in octree.EnumerateNodes()) {
			var node0 = node_info.node;
			foreach (var p0 in node0.data.Enumerate()) {
				for (int dz = -1; dz <= 1; dz++) {
					for (int dy = -1; dy <= 1; dy++) {
						for (int dx = -1; dx <= 1; dx++) {
							var node1 = node0;
							if ((dx != 0) | (dy != 0) | (dz != 0)) {
								var pos1 = node_info.pos; // neighbor position
								pos1.x += dx; pos1.y += dy; pos1.z += dz;
								node1 = octree.GetNode(pos1);
								if (node1 == null) continue;
							}
							foreach (var p1 in node1.data.Enumerate()) {
								float dist = metric(p1.pos - p0.pos);
								if (dist > min_dist) {
									if (dist < p0.dist) p0.dist = dist;
									if (dist < p1.dist) p1.dist = dist;
								}
							}
						}
					}
				}
			}
		}
	}
	static float ManhattanMetric(Vector3 diff) {
		if (diff.x < 0) diff.x = -diff.x;
		if (diff.y < 0) diff.y = -diff.y;
		if (diff.z < 0) diff.z = -diff.z;
		float dist = diff.x;
		if (diff.y > dist) dist = diff.y;
		if (diff.z > dist) dist = diff.z;
		return dist;
	}

	static void Unsibdivide(OctreeNode<LinkedPoints> node) {
		for (int iXYZ = 0; iXYZ < 8; iXYZ++) {
			var subnode = node[iXYZ];
			if (subnode == null) continue;
			if (subnode.IsEmpty) {
				node[iXYZ] = null;
			} else {
				Unsibdivide(subnode);
			}
		}
	}

	static int SubdivideFractalOctree(OctreeNode<LinkedPoints> root, Bounds bounds, float D_stop=1, int max_levels=31) {
		float max_bounds = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
		float max_extents = max_bounds*0.5f;

		int _leaf_count = 1;
		float _leaf_size = max_extents;
		for (int level = 1; level <= max_levels; level++) {
			int leaf_count = 0;
			float leaf_size = max_extents;
			Subdivide(root, bounds.center, max_extents, ref leaf_count, ref leaf_size);
			float D = FractalDimension(_leaf_size, _leaf_count, leaf_size, leaf_count);
			if (D < D_stop) return level;
			_leaf_count = leaf_count;
			_leaf_size = leaf_size;
		}

		return max_levels;
	}
	static float FractalDimension(float s0, float n0, float s1, float n1) {
		return Mathf.Abs(Mathf.Log(n1/n0) / Mathf.Log(s1/s0));
	}

	static void Subdivide(OctreeNode<LinkedPoints> node, Vector3 center, float extents, ref int leaf_count, ref float leaf_size) {
		if (node.IsEmpty) {
			float sz2 = extents * 0.5f;
			if (sz2 < leaf_size) leaf_size = sz2;
			// Sort into subnodes
			foreach (var p in node.data.Enumerate()) {
				int iXYZ = 0;
				if (p.pos.x >= center.x) { iXYZ |= 1; }
				if (p.pos.y >= center.y) { iXYZ |= 2; }
				if (p.pos.z >= center.z) { iXYZ |= 4; }
				var subnode = node[iXYZ];
				if (subnode == null) {
					subnode = new OctreeNode<LinkedPoints>();
					node[iXYZ] = subnode;
					leaf_count++;
				}
				AddLPoint(subnode, p);
			}
		} else {
			float sz2 = extents * 0.5f;
			for (int iXYZ = 0; iXYZ < 8; iXYZ++) {
				var subnode = node[iXYZ];
				if (subnode == null) continue;
				Vector3 c = center;
				if ((iXYZ & 1) == 0) { c.x -= sz2; } else { c.x += sz2; }
				if ((iXYZ & 2) == 0) { c.y -= sz2; } else { c.y += sz2; }
				if ((iXYZ & 4) == 0) { c.z -= sz2; } else { c.z += sz2; }
				Subdivide(subnode, c, sz2, ref leaf_count, ref leaf_size);
			}
		}

		// Restore links...
		node.data.first = null;
		node.data.last = null;
		for (int iXYZ = 0; iXYZ < 8; iXYZ++) {
			var subnode = node[iXYZ];
			if (subnode == null) continue;
			if (node.data.first == null) {
				node.data.first = subnode.data.first;
			} else {
				node.data.last.next = subnode.data.first;
			}
			node.data.last = subnode.data.last;
		}
		if (node.data.last != null) {
			node.data.last.next = null; // for cleanness
		}
	}
	#endregion
}

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
#if UNITY_EDITOR
using UnityEditor;
#endif

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

	public byte Mask {
		get {
			byte mask = 0;
			if (n000 != null) mask |= 1;
			if (n001 != null) mask |= 2;
			if (n010 != null) mask |= 4;
			if (n011 != null) mask |= 8;
			if (n100 != null) mask |= 16;
			if (n101 != null) mask |= 32;
			if (n110 != null) mask |= 64;
			if (n111 != null) mask |= 128;
			return mask;
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

public enum OctreeAccess {
	LeafOnly=0,
	AnyLevel=1,
	AutoInit=2,
	EmptyToo=3,
}

/// <summary>
/// Leaf octree. Leaves stay fixed size, while the octree can expand
/// to encompass any coordinate within int32 range.
/// </summary>
public class LeafOctree<T> : IVoxelCloud<T> {
	public int Count { get; protected set; }

	public IEnumerator<KeyValuePair<Vector3Int,T>> GetEnumerator() {
		foreach (var node_info in EnumerateNodes()) {
			yield return new KeyValuePair<Vector3Int,T>(node_info.pos, node_info.node.data);
		}
	}
	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
		return GetEnumerator();
	}

	public T this[Vector3Int pos] {
		get {
			var node = GetNode(pos);
			return (node != null ? node.data : default(T));
		}
		set {
			var node = GetNode(pos, OctreeAccess.AutoInit);
			node.data = value;
		}
	}

	public bool Query(Vector3Int pos) {
		return (GetNode(pos) != null);
	}
	public bool Query(Vector3Int pos, out T data) {
		var node = GetNode(pos);
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

	public bool InRange(Vector3Int p) {
		int sz2 = Size >> 1;
		return (p.x >= -sz2) & (p.x < sz2) & (p.y >= -sz2) & (p.y < sz2) & (p.z >= -sz2) & (p.z < sz2);
	}

	public void Encapsulate(Vector3Int p) {
		while (!InRange(p)) {
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
			Levels++;
		}
	}
	static void InitOne(ref OctreeNode<T> node, int iXYZ) {
		if (node == null) return;
		var parent = new OctreeNode<T>();
		parent[iXYZ] = node;
		node = parent;
	}

	public OctreeNode<T> GetNode(Vector3Int p, OctreeAccess access=OctreeAccess.LeafOnly) {
		return GetInfo(p, access).node;
	}

	public OctreeNode<T>.Info GetInfo(Vector3Int p, OctreeAccess access=OctreeAccess.LeafOnly) {
		bool any_level = ((access & OctreeAccess.AnyLevel) != 0);
		bool auto_init = ((access & OctreeAccess.AutoInit) != 0);

		if (auto_init) { Encapsulate(p); } else if (!InRange(p)) { return default(OctreeNode<T>.Info); }

		var d = p; // residual / offset from node origin
		int sz2 = Size >> 1;
		int level = Levels;
		var node = Root;
		while (node != null) {
			var d0 = d;
			sz2 >>= 1;
			--level;
			int iXYZ = 0;
			if (sz2 == 0) {
				if (d.x >= 0) { iXYZ |= 1; } else { d.x += 1; }
				if (d.y >= 0) { iXYZ |= 2; } else { d.y += 1; }
				if (d.z >= 0) { iXYZ |= 4; } else { d.z += 1; }
			} else {
				if (d.x >= 0) { iXYZ |= 1; d.x -= sz2; } else { d.x += sz2; }
				if (d.y >= 0) { iXYZ |= 2; d.y -= sz2; } else { d.y += sz2; }
				if (d.z >= 0) { iXYZ |= 4; d.z -= sz2; } else { d.z += sz2; }
			}
			var subnode = node[iXYZ];
			if (subnode == null) {
				if (any_level & (!auto_init || node.IsEmpty)) {
					return new OctreeNode<T>.Info(p-d0, level+1, node);
				}
				if (auto_init) {
					subnode = new OctreeNode<T>();
					node[iXYZ] = subnode;
					if (sz2 == 0) ++Count;
				}
			}
			node = subnode;
			if (sz2 == 0) return new OctreeNode<T>.Info(p-d, level, node);
		}
		return default(OctreeNode<T>.Info);
	}

	public void RemoveNode(Vector3Int p, OctreeAccess access=OctreeAccess.LeafOnly) {
		bool any_level = ((access & OctreeAccess.AnyLevel) != 0);

		if (!InRange(p)) return;

		int sz2 = Size >> 1;
		int i_stack = 0;
		var node = Root;
		while (node != null) {
			sz2 >>= 1;
			int iXYZ = 0;
			if (p.x >= 0) { iXYZ |= 1; p.x -= sz2; } else { p.x += sz2; }
			if (p.y >= 0) { iXYZ |= 2; p.y -= sz2; } else { p.y += sz2; }
			if (p.z >= 0) { iXYZ |= 4; p.z -= sz2; } else { p.z += sz2; }
			var subnode = node[iXYZ];
			if ((subnode == null) & any_level) break;
			index_stack[i_stack] = iXYZ;
			node_stack[i_stack++] = node;
			node = subnode;
			if (sz2 == 0) break;
		}
		--i_stack;

		if (node != null) {
			if (sz2 == 0) {
				node.Clear();
				--Count;
			}
			while (i_stack >= 0) {
				int iXYZ = index_stack[i_stack];
				if (node_stack[i_stack][iXYZ].IsEmpty) {
					node_stack[i_stack][iXYZ] = null;
				}
				node_stack[i_stack--] = null; // clean up static references
			}
		} else {
			while (i_stack >= 0) {
				node_stack[i_stack--] = null; // clean up static references
			}
		}
	}

	public IEnumerable<OctreeNode<T>.Info> EnumerateNodes(OctreeAccess access=OctreeAccess.LeafOnly) {
		bool any_level = ((access & OctreeAccess.AnyLevel) != 0);
		bool empty_too = ((access & OctreeAccess.AutoInit) != 0);

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
		if (empty_too|any_level) yield return new OctreeNode<T>.Info(pos, level, node);
		--level;
		while (true) {
			while (iXYZ < 8) {
				int _iXYZ = iXYZ;
				var subnode = node[iXYZ++];
				if ((subnode != null) | empty_too) {
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
						if (empty_too|any_level) yield return new OctreeNode<T>.Info(subpos, level, subnode);
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
		public Vector3 pos;
		public Color32 color;
		public Vector3 normal;
		public float dist;
		public LinkedPoint next;

		public LinkedPoint(Vector3 pos, Color32 color, Vector3 normal) {
			this.pos = pos; this.color = color; this.normal = normal;
			dist = float.MaxValue;
		}
	}

	struct LinkedPoints {
		public LinkedPoint first, last;
		public int count;

		public int flags;

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

	const int FLAG_XN = (1 << 8);
	const int FLAG_YN = (1 << 9);
	const int FLAG_ZN = (1 << 10);
	const int FLAG_XP = (1 << 11);
	const int FLAG_YP = (1 << 12);
	const int FLAG_ZP = (1 << 13);
	const int FLAG_ALLDIR = FLAG_XN|FLAG_YN|FLAG_ZN|FLAG_XP|FLAG_YP|FLAG_ZP;

	OctreeNode<LinkedPoints> root = new OctreeNode<LinkedPoints>();
	public Bounds pointBounds { get; private set; }
	public int pointCount {
		get { return root.data.count; }
	}

	LeafOctree<LinkedPoints> octree = null;
	public float voxelSize { get; private set; }
	public Vector3 voxelOffset { get; private set; }
	public BoundsInt voxelBounds { get; private set; }
	public int voxelCount {
		get { return (octree != null ? octree.Count : 0); }
	}

	public string task { get; private set; }
	public float progress { get; private set; }
	bool cancelled = false;
	public bool halt {
		get { return cancelled; }
		set { cancelled |= value; }
	}

	public void Add(Vector3 pos, Color32 color, Vector3 normal) {
		if (halt) return;

		var p = new LinkedPoint(pos, color, normal);
		if (octree != null) {
			AddVoxelPoint(p);
			root.data.count++;
		} else {
			AddLinkedPoint(root, p);
		}

		if (root.data.count == 1) {
			pointBounds = new Bounds(pos, Vector3.zero);
		} else {
			var bounds = pointBounds;
			bounds.Encapsulate(pos);
			pointBounds = bounds;
		}
	}

	public void Discretize(float voxel_size=-1, float D_stop=1) {
		if (halt) return;

		if (root.data.count == 0) return;

		if (octree != null) {
			root.data.count = 0;
			foreach (var node_info in octree.EnumerateNodes()) {
				foreach (var p in node_info.node.data.Enumerate()) {
					AddLinkedPoint(root, p);
				}
			}
			octree = null;
		}

		voxelSize = (voxel_size <= float.Epsilon ? EstimateVoxelSize(root, pointBounds, D_stop) : voxel_size);
		if (halt) return;

		int nx = Mathf.RoundToInt(pointBounds.size.x / voxelSize) + 1;
		int ny = Mathf.RoundToInt(pointBounds.size.y / voxelSize) + 1;
		int nz = Mathf.RoundToInt(pointBounds.size.z / voxelSize) + 1;
		voxelOffset = pointBounds.center - (new Vector3(nx % 2, ny % 2, nz % 2))*(voxelSize*0.5f);

		octree = new LeafOctree<LinkedPoints>();
		task = "Discretizing"; progress = 0; int counter = 0;
		foreach (var p in root.data.Enumerate()) {
			AddVoxelPoint(p);
			progress = (++counter) / (float)root.data.count;
			if (halt) return;
		}
		task = null;

		// All points are sorted into octree now
		root.data.first = null;
		root.data.last = null;
	}

	public Vector3Int VoxelGridPos(Vector3 pos) {
		if (voxelSize <= float.Epsilon) return Vector3Int.zero;
		return Vector3Int.FloorToInt((pos - voxelOffset) / voxelSize);
	}
	public Vector3 VoxelCenter(Vector3Int pos) {
		return voxelOffset + (new Vector3(pos.x+0.5f, pos.y+0.5f, pos.z+0.5f))*voxelSize;
	}

	void AddVoxelPoint(LinkedPoint p) {
		var grid_pos = VoxelGridPos(p.pos);
		if (octree.Count == 0) {
			voxelBounds = new BoundsInt(grid_pos, Vector3Int.zero);
		} else {
			var b_min = Vector3Int.Min(voxelBounds.min, grid_pos);
			var b_max = Vector3Int.Max(voxelBounds.max, grid_pos);
			voxelBounds = new BoundsInt(b_min, b_max-b_min);
		}
		var node = octree.GetNode(grid_pos, OctreeAccess.AutoInit);
		node.data.flags |= FLAG_ALLDIR;
		AddLinkedPoint(node, p);
	}

	static void AddLinkedPoint(OctreeNode<LinkedPoints> node, LinkedPoint p) {
		if (node.data.last == null) {
			node.data.last = p;
		}
		p.next = node.data.first;
		node.data.first = p;
		node.data.count++;
	}

	#region Flood fill
	public void FloodFill() {
		if (halt) return;
		FloodFill(BorderCoords(voxelBounds));
	}
	public void FloodFill(IEnumerable<Vector3Int> seeds) {
		if (halt) return;
		var limits = voxelBounds;
		limits.min -= Vector3Int.one;
		limits.max += Vector3Int.one;
		FloodFill(seeds, limits);
	}
	public void FloodFill(BoundsInt limits) {
		if (halt) return;
		FloodFill(BorderCoords(limits, 0), limits);
	}
	public void FloodFill(IEnumerable<Vector3Int> seeds, BoundsInt limits) {
		if (halt) return;

		// Make sure limits are inside the octree, then reset accessibility information
		octree.Encapsulate(limits.min);
		octree.Encapsulate(limits.max);
		foreach (var node_info in octree.EnumerateNodes(OctreeAccess.AnyLevel)) {
			node_info.node.data.flags = node_info.node.Mask;
		}

		var wavefront = new Queue<Vector3Int>();
		foreach (var seed in seeds) {
			AddSeed(wavefront, seed, Vector3Int.zero, limits);
		}

		task = "Flood filling"; progress = 0; int max_count = 1;
		while (wavefront.Count != 0) {
			var seed = wavefront.Dequeue();
			var node_info = octree.GetInfo(seed, OctreeAccess.AnyLevel);
			int size = 1 << node_info.level;
			int sz2 = size >> 1;
			var d = seed - node_info.pos;
			var min = node_info.pos;
			var max = node_info.pos;
			if (sz2 > 0) {
				max -= Vector3Int.one;
				if (d.x >= 0) { max.x += sz2; } else { min.x -= sz2; }
				if (d.y >= 0) { max.y += sz2; } else { min.y -= sz2; }
				if (d.z >= 0) { max.z += sz2; } else { min.z -= sz2; }
			}
			foreach (var seed2 in BorderCoords(min, max)) {
				var delta = Vector3Int.zero;
				if (seed2.x < min.x) {
					delta.x = 1;
				} else if (seed2.x > max.x) {
					delta.x = -1;
				} else if (seed2.y < min.y) {
					delta.y = 1;
				} else if (seed2.y > max.y) {
					delta.y = -1;
				} else if (seed2.z < min.z) {
					delta.z = 1;
				} else if (seed2.z > max.z) {
					delta.z = -1;
				}
				AddSeed(wavefront, seed2, delta, limits);
			}
			max_count = Mathf.Max(max_count, wavefront.Count);
			progress = 1f - (wavefront.Count / (float)max_count);
			if (halt) return;
		}
		task = null;
	}

	void AddSeed(Queue<Vector3Int> wavefront, Vector3Int seed, Vector3Int delta, BoundsInt limits) {
		if (seed.x < limits.min.x) return;
		if (seed.x > limits.max.x) return;
		if (seed.y < limits.min.y) return;
		if (seed.y > limits.max.y) return;
		if (seed.z < limits.min.z) return;
		if (seed.z > limits.max.z) return;

		var node_info = octree.GetInfo(seed, OctreeAccess.AnyLevel);
		if (node_info.level == 0) {
			node_info.node.data.flags |= DirectionFlag(delta);
		} else {
			var d = seed - node_info.pos;
			int iXYZ = 0;
			if (d.x >= 0) { iXYZ |= 1; }
			if (d.y >= 0) { iXYZ |= 2; }
			if (d.z >= 0) { iXYZ |= 4; }
			int mask = (1 << iXYZ);
			if ((node_info.node.data.flags & mask) == 0) {
				node_info.node.data.flags |= mask;
				wavefront.Enqueue(seed);
			}
		}
	}

	static int DirectionFlag(Vector3 delta) {
		if (delta == Vector3.zero) return FLAG_ALLDIR;
		var delta_abs = delta;
		delta_abs.x = Mathf.Abs(delta.x);
		delta_abs.y = Mathf.Abs(delta.y);
		delta_abs.z = Mathf.Abs(delta.z);
		if (delta_abs.x >= delta_abs.y) {
			if (delta_abs.x >= delta_abs.z) {
				return (delta.x < 0 ? FLAG_XN : FLAG_XP);
			} else {
				return (delta.z < 0 ? FLAG_ZN : FLAG_ZP);
			}
		} else {
			if (delta_abs.y >= delta_abs.z) {
				return (delta.y < 0 ? FLAG_YN : FLAG_YP);
			} else {
				return (delta.z < 0 ? FLAG_ZN : FLAG_ZP);
			}
		}
	}

	public static IEnumerable<Vector3Int> BorderCoords(BoundsInt limits, int delta=1) {
		return BorderCoords(limits.min, limits.max, delta);
	}
	public static IEnumerable<Vector3Int> BorderCoords(BoundsInt limits, Vector3Int delta) {
		return BorderCoords(limits.min, limits.max, delta);
	}
	public static IEnumerable<Vector3Int> BorderCoords(BoundsInt limits, Vector3Int dmin, Vector3Int dmax) {
		return BorderCoords(limits.min, limits.max, dmin, dmax);
	}
	public static IEnumerable<Vector3Int> BorderCoords(Vector3Int min, Vector3Int max, int delta=1) {
		var delta3 = new Vector3Int(delta, delta, delta);
		return BorderCoords(min, max, delta3, delta3);
	}
	public static IEnumerable<Vector3Int> BorderCoords(Vector3Int min, Vector3Int max, Vector3Int delta) {
		return BorderCoords(min, max, delta, delta);
	}
	public static IEnumerable<Vector3Int> BorderCoords(Vector3Int min, Vector3Int max, Vector3Int dmin, Vector3Int dmax) {
		if (min == max) {
			if ((dmin == Vector3Int.zero) & (dmax == Vector3Int.zero)) {
				yield return new Vector3Int(min.x, min.y, min.z);
			} else {
				yield return new Vector3Int(min.x-dmin.x, min.y, min.z);
				yield return new Vector3Int(min.x+dmax.x, min.y, min.z);
				yield return new Vector3Int(min.x, min.y-dmin.y, min.z);
				yield return new Vector3Int(min.x, min.y+dmax.y, min.z);
				yield return new Vector3Int(min.x, min.y, min.z-dmin.z);
				yield return new Vector3Int(min.x, min.y, min.z+dmax.z);
			}
		} else {
			for (int x = min.x; x <= max.x; x++) {
				for (int y = min.y; y <= max.y; y++) {
					yield return new Vector3Int(x, y, min.z-dmin.z);
					yield return new Vector3Int(x, y, max.z+dmax.z);
				}
			}
			for (int x = min.x; x <= max.x; x++) {
				for (int z = min.z; z <= max.z; z++) {
					yield return new Vector3Int(x, min.y-dmin.y, z);
					yield return new Vector3Int(x, max.y+dmax.y, z);
				}
			}
			for (int y = min.y; y <= max.y; y++) {
				for (int z = min.z; z <= max.z; z++) {
					yield return new Vector3Int(min.x-dmin.x, y, z);
					yield return new Vector3Int(max.x+dmax.x, y, z);
				}
			}
		}
	}
	#endregion

	#if UNITY_EDITOR
	public void Draw(byte alpha_empty=64, byte alpha_occupied=64) {
		if ((alpha_empty == 0) & (alpha_occupied == 0)) return;
		foreach (var node_info in octree.EnumerateNodes(OctreeAccess.AnyLevel)) {
			Vector3 size = Vector3.one * (1 << node_info.level);
			Vector3 pos = node_info.pos;
			if (node_info.level == 0) pos += Vector3.one * 0.5f;
			size *= voxelSize;
			pos *= voxelSize;
			pos += voxelOffset;
			if (node_info.level == 0) {
				var flags = node_info.node.data.flags;
				if ((flags & FLAG_ALLDIR) != 0) {
					if (alpha_occupied > 0) {
						byte r = (byte)((flags & (FLAG_XN|FLAG_XP)) != 0 ? 255 : 0);
						byte g = (byte)((flags & (FLAG_YN|FLAG_YP)) != 0 ? 255 : 0);
						byte b = (byte)((flags & (FLAG_ZN|FLAG_ZP)) != 0 ? 255 : 0);
						Gizmos.color = new Color32(r, g, b, alpha_occupied);
						Gizmos.DrawCube(pos, size);
					}
				}
			} else if (alpha_empty > 0) {
				Gizmos.color = new Color32(255, 255, 255, alpha_empty);
				DrawVirtualSubnodes(node_info.node, pos, size*0.5f);
			}
		}
	}
	void DrawVirtualSubnodes(OctreeNode<LinkedPoints> node, Vector3 pos, Vector3 size) {
		var size2 = size*0.5f;
		var mask = node.data.flags & 255;
		for (int iXYZ = 0; iXYZ < 8; iXYZ++) {
			if (((mask >> iXYZ) & 1) == 0) continue;
			var subpos = pos;
			if ((iXYZ & 1) == 0) { subpos.x -= size2.x; } else { subpos.x += size2.x; }
			if ((iXYZ & 2) == 0) { subpos.y -= size2.y; } else { subpos.y += size2.y; }
			if ((iXYZ & 4) == 0) { subpos.z -= size2.z; } else { subpos.z += size2.z; }
			if (node[iXYZ] == null) Gizmos.DrawWireCube(subpos, size);
		}
	}
	#endif

	#region Enumerate voxels
	public struct VoxelInfo {
		public Vector3Int pos;
		public Color32 color;
		public Vector3 normal;
	}
	public IEnumerable<VoxelInfo> EnumerateVoxels(ColorSpaces color_space=ColorSpaces.GammaRGB) {
		if (halt) yield break;
		if (octree == null) yield break;
		var voxel_info = default(VoxelInfo);
		foreach (var node_info in octree.EnumerateNodes()) {
			if (!AggregatePoints(node_info.node, ref voxel_info, color_space)) continue;
			voxel_info.pos = node_info.pos;
			yield return voxel_info;
		}
	}
	static bool AggregatePoints(OctreeNode<LinkedPoints> node, ref VoxelInfo voxel_info, ColorSpaces color_space) {
		var flags = node.data.flags;
		if ((flags & FLAG_ALLDIR) == 0) return false;
		var normal = Vector3.zero;
		var color = Color.clear;
		int count = 0;
		//foreach (var p in node.data.Enumerate()) {
		//	AggregatePoint(p, ref normal, ref color, ref count, color_space);
		//}
		LinkedPoint px0 = node.data.first;
		LinkedPoint py0 = node.data.first;
		LinkedPoint pz0 = node.data.first;
		LinkedPoint px1 = node.data.first;
		LinkedPoint py1 = node.data.first;
		LinkedPoint pz1 = node.data.first;
		foreach (var p in node.data.Enumerate()) {
			if (p.pos.x < px0.pos.x) px0 = p;
			if (p.pos.y < py0.pos.y) py0 = p;
			if (p.pos.z < pz0.pos.z) pz0 = p;
			if (p.pos.x > px1.pos.x) px1 = p;
			if (p.pos.y > py1.pos.y) py1 = p;
			if (p.pos.z > pz1.pos.z) pz1 = p;
		}
		if ((flags & FLAG_XN) != 0) { AggregatePoint(px0, ref normal, ref color, ref count, color_space); }
		if ((flags & FLAG_YN) != 0) { AggregatePoint(py0, ref normal, ref color, ref count, color_space); }
		if ((flags & FLAG_ZN) != 0) { AggregatePoint(pz0, ref normal, ref color, ref count, color_space); }
		if ((flags & FLAG_XP) != 0) { AggregatePoint(px1, ref normal, ref color, ref count, color_space); }
		if ((flags & FLAG_YP) != 0) { AggregatePoint(py1, ref normal, ref color, ref count, color_space); }
		if ((flags & FLAG_ZP) != 0) { AggregatePoint(pz1, ref normal, ref color, ref count, color_space); }
		normal = (normal / count).normalized;
		color = FromColorSpace(color / count, color_space);
		voxel_info.normal = normal;
		voxel_info.color = color;
		return true;
	}
	static void AggregatePoint(LinkedPoint p, ref Vector3 normal, ref Color color, ref int count, ColorSpaces color_space) {
		normal += p.normal; color += ToColorSpace(p.color, color_space); count++;
	}
	static Color ToColorSpace(Color color, ColorSpaces color_space) {
		if (color_space != ColorSpaces.GammaRGB) {
			color = ColorUtils.GammaRGB_to_LinearRGB(color);
			if (color_space != ColorSpaces.LinearRGB) {
				color = ColorUtils.LinearRGB_to_XYZ(color);
				if (color_space == ColorSpaces.CIE_Lab) {
					color = ColorUtils.XYZ_to_Lab(color);
				}
			}
		}
		return color;
	}
	static Color FromColorSpace(Color color, ColorSpaces color_space) {
		if (color_space != ColorSpaces.GammaRGB) {
			if (color_space != ColorSpaces.LinearRGB) {
				if (color_space == ColorSpaces.CIE_Lab) {
					color = ColorUtils.Lab_to_XYZ(color);
				}
				color = ColorUtils.XYZ_to_LinearRGB(color);
			}
			color = ColorUtils.LinearRGB_to_GammaRGB(color);
		}
		return color;
	}
	#endregion

	#region Enumerate points
	public struct PointInfo {
		public Vector3 pos;
		public Color32 color;
		public Vector3 normal;
	}
	public IEnumerable<PointInfo> EnumeratePoints() {
		if (halt) yield break;
		var point_info = default(PointInfo);
		if (octree == null) {
			foreach (var p in root.data.Enumerate()) {
				point_info.pos = p.pos;
				point_info.color = p.color;
				point_info.normal = p.normal;
				yield return point_info;
			}
		} else {
			foreach (var node_info in octree.EnumerateNodes()) {
				foreach (var p in node_info.node.data.Enumerate()) {
					point_info.pos = p.pos;
					point_info.color = p.color;
					point_info.normal = p.normal;
					yield return point_info;
				}
			}
		}
	}
	public IEnumerable<PointInfo> EnumeratePoints(Vector3Int grid_pos) {
		if (halt) yield break;
		if (octree == null) yield break;
		var node = octree.GetNode(grid_pos);
		if (node == null) yield break;
		var point_info = default(PointInfo);
		foreach (var p in node.data.Enumerate()) {
			point_info.pos = p.pos;
			point_info.color = p.color;
			point_info.normal = p.normal;
			yield return point_info;
		}
	}
	#endregion

	#region Voxel size estimation
	public float EstimateVoxelSize(float D_stop=1) {
		if (halt) return 0;
		if (octree != null) return voxelSize;
		return EstimateVoxelSize(root, pointBounds, D_stop);
	}
	float EstimateVoxelSize(OctreeNode<LinkedPoints> root, Bounds bounds, float D_stop=1) {
		if (root.data.count == 1) return 1; // as good as any

		// We cannot use number of points in node as a stopping criterion,
		// since it can't handle the case of coniciding points.
		// However, we can use fractal dimension.
		task = "Fractal subdividing";
		int levels = SubdivideFractalOctree(root, bounds, D_stop);
		if (halt) return 0;

		float max_bounds = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
		float min_dist = max_bounds / (1 << levels);
		// Remove 2 lowest levels to make sure that node size is bigger
		// than point cloud's spatial resolution
		if (levels > 0) { Unsibdivide(root); levels--; }
		if (levels > 0) { Unsibdivide(root); levels--; }
		float max_dist = max_bounds / (1 << levels);

		task = "Finding neighbors";
		var octree = new LeafOctree<LinkedPoints>(root);
		FindNeighbors(octree, min_dist, max_dist);
		if (halt) return 0;

		task = "Estimating distance";
		float d_min = float.MaxValue, d_max = 0;
		float d_avg = 0; int d_cnt = 0;
		foreach (var p in root.data.Enumerate()) {
			if (p.dist == float.MaxValue) continue;
			if (p.dist < d_min) d_min = p.dist;
			if (p.dist > d_max) d_max = p.dist;
			d_avg += p.dist;
			d_cnt++;
			progress = d_cnt / (float)root.data.count;
			if (halt) return 0;
		}
		d_avg /= d_cnt;

		root.Clear(); // clean up

		task = null;

		// We need to preserve actual voxel distance for
		// already-discretized point clouds. Empirically,
		// they are distinguished by d_min==d_avg==d_max.
		// For irregular point clouds, we might need to
		// scale distance by a factor of 2 to make sure
		// there are no holes.
		return d_avg*Mathf.Min(d_max/d_min, 2f);
	}

	void FindNeighbors(LeafOctree<LinkedPoints> octree, float min_dist, float max_dist) {
		int counter = 0;
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
								float dist = ManhattanMetric(p1.pos - p0.pos);
								if ((dist >= min_dist) & (dist <= max_dist)) {
									if (dist < p0.dist) p0.dist = dist;
									if (dist < p1.dist) p1.dist = dist;
								}
							}
						}
					}
				}
			}
			progress = (++counter) / (float)octree.Count;
			if (halt) return;
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
	static float EuclideanMetric(Vector3 diff) {
		return diff.magnitude;
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

	int SubdivideFractalOctree(OctreeNode<LinkedPoints> root, Bounds bounds, float D_stop=1, int max_levels=31) {
		if (D_stop >= 3f) return 1;

		float max_bounds = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
		float max_extents = max_bounds*0.5f;

		int _leaf_count = 1;
		float _leaf_size = max_extents;
		for (int level = 1; level <= max_levels; level++) {
			int leaf_count = 0;
			float leaf_size = max_extents;
			Subdivide(root, bounds.center, max_extents, ref leaf_count, ref leaf_size);
			float D = FractalDimension(_leaf_size, _leaf_count, leaf_size, leaf_count);
			progress = 1f - Mathf.Max(D-D_stop, 0f) / (3f-D_stop);
			if (halt) return 1;
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
				AddLinkedPoint(subnode, p);
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

public enum ColorSpaces {
	GammaRGB, LinearRGB, XYZ, CIE_Lab
}

public static class ColorUtils {
	// http://www.easyrgb.com/en/math.php
	// https://github.com/antimatter15/rgb-lab/blob/master/color.js
	public static Color GammaRGB_to_LinearRGB(Color RGB) {
		RGB.r = (RGB.r > 0.04045f ? Mathf.Pow(((RGB.r + 0.055f) / 1.055f), 2.4f) : RGB.r / 12.92f);
		RGB.g = (RGB.g > 0.04045f ? Mathf.Pow(((RGB.g + 0.055f) / 1.055f), 2.4f) : RGB.g / 12.92f);
		RGB.b = (RGB.b > 0.04045f ? Mathf.Pow(((RGB.b + 0.055f) / 1.055f), 2.4f) : RGB.b / 12.92f);
		return RGB;
	}
	public static Color LinearRGB_to_GammaRGB(Color RGB) {
		RGB.r = (RGB.r > 0.0031308f ? 1.055f * Mathf.Pow(RGB.r, 1f/2.4f) - 0.055f : 12.92f * RGB.r);
		RGB.g = (RGB.g > 0.0031308f ? 1.055f * Mathf.Pow(RGB.g, 1f/2.4f) - 0.055f : 12.92f * RGB.g);
		RGB.b = (RGB.b > 0.0031308f ? 1.055f * Mathf.Pow(RGB.b, 1f/2.4f) - 0.055f : 12.92f * RGB.b);
		return RGB;
	}
	public static Color LinearRGB_to_XYZ(Color RGB) {
		float R = RGB.r * 100f;
		float G = RGB.g * 100f;
		float B = RGB.b * 100f;
		float X = R * 0.4124f + G * 0.3576f + B * 0.1805f;
		float Y = R * 0.2126f + G * 0.7152f + B * 0.0722f;
		float Z = R * 0.0193f + G * 0.1192f + B * 0.9505f;
		return new Color(X, Y, Z, RGB.a);
	}
	public static Color XYZ_to_LinearRGB(Color XYZ) {
		float X = XYZ.r / 100f;
		float Y = XYZ.g / 100f;
		float Z = XYZ.b / 100f;
		float R = X *  3.2406f + Y * -1.5372f + Z * -0.4986f;
		float G = X * -0.9689f + Y *  1.8758f + Z *  0.0415f;
		float B = X *  0.0557f + Y * -0.2040f + Z *  1.0570f;
		return new Color(R, G, B, XYZ.a);
	}
	public readonly static Vector3 Reference_D65_10 = new Vector3(94.811f, 100.000f, 107.304f); // Daylight, sRGB, Adobe-RGB, 10° (CIE 1964)
	public static Color XYZ_to_Lab(Color XYZ) {
		return XYZ_to_Lab(XYZ, Reference_D65_10);
	}
	public static Color Lab_to_XYZ(Color Lab) {
		return Lab_to_XYZ(Lab, Reference_D65_10);
	}
	public static Color XYZ_to_Lab(Color XYZ, Vector3 Reference) {
		float X = XYZ.r / Reference.x;
		float Y = XYZ.g / Reference.y;
		float Z = XYZ.b / Reference.z;
		X = (X > 0.008856f ? Mathf.Pow(X, 1f/3f) : (7.787f * X) + (16f/116f));
		Y = (Y > 0.008856f ? Mathf.Pow(Y, 1f/3f) : (7.787f * Y) + (16f/116f));
		Z = (Z > 0.008856f ? Mathf.Pow(Z, 1f/3f) : (7.787f * Z) + (16f/116f));
		float L = (116f * Y) - 16f;
		float a = 500f * (X - Y);
		float b = 200f * (Y - Z);
		return new Color(L, a, b, XYZ.a);
	}
	public static Color Lab_to_XYZ(Color Lab, Vector3 Reference) {
		float Y = (Lab.r + 16f) / 116f;
		float X = Lab.g / 500f + Y;
		float Z = Y - Lab.b / 200f;
		float Y3 = Mathf.Pow(Y, 3f);
		float X3 = Mathf.Pow(X, 3f);
		float Z3 = Mathf.Pow(Z, 3f);
		Y = (Y3 > 0.008856f ? Y3 : (Y - (16f/116f)) / 7.787f);
		X = (X3 > 0.008856f ? X3 : (X - (16f/116f)) / 7.787f);
		Z = (Z3 > 0.008856f ? Z3 : (Z - (16f/116f)) / 7.787f);
		X = X * Reference.x;
		Y = Y * Reference.y;
		Z = Z * Reference.z;
		return new Color(X, Y, Z, Lab.a);
	}
}

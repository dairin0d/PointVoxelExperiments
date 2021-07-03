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

using System.Collections.Generic;
using UnityEngine;

namespace dairin0d.Data.Voxels {
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

        public int NodeCount;// { get; protected set; }

        public LeafOctree() {
            Root = null; Levels = 0; Count = 0; NodeCount = 0;
        }
        public LeafOctree(OctreeNode<T> root, int levels=-1, int count=-1, int node_count=-1) {
            if (root == null) { levels = 0; count = 0; node_count = 0; }
            this.Root = root; this.Levels = levels; this.Count = count; this.NodeCount = node_count;
            if ((levels < 0) | (count < 0) | (node_count < 0)) CalcLevels(root, 0);
        }
        void CalcLevels(OctreeNode<T> node, int level) {
            ++NodeCount;
            if (level > Levels) {
                Levels = level;
                Count = 1;
            } else if (level == Levels) {
                ++Count;
            }
            if (level >= 31) return; // int32 range limits
            for (int iXYZ = 0; iXYZ < 8; iXYZ++) {
                var subnode = node[iXYZ];
                if (subnode == node) continue; // just in case there are self-references
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
                    ++NodeCount;
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
                ++Levels;
            }
        }
        void InitOne(ref OctreeNode<T> node, int iXYZ) {
            if (node == null) return;
            var parent = new OctreeNode<T>();
            ++NodeCount;
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
                        ++NodeCount;
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
                        --NodeCount;
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

        public void Linearize(out int[] nodes, out T[] datas) {
            datas = new T[NodeCount];
            nodes = new int[NodeCount << 3];
            int id = 0;
            Root.Linearize(nodes, datas, ref id);
        }
    }
}
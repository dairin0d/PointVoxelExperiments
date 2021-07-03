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

using UnityEngine;

namespace dairin0d.Data.Voxels {
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

        public void Linearize(int[] nodes, T[] datas, ref int id) {
            int id0 = id, pos0 = id0 << 3;
            datas[id0] = data;
            for (int i = 0; i < 8; i++) {
                var subnode = this[i];
                if (subnode == null) {
                    nodes[pos0|i] = -1;
                } else if (subnode == this) {
                    nodes[pos0|i] = id0;
                } else {
                    ++id;
                    nodes[pos0|i] = id;
                    subnode.Linearize(nodes, datas, ref id);
                }
            }
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
}
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

namespace dairin0d.Rendering {
    public static class OctantOrder {
        // Node traversal order and traversal state can be combined into a
        // bit-string "queue" of octant indices (can also take into account
        // different number of stored octants). When a node is "dequeued",
        // the bit-string shifts by 4 bits. 3 bits for octant index,
        // 1 bit for signifying a non-empty queue item.
        
        public struct Queue {
            public uint Octants;
            public uint Indices;
        }
        
        public const int XYZ=0, XZY=1, YXZ=2, YZX=3, ZXY=4, ZYX=5;
        
        private static int[] counts = null;
        public static int[] Counts => counts ?? MakeCounts();
        
        private static int[] octantToIndex = null;
        public static int[] OctantToIndex => octantToIndex ?? MakeMaps().ToIndex;
        
        private static int[] indexToOctant = null;
        public static int[] IndexToOctant => indexToOctant ?? MakeMaps().ToOctant;
        
        private static Queue[] sparseQueues = null;
        public static Queue[] SparseQueues => sparseQueues ?? MakeQueues().Sparse;
        
        private static Queue[] packedQueues = null;
        public static Queue[] PackedQueues => packedQueues ?? MakeQueues().Packed;
        
        public static int Key(in Matrix4x4 matrix) {
            return ((Order(in matrix) << 3) | Octant(in matrix)) << 8;
        }
        
        public static int Octant(in Matrix4x4 matrix) {
            // Here we check which side of YZ/XZ/XY planes the view vector belongs to
            // This is specific to Unity's coordinate system (X right, Y up, Z forward)
            int bit_x = (matrix.m11 * matrix.m02 <= matrix.m01 * matrix.m12 ? 0 : 1); // Y.y * Z.x <= Y.x * Z.y
            int bit_y = (matrix.m12 * matrix.m00 <= matrix.m02 * matrix.m10 ? 0 : 2); // Z.y * X.x <= Z.x * X.y
            int bit_z = (matrix.m10 * matrix.m01 <= matrix.m00 * matrix.m11 ? 0 : 4); // X.y * Y.x <= X.x * Y.y
            return bit_x | bit_y | bit_z;
        }
        
        public static int Order(in Matrix4x4 matrix) {
            return Order(matrix.m20, matrix.m21, matrix.m22);
        }
        public static int Order(float x_z, float y_z, float z_z) {
            if (x_z < 0f) x_z = -x_z;
            if (y_z < 0f) y_z = -y_z;
            if (z_z < 0f) z_z = -z_z;
            if (x_z <= y_z) {
                return (x_z <= z_z ? (y_z <= z_z ? XYZ : XZY) : ZXY);
            } else {
                return (y_z <= z_z ? (x_z <= z_z ? YXZ : YZX) : ZYX);
            }
        }
        
        private static int[] MakeCounts() {
            counts = new int[256];
            
            for (int mask = 0; mask < counts.Length; mask++) {
                int count = 0;
                for (int bits = mask; bits != 0; bits >>= 1) {
                    if ((bits & 1) != 0) count++;
                }
                counts[mask] = count;
            }
            
            return counts;
        }
        
        private static (int[] ToIndex, int[] ToOctant) MakeMaps() {
            octantToIndex = new int[256*8];
            indexToOctant = new int[256*8];
            
            for (int mask = 0; mask < 256; mask++) {
                int maskKey = mask << 3;
                int maxIndex = -1;
                for (int octant = 0; octant < 8; octant++) {
                    int index = GetOctantIndex(octant, mask);
                    octantToIndex[maskKey|octant] = index;
                    if (index > maxIndex) maxIndex = index;
                    if (index < 0) continue;
                    indexToOctant[maskKey|index] = octant;
                }
                for (int index = maxIndex+1; index < 8; index++) {
                    indexToOctant[maskKey|index] = -1;
                }
            }
            
            return (octantToIndex, indexToOctant);
        }
        
        private static int GetOctantIndex(int octant, int mask) {
            int index = -1;
            for (; (mask != 0) & (octant >= 0); mask >>= 1, octant--) {
                if ((mask & 1) != 0) index++;
            }
            return index;
        }
        
        private static (Queue[] Packed, Queue[] Sparse) MakeQueues() {
            packedQueues = new Queue[6*8*256];
            for (int order = 0; order < 6; order++) {
                for (int octant = 0; octant < 8; octant++) {
                    for (int mask = 0; mask < 256; mask++) {
                        packedQueues[(((order << 3) | octant) << 8) | mask] = MakeQueue(octant, order, mask, true);
                    }
                }
            }
            
            sparseQueues = new Queue[packedQueues.Length];
            for (int i = 0; i < sparseQueues.Length; i++) {
                sparseQueues[i].Octants = sparseQueues[i].Indices = packedQueues[i].Octants;
            }
            
            return (packedQueues, sparseQueues);
        }
        
        private static Queue MakeQueue(int start, int order, int mask, bool packed = false) {
            int _u = 0, _v = 0, _w = 0;
            switch (order) {
            case XYZ: _u = 0; _v = 1; _w = 2; break;
            case XZY: _u = 0; _v = 2; _w = 1; break;
            case YXZ: _u = 1; _v = 0; _w = 2; break;
            case YZX: _u = 1; _v = 2; _w = 0; break;
            case ZXY: _u = 2; _v = 0; _w = 1; break;
            case ZYX: _u = 2; _v = 1; _w = 0; break;
            }
            
            var map = OctantToIndex;
            
            Queue queue = default;
            int shift = 0;
            for (int w = 0; w <= 1; w++) {
                for (int v = 0; v <= 1; v++) {
                    for (int u = 0; u <= 1; u++) {
                        int flip = (u << _u) | (v << _v) | (w << _w);
                        int octant = (start ^ flip);
                        if ((mask & (1 << octant)) == 0) continue;
                        int index = packed ? map[(mask << 3)|octant] : octant;
                        queue.Octants |= (uint)((octant|8) << shift);
                        queue.Indices |= (uint)((index|8) << shift);
                        shift += 4;
                    }
                }
            }
            
            return queue;
        }
    }
}
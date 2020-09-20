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

using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

using dairin0d.Data.Points;
using dairin0d.Data.Voxels;

namespace dairin0d.Rendering.Octree {
    class RawOctree {
        public int root_node;
        public Color32 root_color;
        public int[] nodes;
        public Color32[] colors;

        public static RawOctree MakeFractal(byte mask, Color32 color) {
            int root_node = mask << 24;
            var nodes = new int[8];
            var colors = new Color32[8];
            for (int i = 0; i < 8; i++) {
                nodes[i] = ((mask & (1 << i)) != 0 ? root_node : 0);
                colors[i] = color;
            }
            return new RawOctree() {
                root_node = root_node,
                root_color = color,
                nodes = nodes,
                colors = colors,
            };
        }

        public static RawOctree LoadPointCloud(string path, float voxel_size = -1) {
            if (string.IsNullOrEmpty(path)) return null;

            string cached_path = path + ".cache3";
            if (File.Exists(cached_path)) {
                if (!File.Exists(path)) {
                    return LoadCached(cached_path);
                }
                if (File.GetLastWriteTime(cached_path) >= File.GetLastWriteTime(path)) {
                    return LoadCached(cached_path);
                }
            }

            if (!File.Exists(path)) return null;

            var discretizer = new PointCloudDiscretizer();
            using (var pcr = new PointCloudFile.Reader(path)) {
                Vector3 pos; Color32 color; Vector3 normal;
                while (pcr.Read(out pos, out color, out normal)) {
                    discretizer.Add(pos, color, normal);
                }
            }
            discretizer.Discretize(voxel_size);
            //discretizer.FloodFill();

            var octree = new LeafOctree<Color32>();
            foreach (var voxinfo in discretizer.EnumerateVoxels()) {
                var node = octree.GetNode(voxinfo.pos, OctreeAccess.AutoInit);
                node.data = voxinfo.color;
            }

            var converted = ConvertOctree(octree);
            WriteCached(converted, cached_path);
            return converted;
        }

        private static RawOctree LoadCached(string cached_path) {
            try {
                var octree = new RawOctree();
                var stream = new FileStream(cached_path, FileMode.Open, FileAccess.Read);
                var br = new BinaryReader(stream);
                {
                    int node_count = br.ReadInt32();
                    octree.root_node = br.ReadInt32();
                    octree.root_color = ReadColor32(br);
                    octree.nodes = ReadArray<int>(br, node_count << 3);
                    octree.colors = ReadArray<Color32>(br, node_count << 3);
                }
                stream.Close();
                stream.Dispose();
                SanitizeNodes(octree.nodes);
                Debug.Log("Cached version loaded: " + cached_path);
                return octree;
            } catch (System.Exception exc) {
                Debug.LogException(exc);
                return null;
            }
        }

        private static unsafe void SanitizeNodes(int[] nodes) {
            const int mask = 0xFF << 24;
            fixed (int* _nodes_ptr = nodes) {
                int* nodes_ptr = _nodes_ptr;
                int* nodes_end = nodes_ptr + nodes.Length;
                for (; nodes_ptr != nodes_end; ++nodes_ptr) {
                    if (((*nodes_ptr) & mask) == 0) *nodes_ptr = 0;
                }
            }
        }

        private static void WriteCached(RawOctree octree, string cached_path) {
            var stream = new FileStream(cached_path, FileMode.Create, FileAccess.Write);
            var bw = new BinaryWriter(stream);
            {
                bw.Write(octree.nodes.Length >> 3);
                bw.Write(octree.root_node);
                WriteColor32(bw, octree.root_color);
                WriteArray(bw, octree.nodes);
                WriteArray(bw, octree.colors);
            }
            bw.Flush();
            stream.Flush();
            stream.Close();
            stream.Dispose();
            Debug.Log("Cached version saved: " + cached_path);
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Color32_int {
            [FieldOffset(0)] public Color32 c;
            [FieldOffset(0)] public int i;
        }
        private static void WriteColor32(BinaryWriter bw, Color32 color) {
            Color32_int color_int = default;
            color_int.c = color;
            bw.Write(color_int.i);
        }
        private static Color32 ReadColor32(BinaryReader br) {
            Color32_int color_int = default;
            color_int.i = br.ReadInt32();
            return color_int.c;
        }

        private unsafe static void WriteArray<T>(BinaryWriter bw, T[] array) where T : struct {
            var bytes = new byte[array.Length * Marshal.SizeOf<T>()];
            // System.Buffer.BlockCopy() only works for arrays of primitive types
            fixed (byte* bytes_ptr = bytes) {
                GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                var array_ptr = handle.AddrOfPinnedObject().ToPointer();
                System.Buffer.MemoryCopy(array_ptr, bytes_ptr, bytes.Length, bytes.Length);
                handle.Free();
            }
            bw.Write(bytes);
        }
        private unsafe static T[] ReadArray<T>(BinaryReader br, int count) where T : struct {
            var bytes = br.ReadBytes(count * Marshal.SizeOf<T>());
            var array = new T[count];
            // System.Buffer.BlockCopy() only works for arrays of primitive types
            fixed (byte* bytes_ptr = bytes) {
                GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                var array_ptr = handle.AddrOfPinnedObject().ToPointer();
                System.Buffer.MemoryCopy(bytes_ptr, array_ptr, bytes.Length, bytes.Length);
                handle.Free();
            }
            return array;
        }

        private static RawOctree ConvertOctree(LeafOctree<Color32> octree) {
            // int octree_levels = octree.Levels;
            var colors = new Color32[octree.NodeCount << 3];
            var nodes = new int[octree.NodeCount << 3];
            int id = 0;
            var (mask, color) = LinearizeOctree(octree.Root, nodes, colors, ref id);
            return new RawOctree() {
                root_node = mask << 24,
                root_color = color,
                nodes = nodes,
                colors = colors,
            };
        }

        private static (int, Color32) LinearizeOctree(OctreeNode<Color32> node, int[] nodes, Color32[] colors, ref int id) {
            Color color = default;
            int count = 0;
            int mask = 0;
            int id0 = id, pos0 = id0 << 3;

            for (int i = 0; i < 8; i++) {
                var subnode = node[i];
                if (subnode == null) continue;

                mask |= (1 << i);

                if (subnode == node) {
                    nodes[pos0 | i] = (255 << 24) | id0;
                    colors[pos0 | i] = subnode.data;
                } else {
                    ++id;
                    int subid = id;
                    var (submask, subcolor) = LinearizeOctree(subnode, nodes, colors, ref id);
                    if (submask == 0) subid = 0;
                    nodes[pos0 | i] = (submask << 24) | subid;
                    colors[pos0 | i] = subcolor;
                }

                color.r += colors[pos0 | i].r;
                color.g += colors[pos0 | i].g;
                color.b += colors[pos0 | i].b;
                color.a += colors[pos0 | i].a;
                ++count;
            }

            if (count == 0) return (0, node.data);

            float color_scale = 1f / (count * 255);
            color.r *= color_scale;
            color.g *= color_scale;
            color.b *= color_scale;
            color.a *= color_scale;

            return (mask, color);
        }
    }
}
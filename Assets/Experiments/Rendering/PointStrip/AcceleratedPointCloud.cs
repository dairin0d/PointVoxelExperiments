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
using System.IO;
using UnityEngine;

using ColorQuantization;

using dairin0d.Data.Colors;
using dairin0d.Data.Points;
using dairin0d.Data.Voxels;

namespace dairin0d.Rendering.PointStrip {
    // Judging by magic vs standard float->int performace,
    // Unity's conversion is just as fast (or maybe compiles
    // to the same trick under the hood).
    // Either way, using floats, 1000000 points without boundary checks
    // takes ~30 ms in editor and ~13 ms in build.
    // Using fixed point: ~23 ms in editor, ~12 ms in build (both int and sbyte)


    // Note: these results are for relatively small screen size of the object
    // (i.e. a lot of points don't actually update the buffers).
    // Using floats: ~32 ms in editor, ~11 ms in build (1 mln points)
    // Using ints: ~50 ms in editor, ~18 ms in build (2 mln points)
    // Does not seem to depend on the size of pointsI[] item

    // When object occupies whole screen, performance drops significantly
    // (rendering is ~twice as slow). However, if only 1 combined buffer
    // is used, the drop is not as bad (in build, ~40 ms worst case).

    // In editor, iAbs and iRel are ~comparable, but in build
    // iRel is noticeably more efficient

    // 27 directions is somewhat slower than 6 directions
    // (~23 ms vs ~18 ms for 2 mln points), but considering that
    // 6-dir would need to either be limited to 6-connected patches
    // (which makes them useless for curved surfaces) or to use
    // invisible points (which increases the number of actual points
    // and introduces a rather unpredictable branch), 27-directions
    // would actually be faster for arbitrary-oriented surfaces.


    // Using node objects and individual switch-cases: ~0.55 s
    // Using linearized array: ~ 0.38

    // Using array for deltas seems a bit faster than switch-case

    // Using stack of linked objects is a bit faster than using an array of structs/objects

    // using a supplementary array of masks is a bit faster (due to less work/accesses, on average)


    // To not create significant overhead, number of traversed octree nodes needs to be under ~100k per frame.
    // Asuming it corresponds roughly to 25k leaf nodes, "fast blitting" needs to replace more than ~12 leaf draws (640x480) or ~82 leaf draws (1920x1080).

    // Storage: (N = number of entries in data, S = size of data entry)
    // Pure delta-points: N*S + N*(5 bit)
    // Minimal octree (no mip-maps): N*S + N*(1 byte)*[depends on clustering of points; worst case 1+1+..., best case 1/8+1/64+..., plane 1/4+1/16+...]
    // Minimal octree (+mip-maps): N*S + N*(1 byte + S)*[depends on clustering ...]

    // It seems that the most efficient way is to combine occlusion culling capabilities of octree
    // with raw drawing speed of delta-points, traversing the octree to a sufficiently small
    // node size, then blitting the delta-points corresponding to that LOD level.
    // It would still be slow at high resolutions, but for 640x480 might actually suffice.

    class AcceleratedPointCloud {
        int[] nodes;
        PointsLOD[] datas;
        byte[] masks;
        int octree_levels;
        int viz_level;

        StackDataObj sdoRoot;

        Vector3i[] deltas = new Vector3i[Directions.Count];

        Color32[] palette;

        public int n_oct = 0;
        public int n_abs = 0;
        public int n_rel = 0;

        public int delta_max = 0;
        public float delta_max_rel = 0;

        public AcceleratedPointCloud(string path, float voxel_size, int aggregate = 0) {
            if (sdoRoot == null) {
                sdoRoot = new StackDataObj();
                var sdo0 = sdoRoot;
                for (int i = 0; i < 1024; i++) {
                    var sdo1 = new StackDataObj();
                    sdo0.next = sdo1;
                    sdo1.prev = sdo0;
                    sdo0 = sdo1;
                }
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string cached_path = (ext == ".vco" ? path : path + ".vco");
            if (File.Exists(cached_path)) {
                if (File.GetLastWriteTime(cached_path) >= File.GetLastWriteTime(path)) {
                    LoadCached(cached_path);
                }
            }

            if (nodes == null) {
                LoadPointCloud(path, cached_path, voxel_size, aggregate);
            }
        }

        void LoadCached(string cached_path) {
            var stream = new FileStream(cached_path, FileMode.Open, FileAccess.Read);
            var br = new BinaryReader(stream);
            {
                palette = new Color32[br.ReadInt32()];
                for (int i = 0; i < palette.Length; i++) {
                    palette[i].r = br.ReadByte();
                    palette[i].g = br.ReadByte();
                    palette[i].b = br.ReadByte();
                    palette[i].a = br.ReadByte();
                }
                octree_levels = br.ReadInt32();
                viz_level = br.ReadInt32();
                masks = br.ReadBytes(br.ReadInt32());
                nodes = new int[br.ReadInt32()];
                for (int i = 0; i < nodes.Length; i++) {
                    nodes[i] = br.ReadInt32();
                }
                datas = new PointsLOD[br.ReadInt32()];
                for (int i = 0; i < datas.Length; i++) {
                    datas[i].bytes = br.ReadBytes(br.ReadInt32());
                }
            }
            stream.Close();
            stream.Dispose();
        }

        void WriteCached(string cached_path) {
            var stream = new FileStream(cached_path, FileMode.Create, FileAccess.Write);
            var bw = new BinaryWriter(stream);
            {
                bw.Write(palette.Length);
                for (int i = 0; i < palette.Length; i++) {
                    bw.Write(palette[i].r);
                    bw.Write(palette[i].g);
                    bw.Write(palette[i].b);
                    bw.Write(palette[i].a);
                }
                bw.Write(octree_levels);
                bw.Write(viz_level);
                bw.Write(masks.Length);
                bw.Write(masks);
                bw.Write(nodes.Length);
                for (int i = 0; i < nodes.Length; i++) {
                    bw.Write(nodes[i]);
                }
                bw.Write(datas.Length);
                for (int i = 0; i < datas.Length; i++) {
                    if (datas[i].bytes == null) {
                        bw.Write(0);
                    } else {
                        bw.Write(datas[i].bytes.Length);
                        bw.Write(datas[i].bytes);
                    }
                }
            }
            bw.Flush();
            stream.Flush();
            stream.Close();
            stream.Dispose();
            Debug.Log("Cached version saved: " + cached_path);
        }

        void LoadPointCloud(string path, string cached_path, float voxel_size = -1, int aggregate = 5) {
            aggregate = Mathf.Clamp(aggregate, 0, 5);

            var discretizer = new PointCloudDiscretizer();
            using (var pcr = new PointCloudFile.Reader(path)) {
                Vector3 pos; Color32 color; Vector3 normal;
                while (pcr.Read(out pos, out color, out normal)) {
                    discretizer.Add(pos, color, normal);
                }
            }
            discretizer.Discretize(voxel_size);
            //discretizer.FloodFill();

            var colors = new List<Color32>();

            var octree = new LeafOctree<PointsLOD>();
            foreach (var voxinfo in discretizer.EnumerateVoxels()) {
                var node = octree.GetNode(voxinfo.pos, OctreeAccess.AutoInit);
                node.data.color = voxinfo.color;
                var pdata = new PointData(voxinfo.pos.x, voxinfo.pos.y, voxinfo.pos.z, voxinfo.color);
                node.data.points = new PointData[] { pdata };
                colors.Add(voxinfo.color);
            }

            palette = NeuQuant.Quantize(colors);
            var palette_finder = new Color32Palette(palette);

            foreach (var node_info in octree.EnumerateNodes(OctreeAccess.AnyLevel)) {
                var extents = (1 << node_info.level) >> 1;
                var pos = node_info.pos;
                var node = node_info.node;
                node.data.ox = pos.x;
                node.data.oy = pos.y;
                node.data.oz = pos.z;
            }

            for (int i = 0; i < aggregate; i++) {
                AggregatePoints(octree.Root);
            }

            ConvertPointsToBytes(octree.Root, palette_finder);

            octree.Linearize(out nodes, out datas);
            octree_levels = octree.Levels;

            viz_level = aggregate - 1;

            masks = new byte[datas.Length];
            for (int id = 0; id < masks.Length; id++) {
                int mask = 0;
                for (int i = 0; i < 8; i++) {
                    if (nodes[(id << 3) | i] >= 0) mask |= (1 << i);
                }
                masks[id] = (byte)mask;
            }

            WriteCached(cached_path);

            Debug.Log(octree.Count + " -> " + octree.NodeCount);
        }

        void AggregatePoints(OctreeNode<PointsLOD> node) {
            if (node.data.points != null) return;
            int n = 0;
            for (int i = 0; i < 8; i++) {
                var subnode = node[i];
                if (subnode == null) continue;
                if (subnode.data.points == null) continue;
                n += subnode.data.points.Length;
            }
            if (n > 0) {
                node.data.points = new PointData[n];
                int id = 0;
                for (int i = 0; i < 8; i++) {
                    var subnode = node[i];
                    if (subnode == null) continue;
                    if (subnode.data.points == null) continue;
                    for (int j = 0; j < subnode.data.points.Length; j++) {
                        node.data.points[id] = subnode.data.points[j];
                        id++;
                    }
                }
            } else {
                for (int i = 0; i < 8; i++) {
                    var subnode = node[i];
                    if (subnode == null) continue;
                    AggregatePoints(subnode);
                }
            }
        }

        void ConvertPointsToBytes(OctreeNode<PointsLOD> node, Color32Palette palette_finder) {
            if (node.data.points != null) {
                var points = node.data.points;
                var bytes = new List<byte>();
                var indices = BuildPointsGraph(points);
                for (int ii = 0; ii < indices.Count; ii++) {
                    var p = points[indices[ii]];
                    int idir = -1;
                    if (ii > 0) {
                        var p_prev = points[indices[ii - 1]];
                        idir = Directions.Index(p.x - p_prev.x, p.y - p_prev.y, p.z - p_prev.z);
                    }
                    byte palette_id = (byte)palette_finder.FindIndex(p.color);
                    if (idir >= 0) {
                        bytes.Add((byte)idir);
                        bytes.Add(palette_id);
                    } else {
                        int dx = p.x - node.data.ox;
                        int dy = p.y - node.data.oy;
                        int dz = p.z - node.data.oz;
                        dx += 16; dy += 16; dz += 16; // signed delta: add half-size
                        int dxyz = (dx << 10) | (dy << 5) | dz;
                        bytes.Add((byte)((dxyz >> 8) | 128));
                        bytes.Add((byte)(dxyz & 0xFF));
                        bytes.Add(palette_id);
                    }
                }
                node.data.bytes = bytes.ToArray();
            }
            for (int i = 0; i < 8; i++) {
                var subnode = node[i];
                if (subnode == null) continue;
                ConvertPointsToBytes(subnode, palette_finder);
            }
        }

        List<int> BuildPointsGraph(PointData[] points) {
            var cpoints = new List<ConnectedPoint>();
            for (int i = 0; i < points.Length; i++) {
                cpoints.Add(new ConnectedPoint(i));
            }
            for (int i = 0; i < cpoints.Count; i++) {
                var cp0 = cpoints[i];
                var p0 = points[cp0.id];
                for (int j = i + 1; j < cpoints.Count; j++) {
                    var cp1 = cpoints[j];
                    var p1 = points[cp1.id];
                    int dx = Mathf.Abs(p1.x - p0.x);
                    int dy = Mathf.Abs(p1.y - p0.y);
                    int dz = Mathf.Abs(p1.z - p0.z);
                    if ((dx | dy | dz) == 1) {
                        cp0.neighbors.Add(cp1);
                        cp1.neighbors.Add(cp0);
                    }
                }
            }

            var indices = new List<int>();
            for (int i = cpoints.Count - 1; i >= 0; i--) {
                var cp = cpoints[i];
                if (cp.neighbors.Count == 0) {
                    indices.Add(cp.id);
                    cpoints.RemoveAt(i);
                }
            }

            while (cpoints.Count > 0) {
                cpoints.Sort((cp0, cp1) => {
                    return cp0.neighbors.Count - cp1.neighbors.Count;
                });
                var cp = cpoints[0];
                while (cp != null) {
                    indices.Add(cp.id);
                    cpoints.Remove(cp);
                    if (cp.neighbors.Count == 0) break;
                    var best_cp = cp.neighbors[0];
                    for (int i = 1; i < cp.neighbors.Count; i++) {
                        var cpn = cp.neighbors[i];
                        if (cpn.neighbors.Count < best_cp.neighbors.Count) {
                            best_cp = cpn;
                        }
                    }
                    for (int i = 0; i < cp.neighbors.Count; i++) {
                        var cpn = cp.neighbors[i];
                        cpn.neighbors.Remove(cp);
                    }
                    cp = best_cp;
                }
            }

            return indices;
        }

        public void Render(Matrix4x4 matrix, BufData[] buf, int buf_shift, int w, int h, int queue_key, int dviz = 0) {
            int shift = 8;
            int s = (1 << shift);
            int S = s << 8;
            int xX = (int)(matrix.m00 * s), xY = (int)(matrix.m01 * s), xZ = (int)(matrix.m02 * s), xT = (int)(matrix.m03 * s);
            int yX = (int)(matrix.m10 * s), yY = (int)(matrix.m11 * s), yZ = (int)(matrix.m12 * s), yT = (int)(matrix.m13 * s);
            // int zX=(int)(matrix.m20*s), zY=(int)(matrix.m21*s), zZ=(int)(matrix.m22*s), zT=(int)(matrix.m23*s);
            int zX = (int)(matrix.m20 * S), zY = (int)(matrix.m21 * S), zZ = (int)(matrix.m22 * S), zT = (int)(matrix.m23 * S);

            delta_max = 0;

            var directions = Directions.Get();

            for (int idir = 0; idir < directions.Length; idir++) {
                var dir = directions[idir];
                int sx = dir.x, sy = dir.y, sz = dir.z;
                deltas[idir] = new Vector3i(xX * sx + xY * sy + xZ * sz, yX * sx + yY * sy + yZ * sz, zX * sx + zY * sy + zZ * sz);

                delta_max = Mathf.Max(delta_max, Mathf.Abs(deltas[idir].x));
                delta_max = Mathf.Max(delta_max, Mathf.Abs(deltas[idir].y));
            }

            delta_max_rel = delta_max / (float)s;

            int buf_size = 1 << buf_shift;
            int buf_mask = ~(buf_size - 1);

            int bx0 = 0, by0 = 0, bz0 = 0;
            int bx1 = 0, by1 = 0, bz1 = 0;
            for (int idir = 0; idir < directions.Length; idir++) {
                bx0 = Mathf.Min(bx0, deltas[idir].x);
                by0 = Mathf.Min(by0, deltas[idir].y);
                bz0 = Mathf.Min(bz0, deltas[idir].z);
                bx1 = Mathf.Max(bx1, deltas[idir].x);
                by1 = Mathf.Max(by1, deltas[idir].y);
                bz1 = Mathf.Max(bz1, deltas[idir].z);
            }

            var sdo = sdoRoot;
            sdo.id = 0;
            sdo.level = octree_levels - 1;
            sdo.pos = new Vector3i(new Vector3(xT, yT, zT));
            sdo.pos.z &= -256;

            // uint order = 0;
            // for (int i = 0; i < 8; i++) {
            // 	order |= (uint)((i|8) << (i*4));
            // }
            uint order = OctantOrder.Queues[(queue_key << 8) | 255];

            // n_oct = n_abs = n_rel = 0;

            dviz = Mathf.Clamp(dviz, -viz_level, 0);

            // int min_level = viz_level-1;
            int min_level = viz_level + dviz;
        start0:;
            if (sdo != null) {
                // while (sdo != null) {
                // int level = sdo.level-1;
                // var sd0_pos = sdo.pos;
                // int sd0_id3 = sdo.id << 3;
                // int m = masks[sdo.id];

                // if (level > min_level) {
                if (sdo.level > min_level) {
                    var sd0_pos = sdo.pos;

                    int ix0 = (sd0_pos.x + (bx0 << sdo.level)) >> shift;
                    int ix1 = (sd0_pos.x + (bx1 << sdo.level)) >> shift;
                    int iy0 = (sd0_pos.y + (by0 << sdo.level)) >> shift;
                    int iy1 = (sd0_pos.y + (by1 << sdo.level)) >> shift;

                    if (ix0 < 0) ix0 = 0;
                    if (ix1 >= w) ix1 = w - 1;
                    if (iy0 < 0) iy0 = 0;
                    if (iy1 >= h) iy1 = h - 1;
                    if ((ix1 < ix0) | (iy1 < iy0)) goto skip;

                    int z0 = (sd0_pos.z + (bz0 << sdo.level));

                    // bool test_passed = false;
                    for (int iy = iy0; iy <= iy1; ++iy) {
                        for (int ix = ix0; ix <= ix1; ++ix) {
                            int ixy = ix | (iy << buf_shift);
                            if (z0 < buf[ixy].zi) goto proceed;
                            // if (z0 < buf[ixy].zi) test_passed = true;
                            // buf[ixy].c.r = (byte)Mathf.Min(buf[ixy].c.r+16, 255);
                        }
                    }
                    // if (test_passed) goto proceed;
                    goto skip;
                proceed:;

                    ++n_oct;

                    int level = sdo.level - 1;
                    int sd0_id3 = sdo.id << 3;
                    int m = masks[sdo.id];
                    for (uint o = order; o != 0; o >>= 4) {
                        int octant = unchecked((int)(o & 7));
                        if (((1 << octant) & m) != 0) {
                            sdo.id = nodes[sd0_id3 | octant];
                            sdo.level = level;
                            sdo.pos.x = sd0_pos.x + (deltas[octant].x << level);
                            sdo.pos.y = sd0_pos.y + (deltas[octant].y << level);
                            sdo.pos.z = sd0_pos.z + (deltas[octant].z << level);
                            sdo = sdo.next;
                        }
                    }

                skip:;
                    sdo = sdo.prev;
                    goto start0;
                } else {
                    var bytes = datas[sdo.id].bytes;
                    var sd0_pos = sdo.pos;
                    int px = sd0_pos.x, py = sd0_pos.y, pz = sd0_pos.z;
                    int i = 0, imax = bytes.Length;
                // while (i < imax) {
                start:;
                    byte b = bytes[i];
                    if (b < 128) {
                        ++n_rel;
                        px += deltas[b].x; py += deltas[b].y; pz += deltas[b].z;
                        int ix = (px >> shift), iy = (py >> shift);
                        if (((ix | iy) & buf_mask) == 0) {
                            int ixy = ix | (iy << buf_shift);
                            if (pz < buf[ixy].zi) { buf[ixy].zi = pz; buf[ixy].c = palette[bytes[i + 1]]; }
                            // if (pz < buf[ixy].zi) { buf[ixy].zi = pz; buf[ixy].c.g = (byte)Mathf.Min(buf[ixy].c.g+16, 255); }
                        }
                        i += 2;
                        if (i < imax) goto start;
                    } else {
                        ++n_abs;
                        int dxyz = (b << 8) | bytes[i + 1];
                        int dx = ((dxyz >> 10) & 31) - 16;
                        int dy = ((dxyz >> 5) & 31) - 16;
                        int dz = (dxyz & 31) - 16;
                        px = (dx * xX + dy * xY + dz * xZ + sd0_pos.x);
                        py = (dx * yX + dy * yY + dz * yZ + sd0_pos.y);
                        pz = (dx * zX + dy * zY + dz * zZ + sd0_pos.z);
                        int ix = (px >> shift), iy = (py >> shift);
                        if (((ix | iy) & buf_mask) == 0) {
                            int ixy = ix | (iy << buf_shift);
                            if (pz < buf[ixy].zi) { buf[ixy].zi = pz; buf[ixy].c = palette[bytes[i + 2]]; }
                            // if (pz < buf[ixy].zi) { buf[ixy].zi = pz; buf[ixy].c.g = (byte)Mathf.Min(buf[ixy].c.g+16, 255); }
                        }
                        i += 3;
                        if (i < imax) goto start;
                    }
                    // }
                    sdo = sdo.prev;
                    goto start0;
                }
                // sdo = sdo.prev;
            }
        }
    }
}

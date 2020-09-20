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

/*
Main variants:
* Raycasting (image-order):
  a simpler and more optimal algorithm for a single pixel, but the calculations
  cannot be reused between pixels -> O(pixels) * O(nodes)
* Splatting (object-order):
  more complex, but node calculations are only done once and usually check
  much less pixels than their area -> O(nodes) * O(occluded pixels)
* Quadtree (in-between):
  the only potential advantages here are quicker occlusion checks and some buffer
  data locality, but it ends up with more false positives (queued nodes)

General optimization ideas:
* bandwidth vs computation?
* cache coherency
  * memory locality
  * minimize data size
  * try to fit into 64k level 2 cache
  * minimize unnecessary accesses
* branch (mis)prediction
* use pointers
* int instead of float

Implementation variants:
* one buffer
* tiles (fits into cache, but some repeated work and more boundary checks)
* one buffer + cached nodes from previous frame
// theoretically, node caching can be used for tiles too, but it might not be very
// useful (a separate cache for each tile may have much less overlap with the next
// frame, while shared cache will be much less coherent after the first tile)

Rejected ideas:
* skip lists, quadtrees, etc. either need to be searched or need expensive updates

Performance notes:
* pre-computing traversal orders: an additional memory read, and nodes need to
  store occupancy bitmask, but it allows to process only the occupied nodes,
  and the child node loop has only one condition -> less work/accesses, on average
* interleaved buffer is ~faster than separate buffers (at least in build)
* linearized array is ~faster than references + switch-case
* using array for deltas is a bit faster than switch-case
* using a stack of linked objects is a bit faster than an array of structs/objects
  (though this may only be due to array bounds checking)
* casting pointers has an overhead in Mono builds (but not in IL2CPP builds)
* in Mono builds, masking (e.g. 0xFFFF) is faster than using a field (e.g. int16);
  in IL2CPP builds, it seems to make no difference
* Using "not drawn test" (node.z = constant) instead of depth test slightly
  reduces the number of processed nodes, but not enough to be useful.
* Render1 is ~20% faster than Render0 for IL2CPP, and ~10% slower for Mono.
* Raycasting without a map is several times slower than splatting
  (porttown: around 300-350 ms at 640x480)

Various ideas:
* pixels whose depth test had failed for the parent node don't need to be
  checked again for the child nodes
  [+] this noticeably decreases the number of occlusion-tested pixels
* tiles of level 2 cache size (64k) (also may help with subpixel precision)
  [-] does not seem to improve performance at all
* 2x2 nodes: precompute 4 masks (which octants fall into / intersect the
  pixels), then get nearest node via (order[node.mask & pixel.mask] & 7)
  [+/-] improves performance, but results in holes
* store color at subnode level -> less non-coherent reads at the pixel level
* caching nodes from previous frame (additional expense of writing the nodes
  to the cache, but more coherent memory accesses in the next frame; also,
  this allows "dynamic unpacking" of e.g. linkless octrees). However, this
  requires to fully read node data (or at least to make a full copy).

Layout variants:
* masks[], offsets[], colors[]
* (mask|offset)[], colors[]
* (8 masks|offsets, 8 colors)[]
* (mask|offset, color)[]
* (mask|color, offset)[]
Colors are rarely accessed together with masks|offsets (the only case when
they are often accessed together is node caching). However, even for node
caching most colors would be unused (thus less masks|offsets would actually
fit into a single cache line).

Making Octree/Buffer/Splatter generic is probably technically possible,
but without an unmanaged type constraint (proposed in C# 7.3), pointers
to them are not allowed -> less efficient. Also, for generic data,
blending with background can only be done via callbacks.

At least for simple clearing/blitting, tile size does not seem to matter

It seems that only the conservative method of delta calculations
guarantees the absence of holes.

It seems that, aside from creating octree copies/LODs at intermediate
resolutions, there's virtually nothing we can do to reduce the worst-case
number of pixel-level nodes.
So the only thing we can try at this point is to make the loop itself
more optimal. If node size <= 4 pixels, don't add to stack in reverse
order, just draw in direct order? (using precomputed 2x2 pixel bitmask)
Or precompute a "raycast order" map, to use early-z stopping?

//////////////////////////////////////////////////////////////////////
Example of pinning without the fixed keyword:
var pinnedText = GCHandle.Alloc(text, GCHandleType.Pinned);
char* textAddress = (char*)pinnedText.AddrOfPinnedObject().ToPointer();
...
pinnedText.Free();

Example of allocating/freeing unmanaged memory:
IntPtr hglobal = Marshal.AllocHGlobal(100);
Marshal.FreeHGlobal(hglobal);



// Separable maps for X and Y?

queue = queues[forward_key | mask];

for (; queue != 0; queue >>= 4) {
    int octant = (int)(queue & 7);
    
    var delta = level_deltas + octant;
    
    int _x0 = x0 + delta->x0, _px0 = _x0 >> subpixel_shift;
    int _y0 = y0 + delta->y0, _py0 = _y0 >> subpixel_shift;
    
    var pixel = tile + (_px0 + (_py0 << tile_shift));
    
    var _node = node[octant];
    color = colors + ((_node & 0xFFFFFF) << 3);
    int mask = (_node >> 24) & 0xFF;
    
    map_x = mapX[_x0 & subpixel_mask];
    map_y = mapY[_y0 & subpixel_mask];
    
    mask_map = mask & map_x.v0 & map_y.v0;
    if (mask_map != 0) {
        if (pixel->depth > z) {
            pixel->depth = z;
            pixel->color = color[queues[forward_key | mask_map] & 7];
        }
    }
    
    ...
}


map_x4 = mapX4[_x0 & subpixel_mask];
map_y4 = mapY4[_y0 & subpixel_mask];
map_x = mapX + (_x0 & subpixel_mask) << 3;
map_y = mapY + (_y0 & subpixel_mask) << 3;
for (y4 = 0..cell4_h) {
    for (x4 = 0..cell4_w) {
        if (pixel4->depth <= z4) continue;
        mask_map4 = mask4 & (map_x4 >> x4) & (map_y4 >> y4);
        queue = queues[forward_key | mask_map4];
        for (; queue != 0; queue >>= 4) {
            int octant = (int)(queue & 7);
            var _node = node4[octant];
            int mask = (_node >> 24) & 0xFF;
            mask_map = mask & (map_x[octant] >> x4) & (map_y[octant] >> x4);
            if (mask_map != 0) {
                pixel->depth = z;
                color = colors + ((_node & 0xFFFFFF) << 3);
                pixel->color = color[queues[forward_key | mask_map] & 7];
                goto drawn;
            }
        }
        drawn:;
    }
}


*/

namespace dairin0d.Rendering.Octree {
    class Splatter {
        public struct Delta {
            public int x0, y0;
            public int x1, y1;
            public int z;
            public int pad0; // just for power-of-2 size
            public int x01, y01;
        }
        public Delta[] deltas = new Delta[8 * 32];

        public struct StackEntry {
            public Color32 color;
            public int node;
            public int level;
            public int last;
            public int dx0, x0, px0, x0b;
            public int dy0, y0, py0, y0b;
            public int dx1, x1, px1, x1b;
            public int dy1, y1, py1, y1b;
            public int dz, z, z0b, z1b;
            public int pw, ph;
        }
        public StackEntry[] stack = new StackEntry[8 * 32];

        public static int MaxLevel = 0;

        public static bool UseLast = true;
        public static int StopAt = 0;

        public static int RenderAlg = 0;

        public static int PixelCount = 0;
        public static int QuadCount = 0;
        public static int OccludedCount = 0;
        public static int CulledCount = 0;
        public static int NodeCount = 0;

        struct Map4 {
            public int m00, m10, m01, m11; // mXY
        }
        static Map4 baked_map4;
        public static bool UseMap4 = false;

        public static int MapShift = 5;
        public static bool UseMap = false;

        public struct RayDelta {
            public int x, y, z;
        }
        public static RayDelta[] ray_deltas = new RayDelta[8 * 32];

        public struct RayStack {
            public int level;
            public bool draw;
            public int node;
            public int offset;
            public uint queue;
            public int x, y, z;
        }
        public static RayStack[] ray_stack = new RayStack[8 * 32];

        public static int ray_shift = 16;
        public static int map_shift = 3;

        public static int[] ray_map = new int[128 * 128]; // for testing effects of different sizes

        public static int ComplexityMax = 0;
        public static int ComplexitySum = 0;
        public static int ComplexityCnt = 0;

        public static bool UseRaycast = false;

        public static bool CountRaycastNodes = false;
        public static HashSet<int> RaycastNodes = new HashSet<int>();

        public static bool FlatMode = false;

        public static int OverrideKey = -1;

        public void UpdateWidgets(List<Widget<string>> infoWidgets, List<Widget<float>> sliderWidgets, List<Widget<bool>> toggleWidgets) {
            if ((RenderAlg == 3) | (RenderAlg == 4)) {
                infoWidgets.Add(new Widget<string>($"ComplexityMax={ComplexityMax}"));

                float avg = ComplexitySum;
                if (ComplexityCnt > 0) avg /= ComplexityCnt;
                infoWidgets.Add(new Widget<string>($"ComplexityAvg={avg}"));
            } else {
                infoWidgets.Add(new Widget<string>($"PixelCount={PixelCount}"));
                infoWidgets.Add(new Widget<string>($"QuadCount={QuadCount}"));
                infoWidgets.Add(new Widget<string>($"CulledCount={CulledCount}"));
                infoWidgets.Add(new Widget<string>($"OccludedCount={OccludedCount}"));
                infoWidgets.Add(new Widget<string>($"NodeCount={NodeCount}"));
            }

            sliderWidgets.Add(new Widget<float>("Level", () => MaxLevel, (value) => { MaxLevel = (int)value; }, 0, 16));
            sliderWidgets.Add(new Widget<float>("Stop At", () => StopAt, (value) => { StopAt = (int)value; }, 0, 8));
            sliderWidgets.Add(new Widget<float>("Renderer", () => RenderAlg, (value) => { RenderAlg = (int)value; }, 0, 8));
            sliderWidgets.Add(new Widget<float>("MapShift", () => MapShift, (value) => { MapShift = (int)value; }, 3, 7));
            sliderWidgets.Add(new Widget<float>("OverrideKey", () => OverrideKey, (value) => { OverrideKey = (int)value; }, -1, 7));

            toggleWidgets.Add(new Widget<bool>("Flat Mode", () => FlatMode, (value) => { FlatMode = value; }));
            toggleWidgets.Add(new Widget<bool>($"RC={RaycastNodes.Count}",
                () => CountRaycastNodes, (value) => { CountRaycastNodes = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Raycast", () => UseRaycast, (value) => { UseRaycast = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Last", () => UseLast, (value) => { UseLast = value; }));
        }

        public unsafe void RenderObjects(Buffer buffer, int subpixel_shift, IEnumerable<(Matrix4x4, RawOctree)> instances) {
            PixelCount = 0;
            QuadCount = 0;
            CulledCount = 0;
            OccludedCount = 0;
            NodeCount = 0;

            ComplexityMax = 0;
            ComplexitySum = 0;
            ComplexityCnt = 0;

            if (CountRaycastNodes) RaycastNodes.Clear();

            int w = buffer.Width, h = buffer.Height, tile_shift = buffer.TileShift;
            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (uint* queues = OctantOrder.Queues)
            fixed (Delta* deltas = this.deltas)
            fixed (StackEntry* stack = this.stack)
            fixed (RayDelta* ray_deltas = Splatter.ray_deltas)
            fixed (RayStack* ray_stack = Splatter.ray_stack)
            fixed (int* map = ray_map) {
                foreach (var (mvp_matrix, octree) in instances) {
                    int node = octree.root_node;
                    var color = octree.root_color;
                    fixed (int* nodes = octree.nodes)
                    fixed (Color32* colors = octree.colors) {
                        Render(buf, w, h, tile_shift, node, color, nodes, colors,
                            mvp_matrix, subpixel_shift, queues, deltas, stack,
                            ray_deltas, ray_stack, map);
                    }
                }
            }

            CountRaycastNodes = false;
        }

        unsafe static Delta CalcBoundsAndDeltas(ref Matrix4x4 matrix, int subpixel_shift, Delta* deltas) {
            // float Xx=matrix.m00, Yx=matrix.m01, Zx=matrix.m02, Tx=matrix.m03;
            // float Xy=matrix.m10, Yy=matrix.m11, Zy=matrix.m12, Ty=matrix.m13;
            // float Xz=matrix.m20, Yz=matrix.m21, Zz=matrix.m22, Tz=matrix.m23;

            var center = new Vector3 { x = matrix.m03, y = matrix.m13, z = matrix.m23 };
            var extents = new Vector3 {
                x = (matrix.m00 < 0f ? -matrix.m00 : matrix.m00) +
                (matrix.m01 < 0f ? -matrix.m01 : matrix.m01) +
                (matrix.m02 < 0f ? -matrix.m02 : matrix.m02),
                y = (matrix.m10 < 0f ? -matrix.m10 : matrix.m10) +
                (matrix.m11 < 0f ? -matrix.m11 : matrix.m11) +
                (matrix.m12 < 0f ? -matrix.m12 : matrix.m12),
                z = (matrix.m20 < 0f ? -matrix.m20 : matrix.m20) +
                (matrix.m21 < 0f ? -matrix.m21 : matrix.m21) +
                (matrix.m22 < 0f ? -matrix.m22 : matrix.m22),
            };

            int pixel_size = 1 << subpixel_shift;
            int half_pixel = pixel_size >> 1;

            var root = default(Delta);
            root.x0 = (int)((center.x - extents.x) * pixel_size + 0.5f) + half_pixel;
            root.y0 = (int)((center.y - extents.y) * pixel_size + 0.5f) + half_pixel;
            root.x1 = (int)((center.x + extents.x) * pixel_size + 0.5f) + half_pixel;
            root.y1 = (int)((center.y + extents.y) * pixel_size + 0.5f) + half_pixel;
            root.z = (int)((center.z - extents.z) + 0.5f);

            {
                baked_map4 = default;

                int ray_size = 1 << ray_shift, ray_half = ray_size >> 1, ray_max = ray_size - ray_half;

                int map_size = 1 << MapShift;
                float map_offset = 1f / map_size; // combined effect of (margin - margin*0.5)
                float map_scale = (map_size - 4f) / map_size;

                int octant = 0;
                for (int subZ = -1; subZ <= 1; subZ += 2) {
                    for (int subY = -1; subY <= 1; subY += 2) {
                        for (int subX = -1; subX <= 1; subX += 2) {
                            var delta = deltas + octant;
                            float x = center.x + matrix.m00 * subX + matrix.m01 * subY + matrix.m02 * subZ;
                            float y = center.y + matrix.m10 * subX + matrix.m11 * subY + matrix.m12 * subZ;
                            float z = center.z + matrix.m20 * subX + matrix.m21 * subY + matrix.m22 * subZ;

                            if (x < center.x) {
                                if (y < center.y) {
                                    baked_map4.m00 |= (1 << octant);
                                } else {
                                    baked_map4.m01 |= (1 << octant);
                                }
                            } else {
                                if (y < center.y) {
                                    baked_map4.m10 |= (1 << octant);
                                } else {
                                    baked_map4.m11 |= (1 << octant);
                                }
                            }

                            int ix = (int)(x * pixel_size + 0.5f) + half_pixel;
                            int iy = (int)(y * pixel_size + 0.5f) + half_pixel;
                            int iz = (int)(z + 0.5f);

                            float uv_x = ((x - center.x) / extents.x + 1f) * 0.25f;
                            float uv_y = ((y - center.y) / extents.y + 1f) * 0.25f;
                            if (UseMap) {
                                uv_x = uv_x * map_scale + map_offset;
                                uv_y = uv_y * map_scale + map_offset;
                            }
                            ray_deltas[octant].x = Mathf.Clamp((int)(uv_x * ray_size), 0, ray_max);
                            ray_deltas[octant].y = Mathf.Clamp((int)(uv_y * ray_size), 0, ray_max);
                            ray_deltas[octant].z = Mathf.Max(iz - root.z, 0) >> 1;

                            delta->x0 = ix - root.x0; if (delta->x0 < 0) delta->x0 = 0;
                            delta->y0 = iy - root.y0; if (delta->y0 < 0) delta->y0 = 0;
                            delta->x1 = root.x1 - ix; if (delta->x1 < 0) delta->x1 = 0;
                            delta->y1 = root.y1 - iy; if (delta->y1 < 0) delta->y1 = 0;
                            delta->z = iz - root.z; if (delta->z < 0) delta->z = 0;
                            delta->x0 = delta->x0 >> 1;
                            delta->x1 = delta->x1 >> 1;
                            delta->y0 = delta->y0 >> 1;
                            delta->y1 = delta->y1 >> 1;
                            delta->z = delta->z >> 1;
                            delta->x01 = delta->x0 - delta->x1;
                            delta->y01 = delta->y0 - delta->y1;
                            ++octant;

                            if (FlatMode) {
                                delta->z = 0;
                                ray_deltas[octant].z = 0;
                            }
                        }
                    }
                }
            }

            for (int level = 0; level < 16; ++level) {
                var delta = deltas + (level << 3);
                var subdelta = delta + 8;
                for (int octant = 0; octant < 8; ++octant, ++delta, ++subdelta) {
                    subdelta->x0 = delta->x0 >> 1;
                    subdelta->x1 = delta->x1 >> 1;
                    subdelta->y0 = delta->y0 >> 1;
                    subdelta->y1 = delta->y1 >> 1;
                    subdelta->z = delta->z >> 1;
                    subdelta->x01 = subdelta->x0 - subdelta->x1;
                    subdelta->y01 = subdelta->y0 - subdelta->y1;
                }
            }

            if (UseMap) RasterizeMap(ref matrix);

            for (int i = 0; i < ray_stack.Length; ++i) {
                ray_stack[i].level = i;
            }

            return root;
        }

        static void RasterizeMap(ref Matrix4x4 matrix) {
            int buf_shift = MapShift;
            int w = 1 << MapShift;
            int h = 1 << MapShift;

            if ((w <= 4) | (h <= 4)) return;

            int subpixel_shift = 8;
            int pixel_size = (1 << subpixel_shift), half_pixel = pixel_size >> 1;

            var extents = new Vector2 {
                x = (matrix.m00 < 0f ? -matrix.m00 : matrix.m00) +
                (matrix.m01 < 0f ? -matrix.m01 : matrix.m01) +
                (matrix.m02 < 0f ? -matrix.m02 : matrix.m02),
                y = (matrix.m10 < 0f ? -matrix.m10 : matrix.m10) +
                (matrix.m11 < 0f ? -matrix.m11 : matrix.m11) +
                (matrix.m12 < 0f ? -matrix.m12 : matrix.m12),
            };

            int margin = 2;
            float scale_x = pixel_size * (w * 0.5f - margin) / extents.x;
            float scale_y = pixel_size * (h * 0.5f - margin) / extents.y;

            int Xx = (int)(matrix.m00 * scale_x), Yx = (int)(matrix.m01 * scale_x), Zx = (int)(matrix.m02 * scale_x), Tx = (int)((w * 0.5f) * pixel_size);
            int Xy = (int)(matrix.m10 * scale_y), Yy = (int)(matrix.m11 * scale_y), Zy = (int)(matrix.m12 * scale_y), Ty = (int)((h * 0.5f) * pixel_size);

            int extents_x = (Xx < 0 ? -Xx : Xx) + (Yx < 0 ? -Yx : Yx) + (Zx < 0 ? -Zx : Zx);
            int extents_y = (Xy < 0 ? -Xy : Xy) + (Yy < 0 ? -Yy : Yy) + (Zy < 0 ? -Zy : Zy);
            extents_x >>= 1;
            extents_y >>= 1;

            var nX = (new Vector2(-Xy, Xx));
            if (nX.x < 0) nX.x = -nX.x; if (nX.y < 0) nX.y = -nX.y;
            var nY = (new Vector2(-Yy, Yx));
            if (nY.x < 0) nY.x = -nY.x; if (nY.y < 0) nY.y = -nY.y;
            var nZ = (new Vector2(-Zy, Zx));
            if (nZ.x < 0) nZ.x = -nZ.x; if (nZ.y < 0) nZ.y = -nZ.y;

            Tx -= half_pixel;
            Ty -= half_pixel;

            int dotXM = Mathf.Max(Mathf.Abs(Xx * (Yy + Zy) - Xy * (Yx + Zx)), Mathf.Abs(Xx * (Yy - Zy) - Xy * (Yx - Zx)));
            int dotYM = Mathf.Max(Mathf.Abs(Yx * (Xy + Zy) - Yy * (Xx + Zx)), Mathf.Abs(Yx * (Xy - Zy) - Yy * (Xx - Zx)));
            int dotZM = Mathf.Max(Mathf.Abs(Zx * (Xy + Yy) - Zy * (Xx + Yx)), Mathf.Abs(Zx * (Xy - Yy) - Zy * (Xx - Yx)));

            dotXM >>= 1;
            dotYM >>= 1;
            dotZM >>= 1;

            dotXM += (int)((nX.x + nX.y) * half_pixel + 0.5f);
            dotYM += (int)((nY.x + nY.y) * half_pixel + 0.5f);
            dotZM += (int)((nZ.x + nZ.y) * half_pixel + 0.5f);

            int dotXdx = -Xy << subpixel_shift;
            int dotXdy = Xx << subpixel_shift;
            int dotYdx = -Yy << subpixel_shift;
            int dotYdy = Yx << subpixel_shift;
            int dotZdx = -Zy << subpixel_shift;
            int dotZdy = Zx << subpixel_shift;

            for (int i = 0; i < ray_map.Length; ++i) {
                ray_map[i] = 0;
            }

            int octant = 0;
            for (int subZ = -1; subZ <= 1; subZ += 2) {
                for (int subY = -1; subY <= 1; subY += 2) {
                    for (int subX = -1; subX <= 1; subX += 2) {
                        int dx = (Xx * subX + Yx * subY + Zx * subZ) >> 1;
                        int dy = (Xy * subX + Yy * subY + Zy * subZ) >> 1;
                        int cx = Tx + dx;
                        int cy = Ty + dy;

                        int xmin = Mathf.Max(((cx - extents_x) >> subpixel_shift) - 2, margin);
                        int ymin = Mathf.Max(((cy - extents_y) >> subpixel_shift) - 2, margin);
                        int xmax = Mathf.Min(((cx + extents_x) >> subpixel_shift) + 2, w - margin);
                        int ymax = Mathf.Min(((cy + extents_y) >> subpixel_shift) + 2, h - margin);

                        int offset_x = (xmin << subpixel_shift) - cx;
                        int offset_y = (ymin << subpixel_shift) - cy;

                        int dotXr = Xx * offset_y - Xy * offset_x;
                        int dotYr = Yx * offset_y - Yy * offset_x;
                        int dotZr = Zx * offset_y - Zy * offset_x;

                        int mask = 1 << octant;

                        for (int iy = ymin; iy < ymax; ++iy) {
                            int ixy0 = (iy << buf_shift) + xmin;
                            int ixy1 = (iy << buf_shift) + xmax;
                            int dotX = dotXr;
                            int dotY = dotYr;
                            int dotZ = dotZr;
                            for (int ixy = ixy0; ixy < ixy1; ++ixy) {
                                //if ((dotX<=dotXM)&(-dotX<=dotXM) & (dotY<=dotYM)&(-dotY<=dotYM) & (dotZ<=dotZM)&(-dotZ<=dotZM)) { // a bit slower
                                if (((dotX ^ (dotX >> 31)) <= dotXM) & ((dotY ^ (dotY >> 31)) <= dotYM) & ((dotZ ^ (dotZ >> 31)) <= dotZM)) { // a bit faster
                                    ray_map[ixy] |= mask;
                                }
                                dotX += dotXdx;
                                dotY += dotYdx;
                                dotZ += dotZdx;
                            }
                            dotXr += dotXdy;
                            dotYr += dotYdy;
                            dotZr += dotZdy;
                        }

                        ++octant;
                    }
                }
            }
        }

        public unsafe static void Render(Buffer.DataItem* buf, int w, int h, int tile_shift,
            int node, Color32 color, int* nodes, Color32* colors,
            Matrix4x4 matrix, int subpixel_shift,
            uint* queues, Delta* deltas, StackEntry* stack,
            RayDelta* ray_deltas, RayStack* ray_stack, int* map) {
            UseMap = (RenderAlg == 4) | (RenderAlg == 5);

            var root = CalcBoundsAndDeltas(ref matrix, subpixel_shift, deltas);

            if (root.x1 < 0) return;
            if (root.y1 < 0) return;
            if (root.x0 >= (w << subpixel_shift)) return;
            if (root.y0 >= (h << subpixel_shift)) return;

            int forward_key = OctantOrder.Key(ref matrix);

            int tile_size = 1 << tile_shift;
            int tile_mask = tile_size - 1;
            int tnx = (w + tile_mask) >> tile_shift;
            int tny = (h + tile_mask) >> tile_shift;

            int combined_shift = subpixel_shift + tile_shift;
            int combined_mask = (1 << combined_shift) - 1;
            int tx0 = root.x0 >> combined_shift;
            int ty0 = root.y0 >> combined_shift;
            int tx1 = (root.x1 + combined_mask) >> combined_shift;
            int ty1 = (root.y1 + combined_mask) >> combined_shift;
            if (tx0 < 0) tx0 = 0;
            if (ty0 < 0) ty0 = 0;
            if (tx1 > tnx) tx1 = tnx;
            if (ty1 > tny) ty1 = tny;

            for (int ty = ty0; ty < ty1; ++ty) {
                int iy = ty << tile_shift;
                int th = h - iy; if (th > tile_size) th = tile_size;
                for (int tx = tx0; tx < tx1; ++tx) {
                    int ix = tx << tile_shift;
                    int tw = w - ix; if (tw > tile_size) tw = tile_size;

                    var tile = buf + ((tx + ty * tnx) << (tile_shift + tile_shift));

                    stack->node = node;
                    stack->level = 0;
                    stack->last = 0;
                    stack->x0 = root.x0 - (ix << subpixel_shift);
                    stack->y0 = root.y0 - (iy << subpixel_shift);
                    stack->x1 = root.x1 - (ix << subpixel_shift);
                    stack->y1 = root.y1 - (iy << subpixel_shift);
                    stack->z = root.z;
                    stack->color = color;

                    if (RenderAlg == 4) {
                        Raycast(tile, tw, th, tile_shift, nodes, colors, forward_key, subpixel_shift, queues,
                            stack->x0, stack->y0, stack->x1, stack->y1, stack->z, stack->node,
                            ray_deltas, ray_stack, map);
                    } else if (RenderAlg == 3) {
                        Raycast(tile, tw, th, tile_shift, nodes, colors, forward_key, subpixel_shift, queues,
                            stack->x0, stack->y0, stack->x1, stack->y1, stack->z, stack->node,
                            ray_deltas, ray_stack, map);
                    } else if (RenderAlg == 2) {
                        UseMap4 = true;
                        Render1(tile, tw, th, tile_shift, nodes, colors, forward_key, subpixel_shift, queues, deltas, stack);
                    } else if (RenderAlg == 1) {
                        UseMap4 = false;
                        Render1(tile, tw, th, tile_shift, nodes, colors, forward_key, subpixel_shift, queues, deltas, stack);
                    } else {
                        Render0(tile, tw, th, tile_shift, nodes, colors, forward_key, subpixel_shift, queues, deltas, stack,
                            ray_deltas, ray_stack, map);
                    }
                }
            }
        }

        unsafe static void Render0(Buffer.DataItem* tile, int w, int h, int tile_shift,
            int* nodes, Color32* colors, int forward_key, int subpixel_shift,
            uint* queues, Delta* deltas, StackEntry* stack,
            RayDelta* ray_deltas, RayStack* ray_stack, int* map) {
            // We need to put nodes on stack in back-to-front order
            int reverse_key = forward_key ^ 0b11100000000;

            if ((OverrideKey >= 0) & (OverrideKey < 8)) {
                reverse_key = forward_key & ~0b11100000000;
                reverse_key |= OverrideKey << 8;
            }

            int tile_size = 1 << tile_shift;

            int max_level = Mathf.Clamp(MaxLevel, 0, 32);

            int stack_top = 0;

            int row = 1 << tile_shift;

            uint queue = 0;
            var node = nodes;
            var color = colors;

            int ray_max_level = CalcMaxLevel(stack->x0, stack->y0, stack->x1, stack->y1, subpixel_shift);

            do {
                ++NodeCount;

                if (CountRaycastNodes) RaycastNodes.Add(stack->node);

                var current = stack;
                --stack; --stack_top;

                // Calculate pixel rect
                int px0 = current->x0 >> subpixel_shift;
                if (px0 < 0) px0 = 0;
                // px0 = px0 - ((px0 - 0) & ((px0 - 0) >> 31)); // branchless max
                int py0 = current->y0 >> subpixel_shift;
                // if (py0 < 0) py0 = 0;
                if (py0 < current->last) py0 = current->last;
                // // py0 = py0 - ((py0 - 0) & ((py0 - 0) >> 31)); // branchless max
                // py0 = py0 - ((py0 - current->last) & ((py0 - current->last) >> 31)); // branchless max
                int px1 = current->x1 >> subpixel_shift;
                if (px1 > w) px1 = w;
                // px1 = px1 + ((w - px1) & ((w - px1) >> 31)); // branchless min
                int py1 = current->y1 >> subpixel_shift;
                if (py1 > h) py1 = h;
                // py1 = py1 + ((h - py1) & ((h - py1) >> 31)); // branchless min

                // Overlap/contribution test
                if ((px0 >= px1) | (py0 >= py1)) {
                    ++CulledCount;
                    continue;
                }

                int pw = px1 - px0, ph = py1 - py0;

                if ((pw | ph) == 1) {
                    ++PixelCount;

                    var tile_x = tile + px0 + (py0 << tile_shift);
                    if (tile_x->depth > current->z) {
                        tile_x->depth = current->z;
                        tile_x->color = current->color;
                        // int a = tile_x->color.a + 32;
                        // tile_x->color.a = (byte)(a < 255 ? a : 255);
                        // tile_x->color.g = 0;
                        // tile_x->color.b = 255;
                    }
                    continue;
                } else if ((current->level >= max_level) | ((pw <= StopAt) & (ph <= StopAt))) {
                    // For test
                    ++QuadCount;

                    if (UseRaycast) {
                        Raycast(tile, w, h, tile_shift,
                        nodes, colors, forward_key, subpixel_shift,
                        queues,
                        current->x0,
                        current->y0,
                        current->x1,
                        current->y1,
                        current->z,
                        current->node,
                        ray_deltas, ray_stack, map,
                        ray_max_level, current->level);
                        continue;
                    }

                    var tile_y = tile + (py0 << tile_shift);
                    var tile_y_end = tile + (py1 << tile_shift);
                    for (; tile_y != tile_y_end; tile_y += tile_size) {
                        var tile_x = tile_y + px0;
                        var tile_x_end = tile_y + px1;
                        for (; tile_x != tile_x_end; ++tile_x) {
                            if (tile_x->depth > current->z) {
                                tile_x->depth = current->z;
                                tile_x->color = current->color;
                                // int a = tile_x->color.a + 32;
                                // tile_x->color.a = (byte)(a < 255 ? a : 255);
                            }
                        }
                    }
                    continue;
                }

                // Occlusion test
                {
                    int drow = row - (px1 - px0);
                    int cmp_z = current->z;
                    var pixel = tile + (px0 + (py0 << tile_shift));
                    var endpixel = pixel + (px1 - px0);
                    var lastpixel = endpixel + ((py1 - py0) << tile_shift);

                occlusion_start:;
                    if (cmp_z < pixel->depth) goto occlusion_passed;
                    ++pixel;
                    if (pixel != endpixel) goto occlusion_start;
                    if (pixel == lastpixel) {
                        ++OccludedCount;
                        continue;
                    }
                    pixel += drow;
                    endpixel += row;
                    goto occlusion_start;
                occlusion_passed:
                    if (UseLast) current->last = ((int)(pixel - tile)) >> tile_shift;
                }

                // Add subnodes to the stack
                int mask = (current->node >> 24) & 0xFF;
                bool is_leaf = (mask == 0);
                if (is_leaf) {
                    queue = queues[reverse_key | 255];
                } else {
                    queue = queues[reverse_key | mask];
                    int offset = (current->node & 0xFFFFFF) << 3;
                    node = nodes + offset;
                    color = colors + offset;
                }
                int x0 = current->x0, y0 = current->y0;
                int x1 = current->x1, y1 = current->y1;
                int z = current->z, last = current->last;
                int level = current->level + 1;
                var level_deltas = deltas + (current->level << 3);
                for (; queue != 0; queue >>= 4) {
                    int octant = (int)(queue & 7);
                    ++stack; ++stack_top;
                    if (is_leaf) {
                        stack->node = 0;
                        stack->color = current->color;
                    } else {
                        stack->node = node[octant];
                        stack->color = color[octant];
                    }
                    stack->level = level;
                    stack->last = last;
                    var delta = level_deltas + octant;
                    stack->x0 = x0 + delta->x0;
                    stack->y0 = y0 + delta->y0;
                    stack->x1 = x1 - delta->x1;
                    stack->y1 = y1 - delta->y1;
                    stack->z = z + delta->z;
                }
            } while (stack_top >= 0);
        }

        unsafe static void Render1(Buffer.DataItem* tile, int w, int h, int tile_shift,
            int* nodes, Color32* colors, int forward_key, int subpixel_shift,
            uint* queues, Delta* deltas, StackEntry* stack) {
            stack->px0 = stack->x0 >> subpixel_shift;
            if (stack->px0 < 0) stack->px0 = 0;
            stack->py0 = stack->y0 >> subpixel_shift;
            if (stack->py0 < 0) stack->py0 = 0;
            stack->px1 = stack->x1 >> subpixel_shift;
            if (stack->px1 > w) stack->px1 = w;
            stack->py1 = stack->y1 >> subpixel_shift;
            if (stack->py1 > h) stack->py1 = h;

            if ((stack->px0 >= stack->px1) | (stack->py0 >= stack->py1)) return;

            stack->pw = stack->px1 - stack->px0;
            stack->ph = stack->py1 - stack->py0;

            int pixel_size = 1 << subpixel_shift;
            int half_pixel = pixel_size >> 1;

            // We need to put nodes on stack in back-to-front order
            int reverse_key = forward_key ^ 0b11100000000;

            int tile_size = 1 << tile_shift;

            // int max_level = 0;
            // {
            //     int sz = Mathf.Max(stack->x1 - stack->x0, stack->y1 - stack->y0);
            //     int szm = 1 << subpixel_shift;
            //     while ((sz >> max_level) > szm) ++max_level;
            // }

            int stack_top = 0;

            int row = 1 << tile_shift;

            uint queue = 0;
            var node = nodes;
            var color = colors;

            var map4 = baked_map4;
            int mask_map = 0;
            bool use_map4 = UseMap4;

            while (stack_top >= 0) {
                ++NodeCount;

                if (CountRaycastNodes) RaycastNodes.Add(stack->node);

                var current = stack;
                --stack; --stack_top;

                if ((current->pw | current->ph) == 1) {
                    ++PixelCount;
                    var tile_x = tile + current->px0 + (current->py0 << tile_shift);
                    if (tile_x->depth > current->z) {
                        tile_x->depth = current->z;
                        tile_x->color = current->color;
                    }
                    continue;
                }

                // Occlusion test
                {
                    int pw = current->px1 - current->px0;
                    int ph = current->py1 - current->py0;

                    int drow = row - pw;
                    int cmp_z = current->z;
                    var pixel = tile + (current->px0 + (current->py0 << tile_shift));
                    var endpixel = pixel + pw;
                    var lastpixel = endpixel + (ph << tile_shift);

                occlusion_start:;
                    if (cmp_z < pixel->depth) goto occlusion_passed;
                    ++pixel;
                    if (pixel != endpixel) goto occlusion_start;
                    if (pixel == lastpixel) {
                        ++OccludedCount;
                        continue;
                    }
                    pixel += drow;
                    endpixel += row;
                    goto occlusion_start;
                occlusion_passed:
                    if (UseLast) current->py0 = ((int)(pixel - tile)) >> tile_shift;
                }

                if ((current->pw <= 2) & (current->ph <= 2)) {
                    if (use_map4) {
                        ++QuadCount;

                        int x = current->x0 >> subpixel_shift;
                        int y = current->y0 >> subpixel_shift;

                        if ((x < 0) | (x >= w - 2)) continue;
                        if ((y < 0) | (y >= h - 2)) continue;

                        int mask = (current->node >> 24) & 0xFF;
                        bool is_leaf = (mask == 0);
                        if (is_leaf) {
                            mask = 255;
                        } else {
                            int offset = (current->node & 0xFFFFFF) << 3;
                            color = colors + offset;
                        }

                        int z = current->z;
                        var pixel = tile + (x + (y << tile_shift));

                        if (current->pw == 1) {
                            mask_map = mask & (map4.m00 | map4.m10);
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                            pixel += row;
                            mask_map = mask & (map4.m01 | map4.m11);
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                        } else if (current->ph == 1) {
                            mask_map = mask & (map4.m00 | map4.m01);
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                            ++pixel;
                            mask_map = mask & (map4.m10 | map4.m11);
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                        } else {
                            mask_map = mask & map4.m00;
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                            ++pixel;
                            mask_map = mask & map4.m10;
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                            pixel += row - 1;
                            mask_map = mask & map4.m01;
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                            ++pixel;
                            mask_map = mask & map4.m11;
                            if (mask_map != 0) {
                                if (pixel->depth > z) {
                                    pixel->depth = z;
                                    if (is_leaf) {
                                        pixel->color = current->color;
                                    } else {
                                        pixel->color = color[queues[forward_key | mask_map] & 7];
                                    }
                                }
                            }
                        }

                        continue;
                    } else {
                        ++QuadCount;

                        int mask = (current->node >> 24) & 0xFF;
                        bool is_leaf = (mask == 0);
                        if (is_leaf) {
                            queue = queues[forward_key | 255];
                        } else {
                            queue = queues[forward_key | mask];
                            int offset = (current->node & 0xFFFFFF) << 3;
                            color = colors + offset;
                        }

                        int x0 = current->x0, y0 = current->y0;
                        int x1 = current->x1, y1 = current->y1;
                        int x = current->x0 + current->x1 - pixel_size;
                        int y = current->y0 + current->y1 - pixel_size;
                        int subpixel_shift1 = subpixel_shift + 1;
                        int z = current->z, ymin = current->py0;
                        var level_deltas = deltas + (current->level << 3);

                        for (; queue != 0; queue >>= 4) {
                            int octant = (int)(queue & 7);

                            var delta = level_deltas + octant;

                            int px = (x + delta->x01) >> subpixel_shift1;
                            int py = (y + delta->y01) >> subpixel_shift1;
                            if ((px < 0) | (py < ymin) | (px >= w) | (py >= h)) continue;
                            int pz = z + delta->z;

                            var tile_x = tile + px + (py << tile_shift);
                            if (tile_x->depth > pz) {
                                tile_x->depth = pz;
                                if (is_leaf) {
                                    tile_x->color = current->color;
                                } else {
                                    tile_x->color = color[octant];
                                }
                            }
                        }

                        continue;
                    }
                }

                // Add subnodes to the stack
                {
                    int mask = (current->node >> 24) & 0xFF;
                    bool is_leaf = (mask == 0);
                    if (is_leaf) {
                        queue = queues[reverse_key | 255];
                    } else {
                        queue = queues[reverse_key | mask];
                        int offset = (current->node & 0xFFFFFF) << 3;
                        node = nodes + offset;
                        color = colors + offset;
                    }

                    int x0 = current->x0, y0 = current->y0;
                    int x1 = current->x1, y1 = current->y1;
                    int z = current->z, ymin = current->py0;
                    int level = current->level + 1;
                    var level_deltas = deltas + (current->level << 3);

                    for (; queue != 0; queue >>= 4) {
                        int octant = (int)(queue & 7);

                        var delta = level_deltas + octant;

                        int _x0 = x0 + delta->x0, _px0 = _x0 >> subpixel_shift;
                        int _x1 = x1 - delta->x1, _px1 = _x1 >> subpixel_shift;
                        int _pw = _px1 - _px0;
                        if (_px0 < 0) _px0 = 0;
                        if (_px1 > w) _px1 = w;

                        int _y0 = y0 + delta->y0, _py0 = _y0 >> subpixel_shift;
                        int _y1 = y1 - delta->y1, _py1 = _y1 >> subpixel_shift;
                        int _ph = _py1 - _py0;
                        if (_py0 < ymin) _py0 = ymin;
                        if (_py1 > h) _py1 = h;

                        // Overlap/contribution test
                        if ((_px0 >= _px1) | (_py0 >= _py1)) {
                            ++CulledCount;
                            continue;
                        }

                        ++stack; ++stack_top;

                        stack->x0 = _x0;
                        stack->y0 = _y0;
                        stack->x1 = _x1;
                        stack->y1 = _y1;
                        stack->px0 = _px0;
                        stack->py0 = _py0;
                        stack->px1 = _px1;
                        stack->py1 = _py1;
                        stack->z = z + delta->z;

                        stack->pw = _pw;
                        stack->ph = _ph;

                        stack->level = level;
                        if (is_leaf) {
                            stack->node = 0;
                            stack->color = current->color;
                        } else {
                            stack->node = node[octant];
                            stack->color = color[octant];
                        }
                    }
                }
            }
        }

        static int CalcMaxLevel(int x0, int y0, int x1, int y1, int subpixel_shift) {
            int max_level = 0;
            {
                int sz = Mathf.Max(x1 - x0, y1 - y0);
                int szm = 1 << subpixel_shift;
                while ((sz >> max_level) > szm) ++max_level;
            }
            return max_level;
        }

        unsafe static void Raycast(Buffer.DataItem* tile, int w, int h, int tile_shift,
            int* nodes, Color32* colors, int forward_key, int subpixel_shift,
            uint* queues, int x0, int y0, int x1, int y1, int z, int node_start,
            RayDelta* ray_deltas, RayStack* ray_stack, int* map) {
            int max_level = CalcMaxLevel(x0, y0, x1, y1, subpixel_shift);
            max_level = Mathf.Max(Mathf.Min(max_level, MaxLevel - 1), 0);

            Raycast(tile, w, h, tile_shift,
            nodes, colors, forward_key, subpixel_shift,
            queues, x0, y0, x1, y1, z, node_start,
            ray_deltas, ray_stack, map, max_level);
        }

        unsafe static void Raycast(Buffer.DataItem* tile, int w, int h, int tile_shift,
            int* nodes, Color32* colors, int forward_key, int subpixel_shift,
            uint* queues, int x0, int y0, int x1, int y1, int z, int node_start,
            RayDelta* ray_deltas, RayStack* ray_stack, int* map, int max_level, int start_level = 0) {
            int px0 = x0 >> subpixel_shift; if (px0 < 0) px0 = 0;
            int py0 = y0 >> subpixel_shift; if (py0 < 0) py0 = 0;
            int px1 = x1 >> subpixel_shift; if (px1 > w) px1 = w;
            int py1 = y1 >> subpixel_shift; if (py1 > h) py1 = h;
            if ((px0 >= px1) | (py0 >= py1)) return;

            int pixel_size = 1 << subpixel_shift;
            int half_pixel = pixel_size >> 1;

            int row = 1 << tile_shift;
            int tile_size = 1 << tile_shift;

            int ray_size = 1 << ray_shift, ray_mask = ~(ray_size - 1);
            float x_scale = ray_size / (float)(x1 - x0);
            float y_scale = ray_size / (float)(y1 - y0);

            int map_size = 1 << MapShift;
            float map_scale = (map_size - 4f) / map_size;
            int ray_map_shift = ray_shift - MapShift;

            float r_offset = 0;
            if (UseMap) {
                x_scale *= map_scale;
                y_scale *= map_scale;
                r_offset = ray_size * (2f / map_size);
            }

            float rx0 = ((px0 << subpixel_shift) + half_pixel - x0) * x_scale + r_offset;
            float ry0 = ((py0 << subpixel_shift) + half_pixel - y0) * y_scale + r_offset;
            float rx1 = ((px1 << subpixel_shift) + half_pixel - x0) * x_scale + r_offset;
            float ry1 = ((py1 << subpixel_shift) + half_pixel - y0) * y_scale + r_offset;
            float rdx = pixel_size * x_scale;
            float rdy = pixel_size * y_scale;


            if (FlatMode) {
                for (int i = 0; i < Splatter.ray_deltas.Length; i++) {
                    Splatter.ray_deltas[i].z = 0;
                }
            }


            // for (int i = 0; i < ray_stack.Length; ++i) {
            //     ray_stack[i].level = i;
            //     ray_stack[i].draw = (i >= max_level);
            // }

            var deltas = ray_deltas;
            var stack0 = ray_stack + start_level;
            // fixed (RayDelta* deltas = ray_deltas)
            // fixed (RayStack* stack0 = ray_stack)
            // fixed (int* map = ray_map)
            {
                stack0->node = node_start;
                stack0->offset = (stack0->node & 0xFFFFFF) << 3;

                // root is not expected to be leaf
                int root_mask = (stack0->node >> 24) & 0xFF;
                var root_queue = queues[forward_key | root_mask];

                stack0->z = z;

                int octant = 0;
                int subx = 0, suby = 0, subz = 0;

                var tile_y = tile + (py0 << tile_shift);
                for (var ry = ry0; ry < ry1; ry += rdy, tile_y += row) {
                    stack0->y = (int)ry;

                    var tile_x = tile_y + px0;
                    for (var rx = rx0; rx < rx1; rx += rdx, ++tile_x) {
                        stack0->x = (int)rx;

                        int complexity = 0;

                        var depth = tile_x->depth;

                        if (stack0->z >= depth) goto skip;

                        // reset stack position and queue
                        var stack = stack0;

                        if (UseMap) {
                            int map_x = stack->x >> ray_map_shift;
                            int map_y = stack->y >> ray_map_shift;
                            int map_mask = map[map_x | (map_y << MapShift)];
                            stack->queue = queues[forward_key | (root_mask & map_mask)];

                            for (; ; ) {
                                ++complexity;

                                while (stack->queue == 0) {
                                    if (stack == stack0) goto skip;
                                    --stack;
                                }

                                octant = (int)(stack->queue & 7);
                                stack->queue >>= 4;

                                var delta = (deltas + octant);

                                subz = stack->z + (delta->z >> stack->level);
                                if (subz >= depth) goto skip;

                                //if (stack->draw) goto draw;
                                if (stack->level >= max_level) goto draw;

                                subx = (stack->x - delta->x) << 1;
                                suby = (stack->y - delta->y) << 1;
                                // subx & suby can still end up outside,
                                // possibly due to discrepancy between
                                // map rasterization and ray delta
                                if (((subx | suby) & ray_mask) != 0) continue;

                                map_x = subx >> ray_map_shift;
                                map_y = suby >> ray_map_shift;
                                map_mask = map[map_x | (map_y << MapShift)];
                                if (map_mask == 0) continue;

                                int node = *(nodes + stack->offset + octant);
                                if (CountRaycastNodes) RaycastNodes.Add(node);
                                int mask = (node >> 24) & 0xFF;
                                if (mask == 0) goto draw;

                                ++stack;
                                stack->node = node;
                                stack->offset = (node & 0xFFFFFF) << 3;
                                stack->queue = queues[forward_key | (mask & map_mask)];
                                stack->x = subx;
                                stack->y = suby;
                                stack->z = subz;
                            }
                        } else {
                            stack->queue = root_queue;

                            for (; ; ) {
                                ++complexity;

                                while (stack->queue == 0) {
                                    if (stack == stack0) goto skip;
                                    --stack;
                                }

                                octant = (int)(stack->queue & 7);
                                stack->queue >>= 4;

                                var delta = (deltas + octant);

                                subz = stack->z + (delta->z >> stack->level);
                                if (subz >= depth) goto skip;

                                subx = (stack->x - delta->x) << 1;
                                suby = (stack->y - delta->y) << 1;
                                if (((subx | suby) & ray_mask) != 0) continue;

                                //if (stack->draw) goto draw;
                                if (stack->level >= max_level) goto draw;

                                int node = *(nodes + stack->offset + octant);
                                if (CountRaycastNodes) RaycastNodes.Add(node);
                                int mask = (node >> 24) & 0xFF;
                                if (mask == 0) goto draw;

                                ++stack;
                                stack->node = node;
                                stack->offset = (node & 0xFFFFFF) << 3;
                                stack->queue = queues[forward_key | mask];
                                stack->x = subx;
                                stack->y = suby;
                                stack->z = subz;
                            }
                        }

                    draw:;
                        tile_x->depth = subz;
                        tile_x->color = *(colors + stack->offset + octant);

                    skip:;

                        if (complexity > ComplexityMax) ComplexityMax = complexity;
                        ComplexitySum += complexity;
                        ++ComplexityCnt;
                    }
                }
            }
        }
    }
}
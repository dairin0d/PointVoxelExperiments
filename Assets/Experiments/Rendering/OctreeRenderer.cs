﻿// MIT License
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
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

using dairin0d.Data.Points;
using dairin0d.Data.Voxels;

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
    public class OctreeRenderer : MonoBehaviour {
        public int vSyncCount = 0;
        public int targetFrameRate = 30;
        
        Camera cam;
        int cullingMask;
        CameraClearFlags clearFlags;
        
        Matrix4x4 vp_matrix;
        Plane[] frustum_planes;
        
        public int RenderSize = 0;
        public float TilePow = 0;
        Texture2D tex;
        Color32[] colors;
        Buffer buffer;
        
        public int subpixel_shift = 8;
        public int depth_resolution = 16;
        Splatter splatter;
        
        public Transform PointCloudsParent;
        public float voxel_scale = 1f;
        List<(float, Transform)> visible_objects = new List<(float, Transform)>(64);
        
        System.Diagnostics.Stopwatch stopwatch;
        float dt = 0;
        string info = "";
        
        public Vector2 recursive_start;
        public Vector2 recursive_delta0;
        public Vector2 recursive_delta1;
        public int recursive_iterations = 0;
        
        public Color32 octree_color = Color.green;
        RawOctree test_octree; // for test
        
        public string model_path = "";
        public float voxel_size = -1;
        RawOctree model_octree;
        
        public bool use_model = false;
        
        // ========== Unity events ========== //
        
        void Start() {
            if (!Application.isEditor) Screen.SetResolution(640, 480, false);
            
            stopwatch = new System.Diagnostics.Stopwatch();
            cam = GetComponent<Camera>();
            
            buffer = new Buffer();
            
            splatter = new Splatter();
            
            // int root_node = 255 << 24;
            int root_node = 0b10011001 << 24;
            test_octree = new RawOctree() {
                root_node = root_node,
                root_color = octree_color,
                nodes = new int[] {
                    // root_node, root_node, root_node, root_node,
                    // root_node, root_node, root_node, root_node,
                    root_node, 0, 0, root_node,
                    root_node, 0, 0, root_node,
                },
                colors = new Color32[] {
                    octree_color, octree_color, octree_color, octree_color,
                    octree_color, octree_color, octree_color, octree_color,
                },
            };
            
            model_octree = LoadPointCloud(model_path, voxel_size);
        }

        void Update() {
            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = targetFrameRate;
            ResizeDisplay();
        }
        
        void OnPreCull() {
            // cullingMask = cam.cullingMask;
            // clearFlags = cam.clearFlags;
            // cam.cullingMask = 0;
            // cam.clearFlags = CameraClearFlags.Nothing;
        }
        
        void OnPostRender() {
            // cam.cullingMask = cullingMask;
            // cam.clearFlags = clearFlags;
            RenderMain();
        }
        
        void OnGUI() {
            int x = 0, y = 0, bw = 160, lh = 20;
            
            GUI.DrawTexture(cam.pixelRect, tex, ScaleMode.StretchToFill, true);
            
            if (GUI.Button(new Rect(Screen.width-bw, 0, bw, lh), "Fullscreen")) {
                Screen.fullScreen = !Screen.fullScreen;
            }
            
            DrawBox(new Rect(0, Screen.height-lh*7, bw, Screen.height));
            
            x = 0;
            y = Screen.height - lh;
            {
                GUI.Label(new Rect(x, y, Screen.width, lh), $"{dt:0.000}");
                y -= lh;
                GUI.Label(new Rect(x, y, Screen.width, lh), info);
                y -= lh;
                
                if ((Splatter.RenderAlg == 3) | (Splatter.RenderAlg == 4)) {
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"ComplexityMax={Splatter.ComplexityMax}");
                    y -= lh;
                    float avg = Splatter.ComplexitySum;
                    if (Splatter.ComplexityCnt > 0) avg /= Splatter.ComplexityCnt;
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"ComplexityAvg={avg}");
                    y -= lh;
                } else {
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"PixelCount={Splatter.PixelCount}");
                    y -= lh;
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"QuadCount={Splatter.QuadCount}");
                    y -= lh;
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"CulledCount={Splatter.CulledCount}");
                    y -= lh;
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"OccludedCount={Splatter.OccludedCount}");
                    y -= lh;
                    GUI.Label(new Rect(x, y, Screen.width, lh), $"NodeCount={Splatter.NodeCount}");
                    y -= lh;
                }
            }
            
            DrawBox(new Rect(Screen.width - bw, Screen.height-lh*14, bw, Screen.height));
            
            x = Screen.width - bw;
            y = Screen.height - lh;
            {
                TilePow = GUI.HorizontalSlider(new Rect(x, y, bw, lh), TilePow, 0, 12);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"Tlie: {(int)TilePow}");
                y -= lh;
                
                Splatter.MaxLevel = GUI.HorizontalSlider(new Rect(x, y, bw, lh), Splatter.MaxLevel, 0, 16);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"Level: {(int)Splatter.MaxLevel}");
                y -= lh;
                
                Splatter.StopAt = (int)GUI.HorizontalSlider(new Rect(x, y, bw, lh), Splatter.StopAt, 0, 8);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"Stop At: {Splatter.StopAt}");
                y -= lh;
                
                Splatter.RenderAlg = (int)GUI.HorizontalSlider(new Rect(x, y, bw, lh), Splatter.RenderAlg, 0, 8);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"Renderer: {Splatter.RenderAlg}");
                y -= lh;
                
                Splatter.MapShift = (int)GUI.HorizontalSlider(new Rect(x, y, bw, lh), Splatter.MapShift, 3, 7);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"MapShift: {Splatter.MapShift}");
                y -= lh;
                
                subpixel_shift = (int)GUI.HorizontalSlider(new Rect(x, y, bw, lh), subpixel_shift, 0, 8);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"Subpixel: {subpixel_shift}");
                y -= lh;
                
                RenderSize = (int)GUI.HorizontalSlider(new Rect(x, y, bw, lh), RenderSize/16, 0, 640/16)*16;
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"RenderSize: {RenderSize}");
                y -= lh;
                
                Splatter.OverrideKey = (int)GUI.HorizontalSlider(new Rect(x, y, bw, lh), Splatter.OverrideKey, -1, 7);
                y -= lh;
                GUI.Label(new Rect(x, y, bw, lh), $"OverrideKey: {Splatter.OverrideKey}");
                y -= lh;
                
                Splatter.FlatMode = GUI.Toggle(new Rect(x, y, bw, lh), Splatter.FlatMode, "Flat Mode");
                y -= lh;
                
                Splatter.CountRaycastNodes = GUI.Toggle(new Rect(x, y, bw, lh), Splatter.CountRaycastNodes, $"RC={Splatter.RaycastNodes.Count}");
                y -= lh;
                
                Splatter.UseRaycast = GUI.Toggle(new Rect(x, y, bw, lh), Splatter.UseRaycast, "Use Raycast");
                y -= lh;
                
                Splatter.UseLast = GUI.Toggle(new Rect(x, y, bw, lh), Splatter.UseLast, "Use Last");
                y -= lh;
                
                use_model = GUI.Toggle(new Rect(x, y, bw, lh), use_model, "Use model");
                y -= lh;
            }
            
            void DrawBox(Rect rect, int repeats=2) {
                for (; repeats > 0; repeats--) {
                    GUI.Box(rect, "");
                }
            }
        }
        
        void OnDestroy() {
            // Free unmanaged memory here
        }
        
        // ========== Other methods ========== //
        
        void ResizeDisplay() {
            int w = cam.pixelWidth, h = cam.pixelHeight;
            
            if (RenderSize > 0) {
                int maxTexSize = SystemInfo.maxTextureSize;
                float scale = Mathf.Min(RenderSize, maxTexSize) / (float)Mathf.Max(w, h);
                w = Mathf.Max(Mathf.RoundToInt(w*scale), 1);
                h = Mathf.Max(Mathf.RoundToInt(h*scale), 1);
            }
            
            int tilePow = Mathf.Clamp((int)TilePow, 0, 12);
            int tileSize = (tilePow <= 0 ? 0 : 1 << (tilePow-1));
            
            if (tex && (w == tex.width) && (h == tex.height)) {
                if (tileSize != buffer.TileSize) {
                    buffer.Resize(w, h, tileSize);
                }
                return;
            }
            
            if (tex) UnityEngine.Object.Destroy(tex);
            
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            
            colors = new Color32[w*h];
            
            buffer.Resize(w, h, tileSize);
        }
        
        void UpdateCameraInfo() {
            //vp_matrix = cam.projectionMatrix * cam.worldToCameraMatrix;
            //vp_matrix = Matrix4x4.Scale(new Vector3(tex.width*0.5f, tex.height*0.5f, 1)) * vp_matrix;
            //vp_matrix = Matrix4x4.Translate(new Vector3(tex.width*0.5f, tex.height*0.5f, 0f)) * vp_matrix;
            
            vp_matrix = cam.worldToCameraMatrix;
            float ah = cam.orthographicSize;
            float aw = (ah * tex.width) / tex.height;
            vp_matrix = Matrix4x4.Scale(new Vector3(tex.width*0.5f/aw, tex.height*0.5f/ah, -1)) * vp_matrix;
            vp_matrix = Matrix4x4.Translate(new Vector3(tex.width*0.5f, tex.height*0.5f, 0f)) * vp_matrix;
            
            frustum_planes = GeometryUtility.CalculateFrustumPlanes(cam);
        }
        
        void CollectRenderers() {
            visible_objects.Clear();
            WalkChildren<Renderer>(PointCloudsParent, AddRenderer);
            visible_objects.Sort((itemA, itemB) => {
                return itemA.Item1.CompareTo(itemB.Item1);
            });
            
            void AddRenderer(Renderer renderer) {
                if (!renderer.enabled) return;
                if (!renderer.gameObject.activeInHierarchy) return;
                if (GeometryUtility.TestPlanesAABB(frustum_planes, renderer.bounds)) {
                    var tfm = renderer.transform;
                    var pos = tfm.position;
                    float sort_z = pos.x*vp_matrix.m20 + pos.y*vp_matrix.m21 + pos.z*vp_matrix.m22;
                    visible_objects.Add((sort_z, tfm));
                }
            }
            
            void WalkChildren<T>(Transform parent, System.Action<T> callback) where T : Component {
                if (!parent) return;
                foreach (Transform child in parent) {
                    var component = child.GetComponent<T>();
                    if (component) callback(component);
                    WalkChildren<T>(child, callback);
                }
            }
        }
        
        void RenderMain() {
            UpdateCameraInfo();
            CollectRenderers();
            
            stopwatch.Restart();
            
            Splatter.PixelCount = 0;
            Splatter.QuadCount = 0;
            Splatter.CulledCount = 0;
            Splatter.OccludedCount = 0;
            Splatter.NodeCount = 0;
            
            Splatter.ComplexityMax = 0;
            Splatter.ComplexitySum = 0;
            Splatter.ComplexityCnt = 0;
            
            buffer.Clear(cam.backgroundColor);
            RenderObjects();
            Blit();
            
            stopwatch.Stop();
            dt = (stopwatch.ElapsedMilliseconds/1000f);
            
            tex.SetPixels32(0, 0, tex.width, tex.height, colors, 0);
            tex.Apply(false);
        }
        
        unsafe void Blit() {
            int w = tex.width;
            int h = tex.height;
            int shift = buffer.TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = buffer.TileCountX;
            int tny = buffer.TileCountY;
            
            // int depth_shift = Mathf.Max(depth_resolution - 8, 0);
            int depth_shift = Mathf.Max(depth_resolution - 13, 0);
            
            fixed (Buffer.DataItem* data_ptr = buffer.Data)
            fixed (Color32* colors_ptr = colors)
            {
                for (int ty = 0; ty < tny; ++ty) {
                    int iy = ty << shift;
                    var colors_ty = colors_ptr + iy * w;
                    int th = h - iy; if (th > tile_size) th = tile_size;
                    for (int tx = 0; tx < tnx; ++tx) {
                        int ix = tx << shift;
                        var colors_tx = colors_ty + ix;
                        int tw = w - ix; if (tw > tile_size) tw = tile_size;
                        
                        var tile = data_ptr + ((tx + ty * tnx) << shift2);
                        var tile_end_y = tile + (th << shift);
                        var colors_y = colors_tx;
                        for (var tile_y = tile; tile_y != tile_end_y; tile_y += tile_size, colors_y += w) {
                            var tile_end_x = tile_y + tw;
                            var colors_x = colors_y;
                            for (var tile_x = tile_y; tile_x != tile_end_x; ++tile_x, ++colors_x) {
                                *colors_x = tile_x->color;
                                // int d = tile_x->depth >> depth_shift;
                                // colors_x->r = (byte)d;
                            }
                        }
                    }
                }
            }
        }
        
        unsafe void RenderObjects() {
            if (Splatter.CountRaycastNodes) Splatter.RaycastNodes.Clear();
            
            var octree = test_octree;
            if (use_model && (model_octree != null)) octree = model_octree;
            
            int w = buffer.Width, h = buffer.Height, tile_shift = buffer.TileShift;
            float depth_scale = (1 << depth_resolution) / (cam.farClipPlane - cam.nearClipPlane);
            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (uint* queues = OctantOrder.Queues)
            fixed (Splatter.Delta* deltas = splatter.deltas)
            fixed (Splatter.StackEntry* stack = splatter.stack)
            fixed (Splatter.RayDelta* ray_deltas = Splatter.ray_deltas)
            fixed (Splatter.RayStack* ray_stack = Splatter.ray_stack)
            fixed (int* map = Splatter.ray_map)
            {
                foreach (var (sort_z, tfm) in visible_objects) {
                    var obj2world = tfm.localToWorldMatrix * Matrix4x4.Scale(Vector3.one*voxel_scale);
                    var mvp_matrix = vp_matrix * obj2world;
                    int node = octree.root_node;
                    var color = octree.root_color;
                    fixed (int* nodes = octree.nodes)
                    fixed (Color32* colors = octree.colors)
                    {
                        Splatter.Render(buf, w, h, tile_shift, node, color, nodes, colors,
                            ref mvp_matrix, subpixel_shift, depth_scale, queues, deltas, stack,
                            ray_deltas, ray_stack, map);
                    }
                }
            }
            
            Splatter.CountRaycastNodes = false;
        }
        
        static RawOctree LoadPointCloud(string path, float voxel_size=-1) {
            if (string.IsNullOrEmpty(path)) return null;
            
            string cached_path = path+".cache3";
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
        
        static RawOctree LoadCached(string cached_path) {
            try {
                var octree = new RawOctree();
                var stream = new FileStream(cached_path, FileMode.Open, FileAccess.Read);
                var br = new BinaryReader(stream); {
                    int node_count = br.ReadInt32();
                    octree.root_node = br.ReadInt32();
                    octree.root_color = ReadColor32(br);
                    octree.nodes = ReadArray<int>(br, node_count << 3);
                    octree.colors = ReadArray<Color32>(br, node_count << 3);
                }
                stream.Close();
                stream.Dispose();
                SanitizeNodes(octree.nodes);
    			Debug.Log("Cached version loaded: "+cached_path);
                return octree;
            } catch (System.Exception exc) {
                Debug.LogException(exc);
                return null;
            }
        }
        
        static unsafe void SanitizeNodes(int[] nodes) {
            const int mask = 0xFF << 24;
            fixed (int* _nodes_ptr = nodes) {
                int* nodes_ptr = _nodes_ptr;
                int* nodes_end = nodes_ptr + nodes.Length;
                for (; nodes_ptr != nodes_end; ++nodes_ptr) {
                    if (((*nodes_ptr) & mask) == 0) *nodes_ptr = 0;
                }
            }
        }
        
        static void WriteCached(RawOctree octree, string cached_path) {
			var stream = new FileStream(cached_path, FileMode.Create, FileAccess.Write);
			var bw = new BinaryWriter(stream); {
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
			Debug.Log("Cached version saved: "+cached_path);
        }
        
        [StructLayout(LayoutKind.Explicit)]
        struct Color32_int {
            [FieldOffset(0)] public Color32 c;
            [FieldOffset(0)] public int i;
        }
        static void WriteColor32(BinaryWriter bw, Color32 color) {
            Color32_int color_int = default;
            color_int.c = color;
            bw.Write(color_int.i);
        }
        static Color32 ReadColor32(BinaryReader br) {
            Color32_int color_int = default;
            color_int.i = br.ReadInt32();
            return color_int.c;
        }
        
        unsafe static void WriteArray<T>(BinaryWriter bw, T[] array) where T : struct {
            var bytes = new byte[array.Length * Marshal.SizeOf<T>()];
            // System.Buffer.BlockCopy() only works for arrays of primitive types
            fixed (byte* bytes_ptr = bytes)
            {
                GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                var array_ptr = handle.AddrOfPinnedObject().ToPointer();
                System.Buffer.MemoryCopy(array_ptr, bytes_ptr, bytes.Length, bytes.Length);
                handle.Free();
            }
            bw.Write(bytes);
        }
        unsafe static T[] ReadArray<T>(BinaryReader br, int count) where T : struct {
            var bytes = br.ReadBytes(count * Marshal.SizeOf<T>());
            var array = new T[count];
            // System.Buffer.BlockCopy() only works for arrays of primitive types
            fixed (byte* bytes_ptr = bytes)
            {
                GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                var array_ptr = handle.AddrOfPinnedObject().ToPointer();
                System.Buffer.MemoryCopy(bytes_ptr, array_ptr, bytes.Length, bytes.Length);
                handle.Free();
            }
            return array;
        }
        
        static RawOctree ConvertOctree(LeafOctree<Color32> octree) {
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

        static (int, Color32) LinearizeOctree(OctreeNode<Color32> node, int[] nodes, Color32[] colors, ref int id) {
            Color color = default;
            int count = 0;
            int mask = 0;
            int id0 = id, pos0 = id0 << 3;
            
            for (int i = 0; i < 8; i++) {
                var subnode = node[i];
                if (subnode == null) continue;
                
                mask |= (1 << i);
                
                if (subnode == node) {
                    nodes[pos0|i] = (255 << 24) | id0;
                    colors[pos0|i] = subnode.data;
                } else {
                    ++id;
                    int subid = id;
                    var (submask, subcolor) = LinearizeOctree(subnode, nodes, colors, ref id);
                    if (submask == 0) subid = 0;
                    nodes[pos0|i] = (submask << 24) | subid;
                    colors[pos0|i] = subcolor;
                }
                
                color.r += colors[pos0|i].r;
                color.g += colors[pos0|i].g;
                color.b += colors[pos0|i].b;
                color.a += colors[pos0|i].a;
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

    class RawOctree {
        public int root_node;
        public Color32 root_color;
        public int[] nodes;
        public Color32[] colors;
    }

    class Buffer {
        public struct DataItem {
            public int depth;
            public Color32 color;
        }
        
        public DataItem[] Data;
        
        public int Width;
        public int Height;
        public int TileCountX;
        public int TileCountY;
        public int TileShift;
        
        public int TileSize => 1 << TileShift;
        public int TileCount => TileCountX * TileCountY;
        
        public void Resize(int width, int height, int tileSize=0) {
            Width = width;
            Height = height;
            
            if (tileSize <= 0) {
                TileShift = Mathf.Max(NextPow2(width), NextPow2(height));
            } else {
                TileShift = NextPow2(tileSize);
            }
            
            tileSize = 1 << TileShift;
            
            TileCountX = (Width+tileSize-1) >> TileShift;
            TileCountY = (Height+tileSize-1) >> TileShift;
            
            Data = new DataItem[tileSize*tileSize*TileCountX*TileCountY];
            
            int NextPow2(int v) {
                return Mathf.CeilToInt(Mathf.Log(v) / Mathf.Log(2));
            }
        }
        
        public unsafe void Clear(Color32 background) {
            var clear_data = default(DataItem);
            clear_data.depth = int.MaxValue;
            clear_data.color = background;
            
            int w = Width;
            int h = Height;
            int shift = TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = TileCountX;
            int tny = TileCountY;
            
            fixed (Buffer.DataItem* data_ptr = Data)
            {
                for (int ty = 0; ty < tny; ++ty) {
                    int iy = ty << shift;
                    int th = h - iy; if (th > tile_size) th = tile_size;
                    for (int tx = 0; tx < tnx; ++tx) {
                        int ix = tx << shift;
                        int tw = w - ix; if (tw > tile_size) tw = tile_size;
                        
                        var tile = data_ptr + ((tx + ty * tnx) << shift2);
                        var tile_end_y = tile + (th << shift);
                        for (var tile_y = tile; tile_y != tile_end_y; tile_y += tile_size) {
                            var tile_end_x = tile_y + tw;
                            for (var tile_x = tile_y; tile_x != tile_end_x; ++tile_x) {
                                *tile_x = clear_data;
                            }
                        }
                    }
                }
            }
        }
    }

    class Splatter {
        public struct Delta {
            public int x0, y0;
            public int x1, y1;
            public int z;
            public int pad0; // just for power-of-2 size
            public int x01, y01;
        }
        public Delta[] deltas = new Delta[8*32];
        
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
        public StackEntry[] stack = new StackEntry[8*32];
        
        public static float MaxLevel = 0;
        
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
        public static RayDelta[] ray_deltas = new RayDelta[8*32];
        
        public struct RayStack {
            public int level;
            public bool draw;
            public int node;
            public int offset;
            public uint queue;
            public int x, y, z;
        }
        public static RayStack[] ray_stack = new RayStack[8*32];
        
        public static int ray_shift = 16;
        public static int map_shift = 3;
        
        public static int[] ray_map = new int[128*128]; // for testing effects of different sizes
        
        public static int ComplexityMax = 0;
        public static int ComplexitySum = 0;
        public static int ComplexityCnt = 0;
        
        public static bool UseRaycast = false;
        
        public static bool CountRaycastNodes = false;
        public static HashSet<int> RaycastNodes = new HashSet<int>();
        
        public static bool FlatMode = false;
        
        public static int OverrideKey = -1;
        
        unsafe static Delta CalcBoundsAndDeltas(ref Matrix4x4 matrix, int subpixel_shift, float depth_scale, Delta* deltas) {
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
            root.x0 = (int)((center.x-extents.x)*pixel_size + 0.5f) + half_pixel;
            root.y0 = (int)((center.y-extents.y)*pixel_size + 0.5f) + half_pixel;
            root.x1 = (int)((center.x+extents.x)*pixel_size + 0.5f) + half_pixel;
            root.y1 = (int)((center.y+extents.y)*pixel_size + 0.5f) + half_pixel;
            root.z = (int)((center.z-extents.z)*depth_scale + 0.5f);
            
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
                            float x = center.x + matrix.m00*subX + matrix.m01*subY + matrix.m02*subZ;
                            float y = center.y + matrix.m10*subX + matrix.m11*subY + matrix.m12*subZ;
                            float z = center.z + matrix.m20*subX + matrix.m21*subY + matrix.m22*subZ;
                            
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
                            
                            int ix = (int)(x*pixel_size + 0.5f) + half_pixel;
                            int iy = (int)(y*pixel_size + 0.5f) + half_pixel;
                            int iz = (int)(z*depth_scale + 0.5f);
                            
                            float uv_x = ((x - center.x)/extents.x + 1f) * 0.25f;
                            float uv_y = ((y - center.y)/extents.y + 1f) * 0.25f;
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
            
            int Xx=(int)(matrix.m00*scale_x), Yx=(int)(matrix.m01*scale_x), Zx=(int)(matrix.m02*scale_x), Tx=(int)((w * 0.5f)*pixel_size);
            int Xy=(int)(matrix.m10*scale_y), Yy=(int)(matrix.m11*scale_y), Zy=(int)(matrix.m12*scale_y), Ty=(int)((h * 0.5f)*pixel_size);

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

            int dotXM = Mathf.Max(Mathf.Abs(Xx*(Yy+Zy) - Xy*(Yx+Zx)), Mathf.Abs(Xx*(Yy-Zy) - Xy*(Yx-Zx)));
            int dotYM = Mathf.Max(Mathf.Abs(Yx*(Xy+Zy) - Yy*(Xx+Zx)), Mathf.Abs(Yx*(Xy-Zy) - Yy*(Xx-Zx)));
            int dotZM = Mathf.Max(Mathf.Abs(Zx*(Xy+Yy) - Zy*(Xx+Yx)), Mathf.Abs(Zx*(Xy-Yy) - Zy*(Xx-Yx)));

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
                        int dx = (Xx*subX + Yx*subY + Zx*subZ) >> 1;
                        int dy = (Xy*subX + Yy*subY + Zy*subZ) >> 1;
                        int cx = Tx + dx;
                        int cy = Ty + dy;
                        
                        int xmin = Mathf.Max(((cx-extents_x) >> subpixel_shift) - 2, margin);
                        int ymin = Mathf.Max(((cy-extents_y) >> subpixel_shift) - 2, margin);
                        int xmax = Mathf.Min(((cx+extents_x) >> subpixel_shift) + 2, w-margin);
                        int ymax = Mathf.Min(((cy+extents_y) >> subpixel_shift) + 2, h-margin);
                        
                        int offset_x = (xmin << subpixel_shift) - cx;
                        int offset_y = (ymin << subpixel_shift) - cy;
                        
                        int dotXr = Xx*offset_y - Xy*offset_x;
                        int dotYr = Yx*offset_y - Yy*offset_x;
                        int dotZr = Zx*offset_y - Zy*offset_x;
                        
                        int mask = 1 << octant;
                        
                        for (int iy = ymin; iy < ymax; ++iy) {
                            int ixy0 = (iy << buf_shift) + xmin;
                            int ixy1 = (iy << buf_shift) + xmax;
                            int dotX = dotXr;
                            int dotY = dotYr;
                            int dotZ = dotZr;
                            for (int ixy = ixy0; ixy < ixy1; ++ixy) {
                                //if ((dotX<=dotXM)&(-dotX<=dotXM) & (dotY<=dotYM)&(-dotY<=dotYM) & (dotZ<=dotZM)&(-dotZ<=dotZM)) { // a bit slower
                                if (((dotX^(dotX>>31)) <= dotXM) & ((dotY^(dotY>>31)) <= dotYM) & ((dotZ^(dotZ>>31)) <= dotZM)) { // a bit faster
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
            ref Matrix4x4 matrix, int subpixel_shift, float depth_scale,
            uint* queues, Delta* deltas, StackEntry* stack,
            RayDelta* ray_deltas, RayStack* ray_stack, int* map)
        {
            UseMap = (RenderAlg == 4) | (RenderAlg == 5);
            
            var root = CalcBoundsAndDeltas(ref matrix, subpixel_shift, depth_scale, deltas);
            
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
                    
                    var tile = buf + ((tx + ty * tnx) << (tile_shift+tile_shift));
                    
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
            RayDelta* ray_deltas, RayStack* ray_stack, int* map)
        {
            // We need to put nodes on stack in back-to-front order
            int reverse_key = forward_key ^ 0b11100000000;
            
            if ((OverrideKey >= 0) & (OverrideKey < 8)) {
                reverse_key = forward_key & ~0b11100000000;
                reverse_key |= OverrideKey << 8;
            }
            
            int tile_size = 1 << tile_shift;
            
            int max_level = (int)MaxLevel;
            max_level = Mathf.Clamp(max_level, 0, 32);
            
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
                
                if ((pw|ph) == 1) {
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
                    int drow = row - (px1-px0);
                    int cmp_z = current->z;
                    var pixel = tile + (px0 + (py0 << tile_shift));
                    var endpixel = pixel + (px1-px0);
                    var lastpixel = endpixel + ((py1-py0) << tile_shift);
                    
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
                int level = current->level+1;
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
            uint* queues, Delta* deltas, StackEntry* stack)
        {
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
                
                if ((current->pw|current->ph) == 1) {
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
                        
                        if ((x < 0) | (x >= w-2)) continue;
                        if ((y < 0) | (y >= h-2)) continue;
                        
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
                        int subpixel_shift1 = subpixel_shift+1;
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
                    int level = current->level+1;
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
            RayDelta* ray_deltas, RayStack* ray_stack, int* map)
        {
            int max_level = CalcMaxLevel(x0, y0, x1, y1, subpixel_shift);
            max_level = Mathf.Max(Mathf.Min(max_level, ((int)MaxLevel)-1), 0);
            
            Raycast(tile, w, h, tile_shift,
            nodes, colors, forward_key, subpixel_shift,
            queues, x0, y0, x1, y1, z, node_start,
            ray_deltas, ray_stack, map, max_level);
        }

        unsafe static void Raycast(Buffer.DataItem* tile, int w, int h, int tile_shift,
            int* nodes, Color32* colors, int forward_key, int subpixel_shift,
            uint* queues, int x0, int y0, int x1, int y1, int z, int node_start,
            RayDelta* ray_deltas, RayStack* ray_stack, int* map, int max_level, int start_level = 0)
        {
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
                            
                            for (;;) {
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
                                if (((subx|suby) & ray_mask) != 0) continue;
                                
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
                            
                            for (;;) {
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
                                if (((subx|suby) & ray_mask) != 0) continue;
                                
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
    
    static class OctantOrder {
        // Node traversal order and traversal state can be combined into a
        // bit-string "queue" of octant indices (can also take into account
        // different number of stored octants). When a node is "dequeued",
        // the bit-string shifts by 4 bits. 3 bits for octant index,
        // 1 bit for signifying that this is the last octant.
        
        public const int XYZ=0, XZY=1, YXZ=2, YZX=3, ZXY=4, ZYX=5;
        
        static uint[] queues = null;
        public static uint[] Queues => queues ?? MakeQueues();
        
        public static int Key(ref Matrix4x4 matrix) {
            return ((Order(ref matrix) << 3) | Octant(ref matrix)) << 8;
        }
        
        public static int Octant(ref Matrix4x4 matrix) {
            // Here we check which side of YZ/XZ/XY planes the view vector belongs to
            // This is specific to Unity's coordinate system (X right, Y up, Z forward)
            int bit_x = (matrix.m11 * matrix.m02 <= matrix.m01 * matrix.m12 ? 0 : 1); // Y.y * Z.x <= Y.x * Z.y
            int bit_y = (matrix.m12 * matrix.m00 <= matrix.m02 * matrix.m10 ? 0 : 2); // Z.y * X.x <= Z.x * X.y
            int bit_z = (matrix.m10 * matrix.m01 <= matrix.m00 * matrix.m11 ? 0 : 4); // X.y * Y.x <= X.x * Y.y
            return bit_x | bit_y | bit_z;
        }
        
        public static int Order(ref Matrix4x4 matrix) {
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
        
        static uint[] MakeQueues() {
            if (queues == null) {
                queues = new uint[6*8*256];
                for (int order = 0; order < 6; order++) {
                    for (int octant = 0; octant < 8; octant++) {
                        for (int mask = 0; mask < 256; mask++) {
                            queues[(((order << 3) | octant) << 8) | mask] = MakeQueue(octant, order, mask);
                        }
                    }
                }
            }
            return queues;
        }
        
        static uint MakeQueue(int start, int order, int mask) {
            int _u = 0, _v = 0, _w = 0;
            switch (order) {
            case XYZ: _u = 0; _v = 1; _w = 2; break;
            case XZY: _u = 0; _v = 2; _w = 1; break;
            case YXZ: _u = 1; _v = 0; _w = 2; break;
            case YZX: _u = 1; _v = 2; _w = 0; break;
            case ZXY: _u = 2; _v = 0; _w = 1; break;
            case ZYX: _u = 2; _v = 1; _w = 0; break;
            }
            
            uint queue = 0;
            int shift = 0;
            for (int w = 0; w <= 1; w++) {
                for (int v = 0; v <= 1; v++) {
                    for (int u = 0; u <= 1; u++) {
                        int flip = (u << _u) | (v << _v) | (w << _w);
                        int octant = (start ^ flip);
                        if ((mask & (1 << octant)) == 0) continue;
                        queue |= (uint)((octant|8) << shift);
                        shift += 4;
                    }
                }
            }
            
            return queue;
        }
    }
}
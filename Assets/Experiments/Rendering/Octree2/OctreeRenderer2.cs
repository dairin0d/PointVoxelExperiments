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
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

using dairin0d.Data.Points;
using dairin0d.Data.Voxels;

namespace dairin0d.Rendering.Octree2 {
    class Buffer {
        public struct DataItem {
            public int depth;
            public Color32 color;
        }

        public DataItem[] Data;

        public Texture2D Texture;
        private Color32[] colors;

        public int Width;
        public int Height;
        public int TileCountX;
        public int TileCountY;
        public int TileShift;

        public int TileSize => 1 << TileShift;
        public int TileCount => TileCountX * TileCountY;

        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        public float FrameTime {get; private set;}

        public void RenderStart(Color32 background) {
            stopwatch.Restart();
            Clear(background);
        }
        
        public void RenderEnd(int depth_shift = -1) {
            Blit(depth_shift);
            stopwatch.Stop();
            FrameTime = stopwatch.ElapsedMilliseconds / 1000f;
            UpdateTexture();
        }

        public void Resize(int w, int h, int renderSize, int tilePow = 0) {
            if (renderSize > 0) {
                int maxTexSize = SystemInfo.maxTextureSize;
                float scale = Mathf.Min(renderSize, maxTexSize) / (float)Mathf.Max(w, h);
                w = Mathf.Max(Mathf.RoundToInt(w * scale), 1);
                h = Mathf.Max(Mathf.RoundToInt(h * scale), 1);
            }

            tilePow = Mathf.Clamp(tilePow, 0, 12);
            int tileSize = (tilePow <= 0 ? 0 : 1 << (tilePow - 1));

            if (Texture && (w == Texture.width) && (h == Texture.height)) {
                if (tileSize != TileSize) Resize(w, h, tileSize);
                return;
            }

            if (Texture) UnityEngine.Object.Destroy(Texture);

            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Texture.filterMode = FilterMode.Point;

            colors = new Color32[w * h];

            Resize(w, h, tileSize);
        }
        
        private void Resize(int width, int height, int tileSize = 0) {
            Width = width;
            Height = height;

            if (tileSize <= 0) {
                TileShift = Mathf.Max(NextPow2(width), NextPow2(height));
            } else {
                TileShift = NextPow2(tileSize);
            }

            tileSize = 1 << TileShift;

            TileCountX = (Width + tileSize - 1) >> TileShift;
            TileCountY = (Height + tileSize - 1) >> TileShift;

            Data = new DataItem[tileSize * tileSize * TileCountX * TileCountY];

            int NextPow2(int v) {
                return Mathf.CeilToInt(Mathf.Log(v) / Mathf.Log(2));
            }
        }
        
        public void UpdateTexture() {
            Texture.SetPixels32(0, 0, Texture.width, Texture.height, colors, 0);
            Texture.Apply(false);
        }
        
        public unsafe void Blit(int depth_shift = -1) {
            int w = Texture.width;
            int h = Texture.height;
            int shift = TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = TileCountX;
            int tny = TileCountY;

            bool show_depth = (depth_shift >= 0);

            fixed (DataItem* data_ptr = Data)
            fixed (Color32* colors_ptr = colors) {
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
                                if (show_depth) {
                                    byte d = (byte)(tile_x->depth >> depth_shift);
                                    colors_x->r = colors_x->g = colors_x->b = d;
                                    colors_x->a = tile_x->color.a;
                                } else {
                                    *colors_x = tile_x->color;
                                }
                            }
                        }
                    }
                }
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

            fixed (DataItem* data_ptr = Data) {
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

    class RawOctree {
        public int depth;
        public int root_node;
        public Color32 root_color;
        public int[] nodes;
        public Color32[] colors;

        public static RawOctree MakeFractal(byte mask, Color32 color) {
            int root_node = mask;
            var nodes = new int[8];
            var colors = new Color32[8];
            for (int i = 0; i < 8; i++) {
                nodes[i] = ((mask & (1 << i)) != 0 ? root_node : 0);
                colors[i] = color;
            }
            return new RawOctree() {
                depth = 32,
                root_node = root_node,
                root_color = color,
                nodes = nodes,
                colors = colors,
            };
        }

        public static RawOctree LoadPointCloud(string path, float voxel_size = -1) {
            if (string.IsNullOrEmpty(path)) return null;

            string cached_path = path + ".cache4";
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
                    octree.depth = br.ReadInt32();
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
            const int mask = 0xFF;
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
                bw.Write(octree.depth);
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
            int id = 0, depth = 0;
            var (mask, color) = LinearizeOctree(octree.Root, nodes, colors, ref id, ref depth);
            return new RawOctree() {
                depth = depth,
                root_node = mask,
                root_color = color,
                nodes = nodes,
                colors = colors,
            };
        }

        private static (int, Color32) LinearizeOctree(OctreeNode<Color32> node, int[] nodes, Color32[] colors, ref int id, ref int depth, int level = 0) {
            Color color = default;
            int count = 0;
            int mask = 0;
            int id0 = id, pos0 = id0 << 3;

            if (depth < level) depth = level;

            for (int i = 0; i < 8; i++) {
                var subnode = node[i];
                if (subnode == null) continue;

                mask |= (1 << i);

                if (subnode == node) {
                    nodes[pos0 | i] = (id0 << 8) | 0xFF;
                    colors[pos0 | i] = subnode.data;
                } else {
                    ++id;
                    int subid = id;
                    var (submask, subcolor) = LinearizeOctree(subnode, nodes, colors, ref id, ref depth, level+1);
                    if (submask == 0) subid = 0;
                    nodes[pos0 | i] = (subid << 8) | submask;
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

    internal struct Widget<T> {
        public readonly string Name;
        public readonly System.Func<T> Get;
        public readonly System.Action<T> Set;
        public float Min, Max, Step;

        public Widget(string name, System.Func<T> getter = null, System.Action<T> setter = null,
            float min = 0, float max = 0, float step = 0)
        {
            Name = name;
            Get = getter;
            Set = setter;
            Min = min;
            Max = max;
            Step = step;
        }
    }

    public class OctreeRenderer2 : MonoBehaviour {
        public int vSyncCount = 0;
        public int targetFrameRate = 30;

        Camera cam;
        int cullingMask;
        CameraClearFlags clearFlags;

        Matrix4x4 vp_matrix;
        Plane[] frustum_planes;

        public int RenderSize = 0;
        Buffer buffer;

        public int depth_resolution = 16;
        Splatter splatter;

        public int depth_display_shift = -1;

        public Transform PointCloudsParent;
        public float voxel_scale = 1f;
        List<(float, Transform)> visible_objects = new List<(float, Transform)>(64);

        public Color32 test_octree_color = Color.green;
        public byte test_octree_mask = 0b10011001;
        RawOctree test_octree; // for test

        public bool use_model = false;
        public string model_path = "";
        public float voxel_size = -1;
        RawOctree model_octree;

        List<Widget<string>> InfoWidgets = new List<Widget<string>>(32);
        List<Widget<float>> SliderWidgets = new List<Widget<float>>(32);
        List<Widget<bool>> ToggleWidgets = new List<Widget<bool>>(32);

        Material mat;

        // ========== Unity events ========== //

        void Start() {
            if (!Application.isEditor) Screen.SetResolution(640, 480, false);

            cam = GetComponent<Camera>();

            buffer = new Buffer();

            splatter = new Splatter();

            test_octree = RawOctree.MakeFractal(test_octree_mask, test_octree_color);

            model_octree = RawOctree.LoadPointCloud(model_path, voxel_size);
        }

        void Update() {
            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = targetFrameRate;
            buffer.Resize(cam.pixelWidth, cam.pixelHeight, RenderSize);
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
            UpdateWidgets();
            
            if (!mat) {
                var shader = Shader.Find("UI/Default");
                mat = new Material(shader);
                mat.hideFlags = HideFlags.HideAndDontSave;
            }
            
            mat.mainTexture = buffer.Texture;
            mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadOrtho();

            GL.Begin(GL.QUADS);
            GL.TexCoord2(0, 0);
            GL.Vertex3(0, 0, 0);
            GL.TexCoord2(1, 0);
            GL.Vertex3(1, 0, 0);
            GL.TexCoord2(1, 1);
            GL.Vertex3(1, 1, 0);
            GL.TexCoord2(0, 1);
            GL.Vertex3(0, 1, 0);
            GL.End();

            GL.PopMatrix();
        }

        void OnGUI() {
            int x = 0, y = 0, panelWidth = 160, lineHeight = 20, sliderHeight = 12;

            // GUI.DrawTexture(cam.pixelRect, buffer.Texture, ScaleMode.StretchToFill, true);

            x = 0;
            y = Screen.height;
            
            DrawBox(new Rect(x, y, panelWidth, -lineHeight * ToggleWidgets.Count));
            foreach (var widget in ToggleWidgets) {
                y -= lineHeight;
                widget.Set(GUI.Toggle(new Rect(x, y, panelWidth, lineHeight), widget.Get(), widget.Name));
            }
            
            DrawBox(new Rect(x, y, panelWidth, -lineHeight * InfoWidgets.Count));
            foreach (var widget in InfoWidgets) {
                y -= lineHeight;
                string text = widget.Name;
                if (widget.Get != null) text += "=" + widget.Get();
                GUI.Label(new Rect(x, y, panelWidth, lineHeight), text);
            }

            x = Screen.width - panelWidth;
            y = Screen.height;

            DrawBox(new Rect(x, y, panelWidth, -(lineHeight + sliderHeight) * SliderWidgets.Count));
            foreach (var widget in SliderWidgets) {
                y -= sliderHeight;
                float value = widget.Get();
                if (widget.Step == 0) {
                    value = GUI.HorizontalSlider(new Rect(x, y, panelWidth, lineHeight), value, widget.Min, widget.Max);
                } else {
                    float scaledValue = (int)(value / widget.Step);
                    float scaledMin = widget.Min / widget.Step;
                    float scaledMax = widget.Max / widget.Step;
                    scaledValue = GUI.HorizontalSlider(new Rect(x, y, panelWidth, lineHeight), scaledValue, scaledMin, scaledMax);
                    value = scaledValue * widget.Step;
                }
                widget.Set(value);
                y -= lineHeight;
                GUI.Label(new Rect(x, y, panelWidth, lineHeight), $"{widget.Name}: {value}");
            }
        }

        void OnDestroy() {
            // Free unmanaged memory here
        }

        // ========== Other methods ========== //

        static void DrawBox(Rect rect, int repeats = 2) {
            if (rect.width < 0) {
                rect.width = -rect.width;
                rect.x -= rect.width;
            }
            if (rect.height < 0) {
                rect.height = -rect.height;
                rect.y -= rect.height;
            }
            
            for (; repeats > 0; repeats--) {
                GUI.Box(rect, "");
            }
        }

        void UpdateWidgets() {
            InfoWidgets.Clear();
            InfoWidgets.Add(new Widget<string>($"{buffer.FrameTime:0.000}"));

            SliderWidgets.Clear();
            SliderWidgets.Add(new Widget<float>("RenderSize", () => RenderSize, (value) => { RenderSize = (int)value; }, 0, 640, 16));

            ToggleWidgets.Clear();
            ToggleWidgets.Add(new Widget<bool>("Fullscreen", () => Screen.fullScreen,
                (value) => { if (Screen.fullScreen != value) Screen.fullScreen = value; }));
            ToggleWidgets.Add(new Widget<bool>("Use model", () => use_model, (value) => { use_model = value; }));

            splatter.UpdateWidgets(InfoWidgets, SliderWidgets, ToggleWidgets);
        }

        void UpdateCameraInfo() {
            int w = buffer.Width, h = buffer.Height;

            // vp_matrix = cam.projectionMatrix * cam.worldToCameraMatrix;
            // vp_matrix = Matrix4x4.Scale(new Vector3(w * 0.5f, h * 0.5f, 1)) * vp_matrix;
            // vp_matrix = Matrix4x4.Translate(new Vector3(w * 0.5f, h * 0.5f, 0f)) * vp_matrix;

            float ah = cam.orthographicSize;
            float aw = (ah * w) / h;
            vp_matrix = cam.worldToCameraMatrix;
            vp_matrix = Matrix4x4.Scale(new Vector3(w * 0.5f / aw, h * 0.5f / ah, -1)) * vp_matrix;
            vp_matrix = Matrix4x4.Translate(new Vector3(w * 0.5f, h * 0.5f, 0f)) * vp_matrix;

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
                    float sort_z = pos.x * vp_matrix.m20 + pos.y * vp_matrix.m21 + pos.z * vp_matrix.m22;
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
            var octree = test_octree;
            if (use_model && (model_octree != null)) octree = model_octree;
            
            UpdateCameraInfo();
            CollectRenderers();

            buffer.RenderStart(cam.backgroundColor);
            
            splatter.RenderObjects(buffer, IterateInstances(octree));
            
            buffer.RenderEnd(depth_display_shift);
        }
        
        IEnumerable<(Matrix4x4, T)> IterateInstances<T>(T model) {
            var voxel_scale_matrix = Matrix4x4.Scale(Vector3.one * voxel_scale);
            
            float depth_scale = (1 << depth_resolution) / (cam.farClipPlane - cam.nearClipPlane);
            var depth_scale_matrix = Matrix4x4.Scale(new Vector3(1, 1, depth_scale));
            
            foreach (var (sort_z, tfm) in visible_objects) {
                var obj2world = tfm.localToWorldMatrix * voxel_scale_matrix;
                var mvp_matrix = vp_matrix * obj2world;
                mvp_matrix = depth_scale_matrix * mvp_matrix;
                yield return (mvp_matrix, model);
            }
        }
    }
    
    class Splatter {
        const int PrecisionShift = 30;
        
        struct Delta {
            public int x, y, z, pad0;
            public int x0, y0, x1, y1;
        }
        Delta[] deltas = new Delta[8 * 32];
        
        struct StackEntry {
            public int x, y, offset;
            public uint queue;
            public NodeState state;
        }
        StackEntry[] stack = new StackEntry[8 * 32];
        
        struct NodeState {
            public int x0, y0, x1, y1;
            public int mx0, my0, mx1, my1;
            public int depth, node, pixelShift, pixelSize;
        }
        
        public int MapShift = 5;
        OctantMap octantMap = new OctantMap();
        
        public int MaxLevel = 0;
        
        public bool UseRaycast = false;
        
        public void UpdateWidgets(List<Widget<string>> infoWidgets, List<Widget<float>> sliderWidgets, List<Widget<bool>> toggleWidgets) {
            // infoWidgets.Add(new Widget<string>($"PixelCount={PixelCount}"));
            // infoWidgets.Add(new Widget<string>($"QuadCount={QuadCount}"));
            // infoWidgets.Add(new Widget<string>($"CulledCount={CulledCount}"));
            // infoWidgets.Add(new Widget<string>($"OccludedCount={OccludedCount}"));
            // infoWidgets.Add(new Widget<string>($"NodeCount={NodeCount}"));

            sliderWidgets.Add(new Widget<float>("Level", () => MaxLevel, (value) => { MaxLevel = (int)value; }, 0, 16));
            // sliderWidgets.Add(new Widget<float>("Stop At", () => StopAt, (value) => { StopAt = (int)value; }, 0, 8));
            sliderWidgets.Add(new Widget<float>("MapShift", () => MapShift, (value) => { MapShift = (int)value; }, OctantMap.MinShift, OctantMap.MaxShift));

            toggleWidgets.Add(new Widget<bool>("Use Raycast", () => UseRaycast, (value) => { UseRaycast = value; }));
        }
        
        public unsafe void RenderObjects(Buffer buffer, IEnumerable<(Matrix4x4, RawOctree)> instances) {
            // PixelCount = 0;
            // QuadCount = 0;
            // CulledCount = 0;
            // OccludedCount = 0;
            // NodeCount = 0;

            octantMap.Resize(MapShift);

            int w = buffer.Width, h = buffer.Height, bufShift = buffer.TileShift;
            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (uint* queues = OctantOrder.Queues)
            fixed (Delta* deltas = this.deltas)
            fixed (StackEntry* stack = this.stack)
            fixed (int* map = octantMap.Data) {
                foreach (var (matrix, octree) in instances) {
                    int maxDepth = octree.depth;
                    int node = octree.root_node;
                    var color = octree.root_color;
                    fixed (int* nodes = octree.nodes)
                    fixed (Color32* colors = octree.colors) {
                        Render(buf, w, h, bufShift, queues, deltas, stack, map,
                            in matrix, maxDepth, node, color, nodes, colors);
                    }
                }
            }
        }

        unsafe void Render(Buffer.DataItem* buf, int w, int h, int bufShift,
            uint* queues, Delta* deltas, StackEntry* stack, int* map,
            in Matrix4x4 matrix, int maxDepth, int rootNode, Color32 rootColor, int* nodes, Color32* colors)
        {
            Setup(in matrix, ref maxDepth, out int potShift, out int centerX, out int centerY,
                out int boundsX0, out int boundsY0, out int boundsX1, out int boundsY1);
            
            int forwardKey = OctantOrder.Key(in matrix);
            int reverseKey = forwardKey ^ 0b11100000000;
            uint* forwardQueues = queues + forwardKey;
            uint* reverseQueues = queues + reverseKey;
            
            int potSize = 1 << potShift;
            int potHalf = potSize >> 1;
            int potShiftDelta = PrecisionShift - potShift;
            
            int pixelSize = (1 << potShiftDelta);
            int pixelHalf = pixelSize >> 1;
            
            int mapShift = octantMap.SizeShift;
            int mapSize = octantMap.Size;
            int toMapShift = PrecisionShift - mapShift;
            
            // pixel space: 1 = pixel
            // local space: within the PrecisionShift box
            // map space: 1 = map pixel
            
            int startX = centerX - potHalf;
            int startY = centerY - potHalf;
            int endX = centerX + potHalf;
            int endY = centerY + potHalf;
            
            // min is inclusive, max is exclusive
            int minPX = startX + ((boundsX0+pixelHalf) >> potShiftDelta);
            int minPY = startY + ((boundsY0+pixelHalf) >> potShiftDelta);
            int maxPX = startX + ((boundsX1+pixelHalf) >> potShiftDelta);
            int maxPY = startY + ((boundsY1+pixelHalf) >> potShiftDelta);
            
            maxDepth -= 1; // draw at 1 level above max depth
            maxDepth = Mathf.Clamp(maxDepth, 0, MaxLevel);
            
            int xShift = toMapShift;
            int yShift = toMapShift - mapShift;
            int yMask = ~((1 << mapShift) - 1);
            
            int defaultZ = int.MaxValue;
            
            if (!UseRaycast)
            {
                var curr = stack + 1;
                curr->state.depth = 1;
                curr->state.node = rootNode;
                curr->state.pixelShift = potShiftDelta;
                curr->state.pixelSize = 1 << curr->state.pixelShift;
                curr->state.x0 = (minPX < 0 ? 0 : minPX);
                curr->state.y0 = (minPY < 0 ? 0 : minPY);
                curr->state.x1 = (maxPX >= w ? w-1 : maxPX);
                curr->state.y1 = (maxPY >= h ? h-1 : maxPY);
                curr->state.mx0 = ((curr->state.x0 - startX) << potShiftDelta) + pixelHalf;
                curr->state.my0 = ((curr->state.y0 - startY) << potShiftDelta) + pixelHalf;
                curr->state.mx1 = ((curr->state.x1 - startX) << potShiftDelta) + pixelHalf;
                curr->state.my1 = ((curr->state.y1 - startY) << potShiftDelta) + pixelHalf;
                
                while (curr > stack) {
                    var state = curr->state;
                    --curr;
                    
                    if (state.depth >= maxDepth) {
                        var colorData = colors + ((state.node >> (8-3)) & (0xFFFFFF << 3));
                        for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                            var bufY = buf + (y << bufShift);
                            var mapY = map + ((my >> toMapShift) << mapShift);
                            for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                                int mask = mapY[mx >> toMapShift] & state.node;
                                if ((mask != 0) & (bufY[x].depth == defaultZ)) {
                                    int octant = unchecked((int)(forwardQueues[mask] & 7));
                                    bufY[x].color = colorData[octant];
                                    bufY[x].depth = 1;
                                }
                            }
                        }
                        continue;
                    }
                    
                    int lastMY = state.my0;
                    
                    // Occlusion test
                    for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                        var bufY = buf + (y << bufShift);
                        var mapY = map + ((my >> toMapShift) << mapShift);
                        for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                            int mask = mapY[mx >> toMapShift] & state.node;
                            if ((mask != 0) & (bufY[x].depth == defaultZ)) {
                                lastMY = my;
                                goto traverse;
                            }
                        }
                    }
                    continue;
                    traverse:;
                    
                    var queue = reverseQueues[state.node & 0xFF];
                    var nodeData = nodes + ((state.node >> (8-3)) & (0xFFFFFF << 3));
                    
                    int borderX0 = state.mx0 - (state.pixelSize-1);
                    int borderY0 = state.my0 - (state.pixelSize-1);
                    int borderX1 = state.mx1 + (state.pixelSize-1);
                    int borderY1 = state.my1 + (state.pixelSize-1);
                    
                    lastMY = lastMY - borderY0;
                    
                    for (; queue != 0; queue >>= 4) {
                        int octant = unchecked((int)(queue & 7));
                        
                        int dx0 = deltas[octant].x0 - borderX0;
                        int dy0 = deltas[octant].y0 - borderY0;
                        int dx1 = borderX1 - deltas[octant].x1;
                        int dy1 = borderY1 - deltas[octant].y1;
                        dy0 = (dy0 > lastMY ? dy0 : lastMY);
                        dx0 = (dx0 < 0 ? 0 : dx0) >> state.pixelShift;
                        dy0 = (dy0 < 0 ? 0 : dy0) >> state.pixelShift;
                        dx1 = (dx1 < 0 ? 0 : dx1) >> state.pixelShift;
                        dy1 = (dy1 < 0 ? 0 : dy1) >> state.pixelShift;
                        
                        int x0 = state.x0 + dx0;
                        int y0 = state.y0 + dy0;
                        int x1 = state.x1 - dx1;
                        int y1 = state.y1 - dy1;
                        
                        if ((x0 > x1) | (y0 > y1)) continue;
                        
                        ++curr;
                        curr->state.depth = state.depth+1;
                        curr->state.node = nodeData[octant];
                        curr->state.pixelShift = state.pixelShift + 1;
                        curr->state.pixelSize = state.pixelSize << 1;
                        curr->state.x0 = x0;
                        curr->state.y0 = y0;
                        curr->state.x1 = x1;
                        curr->state.y1 = y1;
                        curr->state.mx0 = (state.mx0 + (dx0 << state.pixelShift) - deltas[octant].x) << 1;
                        curr->state.my0 = (state.my0 + (dy0 << state.pixelShift) - deltas[octant].y) << 1;
                        curr->state.mx1 = curr->state.mx0 + ((x1 - x0) << curr->state.pixelShift);
                        curr->state.my1 = curr->state.my0 + ((y1 - y0) << curr->state.pixelShift);
                    }
                }
            }
            else
            {
                var drawing = stack + maxDepth;
                
                int x0 = (minPX < 0 ? 0 : minPX);
                int y0 = (minPY < 0 ? 0 : minPY);
                int x1 = (maxPX > w ? w : maxPX);
                int y1 = (maxPY > h ? h : maxPY);
                for (int y = y0, my = ((y - startY) << potShiftDelta) + pixelHalf; y < y1; y++, my += pixelSize) {
                    int iy = y << bufShift;
                    int imapY = (my >> toMapShift) << mapShift;
                    for (int x = x0, mx = ((x - startX) << potShiftDelta) + pixelHalf; x < x1; x++, mx += pixelSize) {
                        int mapX = (mx >> toMapShift);
                        
                        int mask = map[mapX | imapY] & rootNode;
                        if (mask == 0) goto skip;
                        
                        stack->x = mx;
                        stack->y = my;
                        stack->queue = forwardQueues[mask];
                        stack->offset = (rootNode >> (8-3)) & (0xFFFFFF << 3);
                        
                        var curr = stack;
                        var next = curr + 1;
                        
                        while (curr != drawing) {
                            while (curr->queue == 0) {
                                if (curr == stack) goto skip;
                                next = curr--;
                            }
                            
                            int octant = unchecked((int)(curr->queue & 7));
                            curr->queue >>= 4;
                            
                            next->x = (curr->x - deltas[octant].x) << 1;
                            next->y = (curr->y - deltas[octant].y) << 1;
                            int node = nodes[curr->offset|octant];
                            
                            mask = map[((next->y >> yShift) & yMask) | (next->x >> xShift)] & node;
                            if (mask == 0) continue;
                            
                            next->queue = forwardQueues[mask];
                            next->offset = (node >> (8-3)) & (0xFFFFFF << 3);
                            
                            curr = next++;
                        }
                        
                        {
                            int octant = unchecked((int)(curr->queue & 7));
                            buf[x|iy].color = colors[curr->offset|octant];
                        }
                        
                        skip:;
                    }
                }
            }
        }
        
        unsafe void Setup(in Matrix4x4 matrix, ref int maxDepth, out int potShift, out int Cx, out int Cy,
            out int minX, out int minY, out int maxX, out int maxY)
        {
            // Shape / size distortion is less noticeable than the presence of gaps
            
            var X = new Vector3 {x = matrix.m00, y = matrix.m10, z = matrix.m20};
            var Y = new Vector3 {x = matrix.m01, y = matrix.m11, z = matrix.m21};
            var Z = new Vector3 {x = matrix.m02, y = matrix.m12, z = matrix.m22};
            var T = new Vector3 {x = matrix.m03, y = matrix.m13, z = matrix.m23};
            
            var XN = ((Vector2)X).normalized;
            var YN = ((Vector2)Y).normalized;
            var ZN = ((Vector2)Z).normalized;
            
            float extentXf = (X.x < 0 ? -X.x : X.x) + (Y.x < 0 ? -Y.x : Y.x) + (Z.x < 0 ? -Z.x : Z.x);
            float extentYf = (X.y < 0 ? -X.y : X.y) + (Y.y < 0 ? -Y.y : Y.y) + (Z.y < 0 ? -Z.y : Z.y);
            // We need 2-pixel margin to make sure that an intersection at level N is inside the map at level N+1
            float mapScaleFactor = octantMap.Size / (octantMap.Size - 4f);
            // Also, add 2 extra pixels (without that, there may be index-out-of-bounds errors in octantMap.Bake())
            int pixelSize = (int)((Mathf.Max(extentXf, extentYf) + 2) * mapScaleFactor);
            
            // Power-of-two bounding square
            potShift = 2;
            for (int potSize = 1 << potShift; potSize < pixelSize; potShift++, potSize <<= 1);
            
            int potShiftDelta = PrecisionShift - potShift;
            int potScale = 1 << potShiftDelta;
            
            int half = 1 << (PrecisionShift - 1);
            
            Cx = (int)T.x;
            Cy = (int)T.y;
            
            // Make hexagon slightly larger to make sure there will be
            // no gaps between this node and the neighboring nodes
            int margin = 2;
            int Xx = ((int)(X.x*potScale))+((int)(XN.x*margin)) >> 1;
            int Xy = ((int)(X.y*potScale))+((int)(XN.y*margin)) >> 1;
            int Yx = ((int)(Y.x*potScale))+((int)(YN.x*margin)) >> 1;
            int Yy = ((int)(Y.y*potScale))+((int)(YN.y*margin)) >> 1;
            int Zx = ((int)(Z.x*potScale))+((int)(ZN.x*margin)) >> 1;
            int Zy = ((int)(Z.y*potScale))+((int)(ZN.y*margin)) >> 1;
            int Tx = (((int)((T.x - Cx)*potScale)) >> 1) + half;
            int Ty = (((int)((T.y - Cy)*potScale)) >> 1) + half;
            
            // Snap to 2-grid to align N+1 map with integer coordinates in N map
            int SnapTo2(int value) {
                if ((value & 1) == 0) return value;
                return value < 0 ? value-1 : value+1;
            }
            Xx = SnapTo2(Xx); Xy = SnapTo2(Xy);
            Yx = SnapTo2(Yx); Yy = SnapTo2(Yy);
            Zx = SnapTo2(Zx); Zy = SnapTo2(Zy);
            Tx = SnapTo2(Tx); Ty = SnapTo2(Ty);
            
            int extentX = (Xx < 0 ? -Xx : Xx) + (Yx < 0 ? -Yx : Yx) + (Zx < 0 ? -Zx : Zx);
            int extentY = (Xy < 0 ? -Xy : Xy) + (Yy < 0 ? -Yy : Yy) + (Zy < 0 ? -Zy : Zy);
            minX = Tx - extentX;
            minY = Ty - extentY;
            maxX = Tx + extentX;
            maxY = Ty + extentY;
            
            int maxSize = 1 + 2 * (extentX > extentY ? extentX : extentY);
            int drawDepth = 1;
            while ((1 << (potShiftDelta + drawDepth)) < maxSize) drawDepth++;
            if (maxDepth > drawDepth) maxDepth = drawDepth;
            
            // Baking
            octantMap.Bake(Xx, Xy, Yx, Yy, Zx, Zy, Tx, Ty, PrecisionShift);
            
            int offsetX = Tx - (half >> 1), offsetY = Ty - (half >> 1);
            int octant = 0;
            for (int subZ = -1; subZ <= 1; subZ += 2) {
                for (int subY = -1; subY <= 1; subY += 2) {
                    for (int subX = -1; subX <= 1; subX += 2) {
                        deltas[octant].x = offsetX + ((Xx * subX + Yx * subY + Zx * subZ) >> 1);
                        deltas[octant].y = offsetY + ((Xy * subX + Yy * subY + Zy * subZ) >> 1);
                        deltas[octant].x0 = deltas[octant].x + (minX >> 1);
                        deltas[octant].y0 = deltas[octant].y + (minY >> 1);
                        deltas[octant].x1 = deltas[octant].x + (maxX >> 1);
                        deltas[octant].y1 = deltas[octant].y + (maxY >> 1);
                        ++octant;
                    }
                }
            }
        }
    }
}
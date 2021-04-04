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

        public void RenderStart(Color32 background, bool clearColor = true) {
            stopwatch.Restart();
            Clear(background, clearColor);
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

            int dataSize = tileSize * tileSize * TileCountX * TileCountY;
            if ((Data != null) && (Data.Length >= dataSize)) return;

            Data = new DataItem[dataSize];

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

        public unsafe void Clear(Color32 background, bool clearColor = true) {
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
                                if (clearColor) {
                                  *tile_x = clear_data;  
                                } else {
                                    tile_x->depth = clear_data.depth;
                                }
                            }
                        }
                    }
                }
            }
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

        List<(float, ModelInstance)> visible_objects = new List<(float, ModelInstance)>(64);

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
            SliderWidgets.Add(new Widget<float>("Target FPS", () => targetFrameRate, (value) => { targetFrameRate = (int)value; }, 20, 60, 5));

            ToggleWidgets.Clear();
            ToggleWidgets.Add(new Widget<bool>("Fullscreen", () => Screen.fullScreen,
                (value) => { if (Screen.fullScreen != value) Screen.fullScreen = value; }));

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
            
            foreach (var instance in ModelInstance.All) {
                if (GeometryUtility.TestPlanesAABB(frustum_planes, instance.BoundingBox)) {
                    var pos = instance.transform.position;
                    float sort_z = pos.x * vp_matrix.m20 + pos.y * vp_matrix.m21 + pos.z * vp_matrix.m22;
                    visible_objects.Add((sort_z, instance));
                }
            }
            
            visible_objects.Sort((itemA, itemB) => {
                return itemA.Item1.CompareTo(itemB.Item1);
            });
        }

        void RenderMain() {
            UpdateCameraInfo();
            CollectRenderers();

            buffer.RenderStart(cam.backgroundColor, splatter.StencilMaskBits == 0);
            
            splatter.RenderObjects(buffer, IterateInstances());
            
            buffer.RenderEnd(depth_display_shift);
        }
        
        IEnumerable<(ModelInstance, Matrix4x4, int)> IterateInstances() {
            float depth_scale = (1 << depth_resolution) / (cam.farClipPlane - cam.nearClipPlane);
            var depth_scale_matrix = Matrix4x4.Scale(new Vector3(1, 1, depth_scale));
            
            foreach (var (sort_z, instance) in visible_objects) {
                var voxel_scale_matrix = Matrix4x4.Scale(Vector3.one * instance.Model.Bounds.size.z); // for now
                var obj2world = instance.transform.localToWorldMatrix * voxel_scale_matrix;
                var mvp_matrix = vp_matrix * obj2world;
                mvp_matrix = depth_scale_matrix * mvp_matrix;
                yield return (instance, mvp_matrix, 0); // for now
            }
        }
    }
    
    public struct NodeInfo {
        public int Address; // where to load the subnode data from
        public byte Mask;
        public Color24 Color;
    }
    
    public struct Color24 {
        public byte R, G, B; // Alpha isn't used in splatting anyway
    }
    
    class Splatter {
        const int PrecisionShift = 30;
        const int PrecisionMask = ((1 << PrecisionShift) - 1);
        const int PrecisionMaskInv = ~PrecisionMask;
        
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
            public int depth, pixelShift, pixelSize;
            public int parentOffset, readIndex, loadAddress;
        }
        
        // These are used for faster copying
        struct IndexCache8 {
            public int n0, n1, n2, n3, n4, n5, n6, n7;
        }
        struct InfoCache8 {
            public NodeInfo n0, n1, n2, n3, n4, n5, n6, n7;
        }
        
        int[] indexCache0 = new int[4*8*1024*1024];
        int[] indexCache1 = new int[4*8*1024*1024];
        NodeInfo[] infoCache0 = new NodeInfo[4*8*1024*1024];
        NodeInfo[] infoCache1 = new NodeInfo[4*8*1024*1024];
        Dictionary<(ModelInstance, int, int), int> sourceCacheOffsets = new Dictionary<(ModelInstance, int, int), int>();
        Dictionary<(ModelInstance, int, int), int> targetCacheOffsets = new Dictionary<(ModelInstance, int, int), int>();
        
        public int MapShift = 5;
        OctantMap octantMap = new OctantMap();
        
        public int MaxLevel = 0;
        
        public float DrawBias = 1;
        
        public int StencilMaskBits = 0;
        
        public bool UseMap = true;
        
        public bool UseMaxCondition = true;
        
        public int CacheCount;
        
        public bool UpdateCache = true;
        
        public void UpdateWidgets(List<Widget<string>> infoWidgets, List<Widget<float>> sliderWidgets, List<Widget<bool>> toggleWidgets) {
            // infoWidgets.Add(new Widget<string>($"PixelCount={PixelCount}"));
            // infoWidgets.Add(new Widget<string>($"QuadCount={QuadCount}"));
            // infoWidgets.Add(new Widget<string>($"CulledCount={CulledCount}"));
            // infoWidgets.Add(new Widget<string>($"OccludedCount={OccludedCount}"));
            // infoWidgets.Add(new Widget<string>($"NodeCount={NodeCount}"));
            infoWidgets.Add(new Widget<string>($"CacheCount={CacheCount}"));

            sliderWidgets.Add(new Widget<float>("Level", () => MaxLevel, (value) => { MaxLevel = (int)value; }, 0, 16));
            sliderWidgets.Add(new Widget<float>("MapShift", () => MapShift, (value) => { MapShift = (int)value; }, OctantMap.MinShift, OctantMap.MaxShift));
            sliderWidgets.Add(new Widget<float>("DrawBias", () => DrawBias, (value) => { DrawBias = value; }, 0.25f, 4f));
            sliderWidgets.Add(new Widget<float>("StencilMask", () => StencilMaskBits, (value) => { StencilMaskBits = (int)value; }, 0, 4));

            toggleWidgets.Add(new Widget<bool>("Use Map", () => UseMap, (value) => { UseMap = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Max", () => UseMaxCondition, (value) => { UseMaxCondition = value; }));
            toggleWidgets.Add(new Widget<bool>("Update Cache", () => UpdateCache, (value) => { UpdateCache = value; }));
        }
        
        public unsafe void RenderObjects(Buffer buffer, IEnumerable<(ModelInstance, Matrix4x4, int)> instances) {
            // PixelCount = 0;
            // QuadCount = 0;
            // CulledCount = 0;
            // OccludedCount = 0;
            // NodeCount = 0;

            CacheCount = 0;
            targetCacheOffsets.Clear();
            // The zeroth element is used for "writing cached parent reference" of root nodes
            // (the value is not used, this just avoids extra checks)
            int writeIndex = 2;

            octantMap.Resize(MapShift);

            int w = buffer.Width, h = buffer.Height, bufShift = buffer.TileShift;
            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (uint* queues = OctantOrder.Queues)
            fixed (Delta* deltas = this.deltas)
            fixed (StackEntry* stack = this.stack)
            fixed (int* map = octantMap.Data)
            fixed (int* readIndexCache = indexCache0)
            fixed (int* writeIndexCache = indexCache1)
            fixed (NodeInfo* readInfoCache = infoCache0)
            fixed (NodeInfo* writeInfoCache = infoCache1)
            {
                MaskDepth(buf, w, h, bufShift);

                foreach (var (instance, matrix, partIndex) in instances) {
                    var model = instance.Model;
                    var part = model.Parts[partIndex];
                    var geometryIndex = part.Geometries[0];
                    var octree = model.Geometries[geometryIndex] as RawOctree;

                    int maxDepth = octree.MaxLevel;
                    int node = octree.RootNode;
                    var color = octree.RootColor;
                    int mask = node & 0xFF;
                    fixed (int* nodes = octree.Nodes)
                    fixed (Color32* colors = octree.Colors)
                    {
                        var cacheOffsetKey = (instance, partIndex, geometryIndex);
                        
                        int writeIndexStart = writeIndex;
                        
                        if (!sourceCacheOffsets.TryGetValue(cacheOffsetKey, out var readIndex)) {
                            readIndex = -1;
                        }
                        
                        readIndex = (readIndex << 8) | mask;
                        
                        Render(buf, w, h, bufShift, queues, deltas, stack, map,
                            in matrix, maxDepth, node, color, nodes, colors,
                            readIndexCache, writeIndexCache, readInfoCache, writeInfoCache,
                            readIndex, ref writeIndex, LoadNode);
                        
                        if (writeIndex > writeIndexStart) {
                            targetCacheOffsets[cacheOffsetKey] = writeIndexStart;
                        }
                    }
                }
            }
            
            CacheCount = writeIndex;
            
            (indexCache0, indexCache1) = (indexCache1, indexCache0);
            (infoCache0, infoCache1) = (infoCache1, infoCache0);
            (sourceCacheOffsets, targetCacheOffsets) = (targetCacheOffsets, sourceCacheOffsets);
        }

        unsafe void MaskDepth(Buffer.DataItem* buf, int w, int h, int bufShift) {
            if (StencilMaskBits <= 0) return;
            
            int bits = StencilMaskBits;
            int mask = (1 << bits) - 1;
            int frame = Time.frameCount;
            int xValue = frame & mask;
            int yValue = (frame >> bits) & mask;
            
            for (int y = 0; y < h; y++) {
                int iy = y << bufShift;
                for (int x = 0; x < w; x++) {
                    if (((x & mask) == xValue) & ((y & mask) == yValue)) continue;
                    buf[x|iy].depth = 1;
                }
            }
        }

        unsafe delegate void LoadFuncDelegate(int loadAddress, NodeInfo* info8, int* nodes, Color32* colors);

        unsafe void Render(Buffer.DataItem* buf, int w, int h, int bufShift,
            uint* queues, Delta* deltas, StackEntry* stack, int* map,
            in Matrix4x4 matrix, int maxDepth, int rootNode, Color32 rootColor, int* nodes, Color32* colors,
            int* readIndexCache, int* writeIndexCache, NodeInfo* readInfoCache, NodeInfo* writeInfoCache,
            int readIndex, ref int writeIndex, LoadFuncDelegate loadFunc)
        {
            maxDepth++;
            
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
            int xShift1 = xShift - 1;
            int yShift1 = yShift - 1;
            
            int defaultZ = int.MaxValue;
            
            int ignoreMap = (UseMap ? 0 : 0xFF);
            
            bool useMax = UseMaxCondition;
            
            bool updateCache = UpdateCache;
            
            IndexCache8 emptyIndices = new IndexCache8 {n0=-1, n1=-1, n2=-1, n3=-1, n4=-1, n5=-1, n6=-1, n7=-1};
            
            var curr = stack + 1;
            curr->state.depth = 1;
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
            
            curr->state.parentOffset = 0;
            curr->state.readIndex = readIndex;
            curr->state.loadAddress = rootNode;
            
            while (curr > stack) {
                var state = curr->state;
                --curr;
                
                int nodeMask = state.readIndex & 0xFF;
                
                int lastMY = state.my0;
                
                // Occlusion test
                for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                    var bufY = buf + (y << bufShift);
                    var mapY = map + ((my >> toMapShift) << mapShift);
                    for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                        int mask = mapY[mx >> toMapShift] & nodeMask;
                        if (((mask|ignoreMap) != 0) & (bufY[x].depth == defaultZ)) {
                            lastMY = my;
                            goto traverse;
                        }
                    }
                }
                continue;
                traverse:;
                
                // Calculate base parent offset for this node's children
                int parentOffset = writeIndex << 3;
                var info8 = writeInfoCache + parentOffset;
                var index8 = writeIndexCache + parentOffset;
                var index8read = index8;
                
                // Write reference to this cached node in the parent
                writeIndexCache[state.parentOffset] = writeIndex;
                
                // Clear the cached node references
                *((IndexCache8*)index8) = emptyIndices;
                
                if (state.readIndex < 0) {
                    loadFunc(state.loadAddress, info8, nodes, colors);
                } else {
                    int _readIndex = state.readIndex >> 8;
                    index8read = readIndexCache + (_readIndex << 3);
                    *((InfoCache8*)info8) = ((InfoCache8*)readInfoCache)[_readIndex];
                }
                
                writeIndex += 1;
                
                ///////////////////////////////////////////
                
                bool sizeCondition = (useMax ? (state.x1-state.x0 < 2) | (state.y1-state.y0 < 2) : (((state.x1-state.x0) | (state.y1-state.y0)) < 2));
                bool shouldDraw = (state.depth >= maxDepth) | sizeCondition;
                shouldDraw |= !updateCache & (state.readIndex < 0);
                
                if (!shouldDraw) {
                    var queue = reverseQueues[nodeMask];
                    
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
                        
                        curr->state.parentOffset = parentOffset | octant;
                        curr->state.readIndex = (index8read[octant] << 8) | info8[octant].Mask;
                        curr->state.loadAddress = info8[octant].Address;
                    }
                } else {
                    for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                        var bufY = buf + (y << bufShift);
                        var mapY = map + ((my >> toMapShift) << mapShift);
                        for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                            int mask = mapY[mx >> toMapShift] & nodeMask;
                            if ((mask != 0) & (bufY[x].depth == defaultZ)) {
                                int octant = unchecked((int)(forwardQueues[mask] & 7));
                                var color24 = info8[octant].Color;
                                bufY[x].color = new Color32 {r=color24.R, g=color24.G, b=color24.B, a=255};
                                bufY[x].depth = 1;
                            }
                        }
                    }
                }
            }
        }
        
        static unsafe void LoadNode(int loadAddress, NodeInfo* info8, int* nodes, Color32* colors) {
            int offset = (loadAddress >> (8-3)) & (0xFFFFFF << 3);
            for (int octant = 0; octant < 8; octant++) {
                int octantOffset = offset|octant;
                var info = info8 + octant;
                info->Address = nodes[octantOffset];
                info->Mask = (byte)(info->Address & 0xFF);
                info->Color.R = colors[octantOffset].r;
                info->Color.G = colors[octantOffset].g;
                info->Color.B = colors[octantOffset].b;
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
            
            // int maxSize = 1 + 2 * (extentX > extentY ? extentX : extentY);
            // int drawDepth = 1;
            // while ((1 << (potShiftDelta + drawDepth)) < maxSize) drawDepth++;
            // if (maxDepth > drawDepth) maxDepth = drawDepth;
            
            float maxSize = 2 * (extentXf > extentYf ? extentXf : extentYf) * DrawBias;
            int drawDepth = 1;
            while ((1 << drawDepth) < maxSize) drawDepth++;
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
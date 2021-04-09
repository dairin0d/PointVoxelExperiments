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
            public int stencil;
            public int depth;
            public Color32 color;
            public int id;
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

        public bool Subsample = false;
        private bool wasSubsample = false;

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

            if (Texture && (w == Width) && (h == Height) && (wasSubsample == Subsample)) {
                if (tileSize != TileSize) Resize(w, h, tileSize);
                return;
            }

            if (Texture) UnityEngine.Object.Destroy(Texture);

            int w2 = w, h2 = h;
            if (Subsample) {
                w2 *= 2;
                h2 *= 2;
            }
            wasSubsample = Subsample;

            Texture = new Texture2D(w2, h2, TextureFormat.RGBA32, false);
            Texture.filterMode = FilterMode.Point;

            colors = new Color32[w2 * h2];

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
            int w = Width;
            int h = Height;
            int shift = TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = TileCountX;
            int tny = TileCountY;

            bool show_depth = (depth_shift >= 0);
            bool show_complexity = (depth_shift < -1);
            int complexity_shift = -1 - depth_shift;

            int w2 = Texture.width, h2 = Texture.height;
            int x2step = 1, y2step = 1;
            int x2start = 0, y2start = 0;
            
            bool useSubsample = Subsample;
            if (Subsample) {
                x2step *= 2;
                y2step *= 2;
                Subsampler.Get(out x2start, out y2start);
            }

            fixed (DataItem* data_ptr = Data)
            fixed (Color32* colors_ptr = colors)
            {
                for (int y = 0, y2 = y2start; y < h; y++, y2 += y2step) {
                    var data_ptr_y = data_ptr + y * tile_size;
                    var colors_ptr_y = colors_ptr + y2 * w2;
                    for (int x = 0, x2 = x2start; x < w; x++, x2 += x2step) {
                        var data_x = data_ptr_y + x;
                        var colors_x = colors_ptr_y + x2;
                        if (show_depth) {
                            byte d = (byte)(data_x->depth >> depth_shift);
                            colors_x->r = colors_x->g = colors_x->b = d;
                            colors_x->a = 255;
                        } else if (show_complexity) {
                            byte d = (byte)(data_x->id << complexity_shift);
                            colors_x->r = colors_x->g = colors_x->b = d;
                            colors_x->a = 255;
                        } else {
                            *colors_x = data_x->color;
                        }
                        
                        if (useSubsample) {
                            for (int subY = 0; subY < 2; subY++) {
                                var colors_sub_y = colors_ptr + (y2-y2start+subY) * w2;
                                for (int subX = 0; subX < 2; subX++) {
                                    if ((subX == x2start) & (subY == y2start)) continue;
                                    var colors_sub_x = colors_sub_y + (x2-x2start+subX);
                                    colors_sub_x->r = (byte)((colors_sub_x->r*3 + colors_x->r + 3) >> 2);
                                    colors_sub_x->g = (byte)((colors_sub_x->g*3 + colors_x->g + 3) >> 2);
                                    colors_sub_x->b = (byte)((colors_sub_x->b*3 + colors_x->b + 3) >> 2);
                                }
                            }
                        }
                    }
                }
            }
        }

        public unsafe void Clear(Color32 background, bool clearColor = true) {
            var clear_data = default(DataItem);
            clear_data.stencil = 0;
            clear_data.depth = int.MaxValue;
            clear_data.color = background;
            clear_data.id = 0;

            int w = Width;
            int h = Height;
            int shift = TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = TileCountX;
            int tny = TileCountY;

            fixed (DataItem* data_ptr = Data)
            {
                for (int y = 0; y < h; y++) {
                    var data_ptr_y = data_ptr + y * tile_size;
                    for (int x = 0; x < w; x++) {
                        var data_x = data_ptr_y + x;
                        if (clearColor) {
                            *data_x = clear_data;
                        } else {
                            data_x->depth = clear_data.depth;
                            data_x->stencil = clear_data.stencil;
                            data_x->id = clear_data.id;
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
        public int VSyncCount = 0;
        public int TargetFrameRate = 30;

        Camera cam;
        int cullingMask;
        CameraClearFlags clearFlags;

        Matrix4x4 vpMatrix;
        Plane[] frustumPlanes;

        public int RenderSize = 0;
        Buffer buffer;

        public int DepthResolution = 16;
        Splatter splatter;

        public int DepthDisplayShift = -1;

        List<(float, ModelInstance)> visibleObjects = new List<(float, ModelInstance)>(64);

        List<Widget<string>> InfoWidgets = new List<Widget<string>>(32);
        List<Widget<float>> SliderWidgets = new List<Widget<float>>(32);
        List<Widget<bool>> ToggleWidgets = new List<Widget<bool>>(32);

        Material mat;

        public dairin0d.Controls.PlayerController Controller;

        // ========== Unity events ========== //

        void Start() {
            if (!Application.isEditor) Screen.SetResolution(640, 480, false);

            cam = GetComponent<Camera>();

            buffer = new Buffer();

            splatter = new Splatter();
        }

        void Update() {
            QualitySettings.vSyncCount = VSyncCount;
            Application.targetFrameRate = TargetFrameRate;
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
            SliderWidgets.Add(new Widget<float>("Target FPS", () => TargetFrameRate, (value) => { TargetFrameRate = (int)value; }, 20, 60, 5));
            SliderWidgets.Add(new Widget<float>("Depth View", () => DepthDisplayShift, (value) => { DepthDisplayShift = (int)value; }, -8, 24));

            if (Controller) {
                SliderWidgets.Add(new Widget<float>("Move Speed", () => Controller.speed, (value) => { Controller.speed = value; }, 0.01f, 0.5f));
            }

            ToggleWidgets.Clear();
            ToggleWidgets.Add(new Widget<bool>("Fullscreen", () => Screen.fullScreen,
                (value) => { if (Screen.fullScreen != value) Screen.fullScreen = value; }));
            ToggleWidgets.Add(new Widget<bool>("Subsample", () => buffer.Subsample, (value) => { buffer.Subsample = value; }));

            splatter.UpdateWidgets(InfoWidgets, SliderWidgets, ToggleWidgets);
        }

        void UpdateCameraInfo() {
            int w = buffer.Width, h = buffer.Height;

            // vp_matrix = cam.projectionMatrix * cam.worldToCameraMatrix;
            // vp_matrix = Matrix4x4.Scale(new Vector3(w * 0.5f, h * 0.5f, 1)) * vp_matrix;
            // vp_matrix = Matrix4x4.Translate(new Vector3(w * 0.5f, h * 0.5f, 0f)) * vp_matrix;

            float ah = cam.orthographicSize;
            float aw = (ah * w) / h;
            vpMatrix = cam.worldToCameraMatrix;
            vpMatrix = Matrix4x4.Scale(new Vector3(w * 0.5f / aw, h * 0.5f / ah, -1)) * vpMatrix;
            vpMatrix = Matrix4x4.Translate(new Vector3(w * 0.5f, h * 0.5f, 0f)) * vpMatrix;

            frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
        }

        void CollectRenderers() {
            visibleObjects.Clear();
            
            foreach (var instance in ModelInstance.All) {
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, instance.BoundingBox)) {
                    var pos = instance.transform.position;
                    float sort_z = pos.x * vpMatrix.m20 + pos.y * vpMatrix.m21 + pos.z * vpMatrix.m22;
                    visibleObjects.Add((sort_z, instance));
                }
            }
            
            visibleObjects.Sort((itemA, itemB) => {
                return itemA.Item1.CompareTo(itemB.Item1);
            });
        }

        void RenderMain() {
            UpdateCameraInfo();
            CollectRenderers();

            buffer.RenderStart(cam.backgroundColor);
            
            splatter.RenderObjects(buffer, IterateInstances());
            
            buffer.RenderEnd(DepthDisplayShift);
        }
        
        IEnumerable<(ModelInstance, Matrix4x4, int)> IterateInstances() {
            float depth_scale = (1 << DepthResolution) / (cam.farClipPlane - cam.nearClipPlane);
            var depth_scale_matrix = Matrix4x4.Scale(new Vector3(1, 1, depth_scale));
            
            Vector2 subsampleOffset = default;
            if (buffer.Subsample) {
                Subsampler.Get(out int subsampleX, out int subsampleY);
                subsampleOffset.x = (subsampleX - 0.5f) * 0.5f;
                subsampleOffset.y = (subsampleY - 0.5f) * 0.5f;
            }
            
            foreach (var (sort_z, instance) in visibleObjects) {
                var voxel_scale_matrix = Matrix4x4.Scale(Vector3.one * instance.Model.Bounds.size.z); // for now
                var obj2world = instance.transform.localToWorldMatrix * voxel_scale_matrix;
                var mvp_matrix = vpMatrix * obj2world;
                mvp_matrix = depth_scale_matrix * mvp_matrix;
                mvp_matrix.m03 -= subsampleOffset.x;
                mvp_matrix.m13 -= subsampleOffset.y;
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
    
    static class Subsampler {
        public static void Get(out int x, out int y) {
            switch (Time.frameCount & 0b11) {
                case 0: x = 0; y = 0; return;
                case 1: x = 1; y = 1; return;
                case 2: x = 1; y = 0; return;
                case 3: x = 0; y = 1; return;
            }
            x = 0; y = 0;
        }
    }
    
    class Splatter {
        const int PrecisionShift = 30;
        const int PrecisionMask = ((1 << PrecisionShift) - 1);
        const int PrecisionMaskInv = ~PrecisionMask;
        
        const int SubpixelShift = 16;
        const int SubpixelSize = 1 << SubpixelShift;
        const int SubpixelMask = SubpixelSize - 1;
        const int SubpixelMaskInv = ~SubpixelMask;
        const int SubpixelHalf = SubpixelSize >> 1;
        
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
            public int depth, pixelShift, pixelSize, x, y, z;
            public int parentOffset, readIndex, loadAddress;
            public Color24 color;
        }
        
        // These are used for faster copying
        struct IndexCache8 {
            public int n0, n1, n2, n3, n4, n5, n6, n7;
        }
        struct InfoCache8 {
            public NodeInfo n0, n1, n2, n3, n4, n5, n6, n7;
        }
        
        class NodeCache {
            public int[] indexCache0;
            public int[] indexCache1;
            public NodeInfo[] infoCache0;
            public NodeInfo[] infoCache1;
            public Dictionary<(ModelInstance, int, int), int> sourceCacheOffsets = new Dictionary<(ModelInstance, int, int), int>();
            public Dictionary<(ModelInstance, int, int), int> targetCacheOffsets = new Dictionary<(ModelInstance, int, int), int>();
            
            public NodeCache(int count) {
                int size = count * 8;
                indexCache0 = new int[size];
                indexCache1 = new int[size];
                infoCache0 = new NodeInfo[size];
                infoCache1 = new NodeInfo[size];
            }
            
            public void Swap() {
                (indexCache0, indexCache1) = (indexCache1, indexCache0);
                (infoCache0, infoCache1) = (infoCache1, infoCache0);
                (sourceCacheOffsets, targetCacheOffsets) = (targetCacheOffsets, sourceCacheOffsets);
            }
        }
        const int CacheStorageCount = 2*1024*1024;
        NodeCache[] caches = new NodeCache[] {
            new NodeCache(CacheStorageCount),
            new NodeCache(CacheStorageCount),
            new NodeCache(CacheStorageCount),
            new NodeCache(CacheStorageCount),
        };
        int currentCacheIndex = 0;
        
        public int MapShift = 5;
        OctantMap octantMap = new OctantMap();
        
        public int MaxLevel = 0;
        
        public float DrawBias = 1;
        
        public bool UseMap = true;
        public bool UseMap1D = true;
        
        public bool UseMaxCondition = false;
        
        public int CacheCount;
        public int LoadedCount;
        
        public bool UpdateCache = true;
        
        public bool UseStencil = true;
        
        public int SplatAt = 2;
        
        public int BlendFactor = 0;
        
        public bool UsePoints = false;
        
        bool useSubsample;
        
        public void UpdateWidgets(List<Widget<string>> infoWidgets, List<Widget<float>> sliderWidgets, List<Widget<bool>> toggleWidgets) {
            // infoWidgets.Add(new Widget<string>($"PixelCount={PixelCount}"));
            // infoWidgets.Add(new Widget<string>($"QuadCount={QuadCount}"));
            // infoWidgets.Add(new Widget<string>($"CulledCount={CulledCount}"));
            // infoWidgets.Add(new Widget<string>($"OccludedCount={OccludedCount}"));
            // infoWidgets.Add(new Widget<string>($"NodeCount={NodeCount}"));
            infoWidgets.Add(new Widget<string>($"CacheCount={CacheCount}"));
            infoWidgets.Add(new Widget<string>($"LoadedCount={LoadedCount}"));

            sliderWidgets.Add(new Widget<float>("Level", () => MaxLevel, (value) => { MaxLevel = (int)value; }, 0, 16));
            sliderWidgets.Add(new Widget<float>("MapShift", () => MapShift, (value) => { MapShift = (int)value; }, OctantMap.MinShift, OctantMap.MaxShift));
            sliderWidgets.Add(new Widget<float>("DrawBias", () => DrawBias, (value) => { DrawBias = value; }, 0.25f, 4f));
            sliderWidgets.Add(new Widget<float>("Splat At", () => SplatAt, (value) => { SplatAt = (int)value; }, 1, 8));
            sliderWidgets.Add(new Widget<float>("BlendFactor", () => BlendFactor, (value) => { BlendFactor = (int)value; }, 0, 255));

            toggleWidgets.Add(new Widget<bool>("Use Stencil", () => UseStencil, (value) => { UseStencil = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Points", () => UsePoints, (value) => { UsePoints = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Map", () => UseMap, (value) => { UseMap = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Map 1D", () => UseMap1D, (value) => { UseMap1D = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Max", () => UseMaxCondition, (value) => { UseMaxCondition = value; }));
            toggleWidgets.Add(new Widget<bool>("Update Cache", () => UpdateCache, (value) => { UpdateCache = value; }));
        }
        
        public unsafe void RenderObjects(Buffer buffer, IEnumerable<(ModelInstance, Matrix4x4, int)> instances) {
            // PixelCount = 0;
            // QuadCount = 0;
            // CulledCount = 0;
            // OccludedCount = 0;
            // NodeCount = 0;
            
            useSubsample = buffer.Subsample;
            if (useSubsample) {
                int frame = Time.frameCount;
                currentCacheIndex = frame & 0b11;
            }
            var cache = caches[currentCacheIndex];

            CacheCount = 0;
            LoadedCount = 0;
            cache.targetCacheOffsets.Clear();
            // The zeroth element is used for "writing cached parent reference" of root nodes
            // (the value is not used, this just avoids extra checks)
            int writeIndex = 2;

            octantMap.Resize(MapShift);

            int w = buffer.Width, h = buffer.Height, bufShift = buffer.TileShift;
            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (uint* queues = OctantOrder.Queues)
            fixed (Delta* deltas = this.deltas)
            fixed (StackEntry* stack = this.stack)
            fixed (byte* map = octantMap.Data)
            fixed (byte* xmap = octantMap.DataX)
            fixed (byte* ymap = octantMap.DataY)
            fixed (int* readIndexCache = cache.indexCache0)
            fixed (int* writeIndexCache = cache.indexCache1)
            fixed (NodeInfo* readInfoCache = cache.infoCache0)
            fixed (NodeInfo* writeInfoCache = cache.infoCache1)
            {
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
                        
                        if (!cache.sourceCacheOffsets.TryGetValue(cacheOffsetKey, out var readIndex)) {
                            readIndex = -1;
                        }
                        
                        readIndex = (readIndex << 8) | mask;
                        
                        if (UsePoints) {
                            RenderPoints(buf, w, h, bufShift, queues, deltas, stack, map, xmap, ymap,
                                in matrix, maxDepth, node, color, nodes, colors,
                                readIndexCache, writeIndexCache, readInfoCache, writeInfoCache,
                                readIndex, ref writeIndex, LoadNode);
                        } else {
                            Render(buf, w, h, bufShift, queues, deltas, stack, map, xmap, ymap,
                                in matrix, maxDepth, node, color, nodes, colors,
                                readIndexCache, writeIndexCache, readInfoCache, writeInfoCache,
                                readIndex, ref writeIndex, LoadNode);
                        }
                        
                        if (writeIndex > writeIndexStart) {
                            cache.targetCacheOffsets[cacheOffsetKey] = writeIndexStart;
                        }
                    }
                }
            }
            
            CacheCount = writeIndex;
            
            cache.Swap();
        }

        unsafe delegate void LoadFuncDelegate(int loadAddress, NodeInfo* info8, int* nodes, Color32* colors);

        unsafe void Render(Buffer.DataItem* buf, int w, int h, int bufShift,
            uint* queues, Delta* deltas, StackEntry* stack, byte* map, byte* xmap, byte* ymap,
            in Matrix4x4 matrix, int maxDepth, int rootNode, Color32 rootColor, int* nodes, Color32* colors,
            int* readIndexCache, int* writeIndexCache, NodeInfo* readInfoCache, NodeInfo* writeInfoCache,
            int readIndex, ref int writeIndex, LoadFuncDelegate loadFunc)
        {
            maxDepth++;
            
            Setup(in matrix, ref maxDepth, out int potShift, out int centerX, out int centerY,
                out int boundsX0, out int boundsY0, out int boundsX1, out int boundsY1, out int startZ);
            
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
            
            bool useMap = UseMap;
            bool useMap1D = UseMap1D;
            int ignoreMap = (UseMap ? 0 : 0xFF);
            int ignoreStencil = (UseStencil ? -1 : 0);
            
            int splatAt = SplatAt;
            
            int blendFactor = BlendFactor;
            int blendFactorInv = 255 - blendFactor;
            
            bool useMax = UseMaxCondition;
            
            bool updateCache = UpdateCache;
            
            IndexCache8 emptyIndices = new IndexCache8 {n0=-1, n1=-1, n2=-1, n3=-1, n4=-1, n5=-1, n6=-1, n7=-1};
            
            var curr = stack + 1;
            curr->state.depth = 1;
            curr->state.pixelShift = potShiftDelta;
            curr->state.pixelSize = 1 << curr->state.pixelShift;
            curr->state.z = startZ;
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
            curr->state.color = new Color24 {R=rootColor.r, G=rootColor.g, B=rootColor.b};
            
            {
                var state = curr->state;
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = buf + (y << bufShift);
                    for (int x = state.x0; x <= state.x1; x++) {
                        bufY[x].stencil = 0;
                    }
                }
            }
            
            while (curr > stack) {
                var state = curr->state;
                --curr;
                
                int nodeMask = state.readIndex & 0xFF;
                
                int lastMY = state.my0;
                
                // Occlusion test
                if (useMap) {
                    if (useMap1D) {
                        for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                            var bufY = buf + (y << bufShift);
                            int maskY = ymap[my >> toMapShift] & nodeMask;
                            for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                                int mask = xmap[mx >> toMapShift] & maskY;
                                bufY[x].id += 1;
                                if ((mask != 0) & (state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                                    lastMY = my;
                                    goto traverse;
                                }
                            }
                        }
                    } else {
                        for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                            var bufY = buf + (y << bufShift);
                            var mapY = map + ((my >> toMapShift) << mapShift);
                            for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                                int mask = mapY[mx >> toMapShift] & nodeMask;
                                bufY[x].id += 1;
                                if ((mask != 0) & (state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                                    lastMY = my;
                                    goto traverse;
                                }
                            }
                        }
                    }
                } else {
                    for (int y = state.y0; y <= state.y1; y++) {
                        var bufY = buf + (y << bufShift);
                        for (int x = state.x0; x <= state.x1; x++) {
                            bufY[x].id += 1;
                            if ((state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                                lastMY += (y - state.y0) << state.pixelShift;
                                goto traverse;
                            }
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
                    LoadedCount++;
                } else {
                    int _readIndex = state.readIndex >> 8;
                    index8read = readIndexCache + (_readIndex << 3);
                    *((InfoCache8*)info8) = ((InfoCache8*)readInfoCache)[_readIndex];
                }
                
                writeIndex += 1;
                
                ///////////////////////////////////////////
                
                bool sizeCondition = (useMax
                    ? (state.x1-state.x0 < splatAt) | (state.y1-state.y0 < splatAt)
                    : (state.x1-state.x0 < splatAt) & (state.y1-state.y0 < splatAt));
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
                        curr->state.z = state.z + (deltas[octant].z >> state.depth);
                        curr->state.x0 = x0;
                        curr->state.y0 = y0;
                        curr->state.x1 = x1;
                        curr->state.y1 = y1;
                        curr->state.mx0 = ((state.mx0 + (dx0 << state.pixelShift) - deltas[octant].x) << 1) + 1;
                        curr->state.my0 = ((state.my0 + (dy0 << state.pixelShift) - deltas[octant].y) << 1) + 1;
                        curr->state.mx1 = curr->state.mx0 + ((x1 - x0) << curr->state.pixelShift);
                        curr->state.my1 = curr->state.my0 + ((y1 - y0) << curr->state.pixelShift);
                        
                        curr->state.parentOffset = parentOffset | octant;
                        curr->state.readIndex = (index8read[octant] << 8) | info8[octant].Mask;
                        curr->state.loadAddress = info8[octant].Address;
                        curr->state.color = info8[octant].Color;
                    }
                } else {
                    if (useMap1D) {
                        for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                            var bufY = buf + (y << bufShift);
                            int maskY = ymap[my >> toMapShift] & nodeMask;
                            for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                                int mask = xmap[mx >> toMapShift] & maskY;
                                if ((mask != 0) & (state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                                    int octant = unchecked((int)(forwardQueues[mask] & 7));
                                    int z = state.z + (deltas[octant].z >> state.depth);
                                    bufY[x].id += 1;
                                    if (z < bufY[x].depth) {
                                        var color24 = info8[octant].Color;
                                        bufY[x].color = new Color32 {
                                            // r = color24.R,
                                            // g = color24.G,
                                            // b = color24.B,
                                            r = (byte)((color24.R * blendFactorInv + state.color.R * blendFactor + 255) >> 8),
                                            g = (byte)((color24.G * blendFactorInv + state.color.G * blendFactor + 255) >> 8),
                                            b = (byte)((color24.B * blendFactorInv + state.color.B * blendFactor + 255) >> 8),
                                            a = 255
                                        };
                                        bufY[x].depth = z;
                                        bufY[x].stencil = 1;
                                    }
                                }
                            }
                        }
                    } else {
                        for (int y = state.y0, my = state.my0; y <= state.y1; y++, my += state.pixelSize) {
                            var bufY = buf + (y << bufShift);
                            var mapY = map + ((my >> toMapShift) << mapShift);
                            for (int x = state.x0, mx = state.mx0; x <= state.x1; x++, mx += state.pixelSize) {
                                int mask = mapY[mx >> toMapShift] & nodeMask;
                                if ((mask != 0) & (state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                                    int octant = unchecked((int)(forwardQueues[mask] & 7));
                                    int z = state.z + (deltas[octant].z >> state.depth);
                                    bufY[x].id += 1;
                                    if (z < bufY[x].depth) {
                                        var color24 = info8[octant].Color;
                                        bufY[x].color = new Color32 {
                                            // r = color24.R,
                                            // g = color24.G,
                                            // b = color24.B,
                                            r = (byte)((color24.R * blendFactorInv + state.color.R * blendFactor + 255) >> 8),
                                            g = (byte)((color24.G * blendFactorInv + state.color.G * blendFactor + 255) >> 8),
                                            b = (byte)((color24.B * blendFactorInv + state.color.B * blendFactor + 255) >> 8),
                                            a = 255
                                        };
                                        bufY[x].depth = z;
                                        bufY[x].stencil = 1;
                                    }
                                }
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
        
        void Setup(in Matrix4x4 matrix, ref int maxDepth, out int potShift, out int Cx, out int Cy,
            out int minX, out int minY, out int maxX, out int maxY, out int minZ)
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
            
            float extentXYx = (X.x < 0 ? -X.x : X.x) + (Y.x < 0 ? -Y.x : Y.x);
            float extentXYy = (X.y < 0 ? -X.y : X.y) + (Y.y < 0 ? -Y.y : Y.y);
            float extentYZx = (Y.x < 0 ? -Y.x : Y.x) + (Z.x < 0 ? -Z.x : Z.x);
            float extentYZy = (Y.y < 0 ? -Y.y : Y.y) + (Z.y < 0 ? -Z.y : Z.y);
            float extentZXx = (Z.x < 0 ? -Z.x : Z.x) + (X.x < 0 ? -X.x : X.x);
            float extentZXy = (Z.y < 0 ? -Z.y : Z.y) + (X.y < 0 ? -X.y : X.y);
            
            // int maxSize = 1 + 2 * (extentX > extentY ? extentX : extentY);
            // int drawDepth = 1;
            // while ((1 << (potShiftDelta + drawDepth)) < maxSize) drawDepth++;
            // if (maxDepth > drawDepth) maxDepth = drawDepth;
            
            // float maxSize = 2 * (extentXf > extentYf ? extentXf : extentYf) * DrawBias;
            float maxSize = 2 * Mathf.Max(extentXYx, extentXYy, extentYZx, extentYZy, extentZXx, extentZXy) * DrawBias;
            int drawDepth = 1;
            while ((1 << drawDepth) < maxSize) drawDepth++;
            if (maxDepth > drawDepth) maxDepth = drawDepth;
            
            // Baking
            octantMap.Bake(Xx, Xy, Yx, Yy, Zx, Zy, Tx, Ty, PrecisionShift);
            
            int Xz = ((int)X.z) >> 1;
            int Yz = ((int)Y.z) >> 1;
            int Zz = ((int)Z.z) >> 1;
            int extentZ = (Xz < 0 ? -Xz : Xz) + (Yz < 0 ? -Yz : Yz) + (Zz < 0 ? -Zz : Zz);
            
            minZ = ((int)T.z) - extentZ;
            
            int offsetX = Tx - (half >> 1), offsetY = Ty - (half >> 1);
            int offsetZ = extentZ;
            int octant = 0;
            for (int subZ = -1; subZ <= 1; subZ += 2) {
                for (int subY = -1; subY <= 1; subY += 2) {
                    for (int subX = -1; subX <= 1; subX += 2) {
                        deltas[octant].x = offsetX + ((Xx * subX + Yx * subY + Zx * subZ) >> 1);
                        deltas[octant].y = offsetY + ((Xy * subX + Yy * subY + Zy * subZ) >> 1);
                        deltas[octant].z = offsetZ + (Xz * subX + Yz * subY + Zz * subZ);
                        deltas[octant].x0 = deltas[octant].x + (minX >> 1);
                        deltas[octant].y0 = deltas[octant].y + (minY >> 1);
                        deltas[octant].x1 = deltas[octant].x + (maxX >> 1);
                        deltas[octant].y1 = deltas[octant].y + (maxY >> 1);
                        ++octant;
                    }
                }
            }
        }
        
        unsafe void RenderPoints(Buffer.DataItem* buf, int w, int h, int bufShift,
            uint* queues, Delta* deltas, StackEntry* stack, byte* map, byte* xmap, byte* ymap,
            in Matrix4x4 matrix, int maxDepth, int rootNode, Color32 rootColor, int* nodes, Color32* colors,
            int* readIndexCache, int* writeIndexCache, NodeInfo* readInfoCache, NodeInfo* writeInfoCache,
            int readIndex, ref int writeIndex, LoadFuncDelegate loadFunc)
        {
            SetupPoints(in matrix, out int potShift,
                out int centerX, out int centerY, out int extentX, out int extentY, out int startZ);
            
            if ((extentX < 0) | (extentY < 0)) {
                Debug.LogError($"extent: ({extentX}, {extentY})");
                return;
            }
            
            if (maxDepth > potShift+1) maxDepth = potShift+1;
            
            int marginX = extentX >> (potShift+1);
            int marginY = extentY >> (potShift+1);
            
            maxDepth = Mathf.Clamp(maxDepth, 0, MaxLevel);
            
            int forwardKey = OctantOrder.Key(in matrix);
            int reverseKey = forwardKey ^ 0b11100000000;
            uint* forwardQueues = queues + forwardKey;
            uint* reverseQueues = queues + reverseKey;
            
            int bufMask = (1 << bufShift) - 1;
            int bufMaskInv = ~bufMask;
            
            int ignoreStencil = (UseStencil ? -1 : 0);
            int blendFactor = BlendFactor;
            int blendFactorInv = 255 - blendFactor;
            bool updateCache = UpdateCache;
            IndexCache8 emptyIndices = new IndexCache8 {n0=-1, n1=-1, n2=-1, n3=-1, n4=-1, n5=-1, n6=-1, n7=-1};
            
            var curr = stack + 1;
            curr->state.depth = 1;
            curr->state.x = centerX;
            curr->state.y = centerY;
            curr->state.z = startZ;
            curr->state.x0 = (curr->state.x + SubpixelHalf - extentX) >> SubpixelShift;
            curr->state.y0 = (curr->state.y + SubpixelHalf - extentY) >> SubpixelShift;
            curr->state.x1 = (curr->state.x - SubpixelHalf + extentX) >> SubpixelShift;
            curr->state.y1 = (curr->state.y - SubpixelHalf + extentY) >> SubpixelShift;
            if (curr->state.x0 < 0) curr->state.x0 = 0;
            if (curr->state.y0 < 0) curr->state.y0 = 0;
            if (curr->state.x1 >= w) curr->state.x1 = w-1;
            if (curr->state.y1 >= h) curr->state.y1 = h-1;
            
            curr->state.parentOffset = 0;
            curr->state.readIndex = readIndex;
            curr->state.loadAddress = rootNode;
            curr->state.color = new Color24 {R=rootColor.r, G=rootColor.g, B=rootColor.b};
            
            {
                var state = curr->state;
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = buf + (y << bufShift);
                    for (int x = state.x0; x <= state.x1; x++) {
                        bufY[x].id += 1;
                        bufY[x].stencil = 0;
                    }
                }
            }
            
            while (curr > stack) {
                var state = curr->state;
                --curr;
                
                int nodeMask = state.readIndex & 0xFF;
                
                int lastY = state.y0;
                
                // Occlusion test
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = buf + (y << bufShift);
                    for (int x = state.x0; x <= state.x1; x++) {
                        bufY[x].id += 1;
                        if ((state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                            lastY = y;
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
                    LoadedCount++;
                } else {
                    int _readIndex = state.readIndex >> 8;
                    index8read = readIndexCache + (_readIndex << 3);
                    *((InfoCache8*)info8) = ((InfoCache8*)readInfoCache)[_readIndex];
                }
                
                writeIndex += 1;
                
                ///////////////////////////////////////////
                
                bool sizeCondition = (state.x1-state.x0 < 2) & (state.y1-state.y0 < 2);
                bool shouldDraw = (state.depth >= maxDepth) | sizeCondition;
                shouldDraw |= !updateCache & (state.readIndex < 0);
                
                if (!shouldDraw) {
                    var queue = reverseQueues[nodeMask];
                    
                    int subExtentX = (extentX >> state.depth);
                    int subExtentY = (extentY >> state.depth);
                    if (UseMap) {
                        subExtentX -= SubpixelHalf;
                        subExtentY -= SubpixelHalf;
                    } else {
                        subExtentX -= marginX;
                        subExtentY -= marginY;
                    }
                    
                    for (; queue != 0; queue >>= 4) {
                        int octant = unchecked((int)(queue & 7));
                        
                        int x = state.x + (deltas[octant].x >> state.depth);
                        int y = state.y + (deltas[octant].y >> state.depth);
                        
                        int x0 = (x - subExtentX) >> SubpixelShift;
                        int y0 = (y - subExtentY) >> SubpixelShift;
                        int x1 = (x + subExtentX) >> SubpixelShift;
                        int y1 = (y + subExtentY) >> SubpixelShift;
                        x0 = (x0 < state.x0 ? state.x0 : x0);
                        y0 = (y0 < lastY ? lastY : y0);
                        x1 = (x1 > state.x1 ? state.x1 : x1);
                        y1 = (y1 > state.y1 ? state.y1 : y1);
                        
                        if ((x0 > x1) | (y0 > y1)) continue;
                        
                        ++curr;
                        curr->state.depth = state.depth+1;
                        curr->state.x = x;
                        curr->state.y = y;
                        curr->state.z = state.z + (deltas[octant].z >> state.depth);
                        curr->state.x0 = x0;
                        curr->state.y0 = y0;
                        curr->state.x1 = x1;
                        curr->state.y1 = y1;
                        
                        curr->state.parentOffset = parentOffset | octant;
                        curr->state.readIndex = (index8read[octant] << 8) | info8[octant].Mask;
                        curr->state.loadAddress = info8[octant].Address;
                        curr->state.color = info8[octant].Color;
                    }
                } else if ((state.x1 == state.x0) & (state.y1 == state.y0)) {
                    var pixel = buf + (state.x0 | (state.y0 << bufShift));
                    pixel->color = new Color32 {
                        r = state.color.R,
                        g = state.color.G,
                        b = state.color.B,
                        a = 255
                    };
                    pixel->depth = state.z;
                    pixel->stencil = 1;
                } else if (UseMap) {
                    int mapHalf = 1 << (SubpixelShift + potShift - state.depth);
                    int toMapShift = (SubpixelShift + potShift - state.depth + 1) - octantMap.SizeShift;
                    int sx0 = (state.x0 << SubpixelShift) + SubpixelHalf - (state.x - mapHalf);
                    int sy0 = (state.y0 << SubpixelShift) + SubpixelHalf - (state.y - mapHalf);
                    
                    for (int y = state.y0, my = sy0; y <= state.y1; y++, my += SubpixelSize) {
                        var bufY = buf + (y << bufShift);
                        int maskY = ymap[my >> toMapShift] & nodeMask;
                        for (int x = state.x0, mx = sx0; x <= state.x1; x++, mx += SubpixelSize) {
                            int mask = xmap[mx >> toMapShift] & maskY;
                            if ((mask != 0) & (state.z < bufY[x].depth) & ((bufY[x].stencil & ignoreStencil) == 0)) {
                                int octant = unchecked((int)(forwardQueues[mask] & 7));
                                int z = state.z + (deltas[octant].z >> state.depth);
                                bufY[x].id += 1;
                                if (z < bufY[x].depth) {
                                    var color24 = info8[octant].Color;
                                    bufY[x].color = new Color32 {
                                        // r = color24.R,
                                        // g = color24.G,
                                        // b = color24.B,
                                        r = (byte)((color24.R * blendFactorInv + state.color.R * blendFactor + 255) >> 8),
                                        g = (byte)((color24.G * blendFactorInv + state.color.G * blendFactor + 255) >> 8),
                                        b = (byte)((color24.B * blendFactorInv + state.color.B * blendFactor + 255) >> 8),
                                        a = 255
                                    };
                                    bufY[x].depth = z;
                                    bufY[x].stencil = 1;
                                }
                            }
                        }
                    }
                } else {
                    var queue = forwardQueues[nodeMask];
                    
                    // int subExtentX = (extentX >> state.depth);
                    // int subExtentY = (extentY >> state.depth);
                    // int subExtentX = 1 << (SubpixelShift + potShift - state.depth - 1);
                    // int subExtentY = 1 << (SubpixelShift + potShift - state.depth - 1);
                    
                    for (; queue != 0; queue >>= 4) {
                        int octant = unchecked((int)(queue & 7));
                        
                        int x = state.x + (deltas[octant].x >> state.depth);
                        int y = state.y + (deltas[octant].y >> state.depth);
                        
                        // int x0 = (x - subExtentX) >> SubpixelShift;
                        // int y0 = (y - subExtentY) >> SubpixelShift;
                        // int x1 = (x + subExtentX) >> SubpixelShift;
                        // int y1 = (y + subExtentY) >> SubpixelShift;
                        // x0 = (x0 < state.x0 ? state.x0 : x0);
                        // y0 = (y0 < lastY ? lastY : y0);
                        // x1 = (x1 > state.x1 ? state.x1 : x1);
                        // y1 = (y1 > state.y1 ? state.y1 : y1);
                        
                        // int z = state.z + (deltas[octant].z >> state.depth);
                        
                        // for (y = y0; y <= y1; y++) {
                        //     var bufY = buf + (y << bufShift);
                        //     for (x = x0; x <= x1; x++) {
                        //         var pixel = bufY + x;
                        //         if ((z < pixel->depth) & ((pixel->stencil & ignoreStencil) == 0)) {
                        //             var color24 = info8[octant].Color;
                        //             pixel->color = new Color32 {
                        //                 // r = color24.R,
                        //                 // g = color24.G,
                        //                 // b = color24.B,
                        //                 r = (byte)((color24.R * blendFactorInv + state.color.R * blendFactor + 255) >> 8),
                        //                 g = (byte)((color24.G * blendFactorInv + state.color.G * blendFactor + 255) >> 8),
                        //                 b = (byte)((color24.B * blendFactorInv + state.color.B * blendFactor + 255) >> 8),
                        //                 a = 255
                        //             };
                        //             pixel->depth = z;
                        //             pixel->stencil = 1;
                        //         }
                        //     }
                        // }
                        
                        int px = x >> SubpixelShift;
                        int py = y >> SubpixelShift;
                        if (((px|py) & bufMaskInv) != 0) continue;
                        
                        int z = state.z + (deltas[octant].z >> state.depth);
                        
                        var pixel = buf + (px | (py << bufShift));
                        if ((z < pixel->depth) & ((pixel->stencil & ignoreStencil) == 0)) {
                            var color24 = info8[octant].Color;
                            pixel->color = new Color32 {
                                // r = color24.R,
                                // g = color24.G,
                                // b = color24.B,
                                r = (byte)((color24.R * blendFactorInv + state.color.R * blendFactor + 255) >> 8),
                                g = (byte)((color24.G * blendFactorInv + state.color.G * blendFactor + 255) >> 8),
                                b = (byte)((color24.B * blendFactorInv + state.color.B * blendFactor + 255) >> 8),
                                a = 255
                            };
                            pixel->depth = z;
                            pixel->stencil = 1;
                        }
                    }
                }
            }            
        }
        
        void SetupPoints(in Matrix4x4 matrix, out int potShift,
            out int Cx, out int Cy, out int extentX, out int extentY, out int minZ)
        {
            var X = new Vector3 {x = matrix.m00, y = matrix.m10, z = matrix.m20};
            var Y = new Vector3 {x = matrix.m01, y = matrix.m11, z = matrix.m21};
            var Z = new Vector3 {x = matrix.m02, y = matrix.m12, z = matrix.m22};
            var T = new Vector3 {x = matrix.m03, y = matrix.m13, z = matrix.m23};
            
            float maxSpan = 0.5f * CalculateMaxGap(X.x, X.y, Y.x, Y.y, Z.x, Z.y);
            
            // Power-of-two bounding square
            potShift = 0;
            for (int potSize = 1 << potShift; (potSize < maxSpan) & (potShift <= 30); potShift++, potSize <<= 1);
            
            int potShiftDelta = SubpixelShift - potShift;
            float potScale = (potShiftDelta >= 0 ? (1 << potShiftDelta) : 1f / (1 << -potShiftDelta));
            
            int Xx = (int)(X.x*potScale);
            int Xy = (int)(X.y*potScale);
            int Yx = (int)(Y.x*potScale);
            int Yy = (int)(Y.y*potScale);
            int Zx = (int)(Z.x*potScale);
            int Zy = (int)(Z.y*potScale);
            
            int maxGap = CalculateMaxGap(Xx, Xy, Yx, Yy, Zx, Zy);
            if (maxGap > SubpixelSize) {
                potShift++;
                potShiftDelta = SubpixelShift - potShift;
                Xx >>= 1; Xy >>= 1;
                Yx >>= 1; Yy >>= 1;
                Zx >>= 1; Zy >>= 1;
            }
            
            octantMap.Bake(Xx >> 1, Xy >> 1, Yx >> 1, Yy >> 1, Zx >> 1, Zy >> 1, SubpixelHalf, SubpixelHalf, SubpixelShift);
            // octantMap.Bake(Xx, Xy, Yx, Yy, Zx, Zy, SubpixelHalf, SubpixelHalf, SubpixelShift);
            
            Xx <<= potShift; Xy <<= potShift;
            Yx <<= potShift; Yy <<= potShift;
            Zx <<= potShift; Zy <<= potShift;
            Xx >>= 1; Xy >>= 1;
            Yx >>= 1; Yy >>= 1;
            Zx >>= 1; Zy >>= 1;
            
            extentX = (Xx < 0 ? -Xx : Xx) + (Yx < 0 ? -Yx : Yx) + (Zx < 0 ? -Zx : Zx);
            extentY = (Xy < 0 ? -Xy : Xy) + (Yy < 0 ? -Yy : Yy) + (Zy < 0 ? -Zy : Zy);
            
            float coordScale = 1 << SubpixelShift;
            int Tx = (int)(T.x*coordScale + 0.5f);
            int Ty = (int)(T.y*coordScale + 0.5f);
            Cx = Tx;
            Cy = Ty;
            
            int Xz = ((int)X.z) >> 1;
            int Yz = ((int)Y.z) >> 1;
            int Zz = ((int)Z.z) >> 1;
            int Tz = ((int)T.z);
            int extentZ = (Xz < 0 ? -Xz : Xz) + (Yz < 0 ? -Yz : Yz) + (Zz < 0 ? -Zz : Zz);
            minZ = Tz - extentZ;
            
            int octant = 0;
            for (int subZ = -1; subZ <= 1; subZ += 2) {
                for (int subY = -1; subY <= 1; subY += 2) {
                    for (int subX = -1; subX <= 1; subX += 2) {
                        deltas[octant].x = (Xx * subX + Yx * subY + Zx * subZ);
                        deltas[octant].y = (Xy * subX + Yy * subY + Zy * subZ);
                        deltas[octant].z = (Xz * subX + Yz * subY + Zz * subZ) + extentZ;
                        ++octant;
                    }
                }
            }
        }
        
        float CalculateMaxGap(float Xx, float Xy, float Yx, float Yy, float Zx, float Zy) {
            if (Xx < 0) Xx = -Xx;
            if (Xy < 0) Xy = -Xy;
            if (Yx < 0) Yx = -Yx;
            if (Yy < 0) Yy = -Yy;
            if (Zx < 0) Zx = -Zx;
            if (Zy < 0) Zy = -Zy;
            
            float maxGap = 0, gap = 0;
            gap = Xx + Yx; if (gap > maxGap) maxGap = gap;
            gap = Xy + Yy; if (gap > maxGap) maxGap = gap;
            gap = Yx + Zx; if (gap > maxGap) maxGap = gap;
            gap = Yy + Zy; if (gap > maxGap) maxGap = gap;
            gap = Zx + Xx; if (gap > maxGap) maxGap = gap;
            gap = Zy + Xy; if (gap > maxGap) maxGap = gap;
            
            return maxGap;
        }
        
        int CalculateMaxGap(int Xx, int Xy, int Yx, int Yy, int Zx, int Zy) {
            if (Xx < 0) Xx = -Xx;
            if (Xy < 0) Xy = -Xy;
            if (Yx < 0) Yx = -Yx;
            if (Yy < 0) Yy = -Yy;
            if (Zx < 0) Zx = -Zx;
            if (Zy < 0) Zy = -Zy;
            
            int maxGap = 0, gap = 0;
            gap = Xx + Yx; if (gap > maxGap) maxGap = gap;
            gap = Xy + Yy; if (gap > maxGap) maxGap = gap;
            gap = Yx + Zx; if (gap > maxGap) maxGap = gap;
            gap = Yy + Zy; if (gap > maxGap) maxGap = gap;
            gap = Zx + Xx; if (gap > maxGap) maxGap = gap;
            gap = Zy + Xy; if (gap > maxGap) maxGap = gap;
            
            return maxGap;
        }
    }
}
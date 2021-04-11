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
        
        public unsafe void UpdateTexture() {
            Texture.SetPixelData(colors, 0);
            Texture.Apply(false);
        }
        
        public unsafe void Blit(int depth_shift = -1) {
            int w = Width;
            int h = Height;
            int shift = TileShift;
            int tile_size = 1 << shift;

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
                        
                        byte r0 = colors_x->r;
                        byte g0 = colors_x->g;
                        byte b0 = colors_x->b;
                        
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
                            int dr = colors_x->r - r0;
                            if (dr < 0) dr = -dr;
                            int dg = colors_x->g - g0;
                            if (dg < 0) dg = -dg;
                            int db = colors_x->b - b0;
                            if (db < 0) db = -db;
                            int dmax = (dr > dg ? dr : dg);
                            dmax = (dmax > db ? dmax : db);
                            int fac = dmax >> 1;
                            int inv = 255 - fac;
                            
                            for (int subY = 0; subY < 2; subY++) {
                                var colors_sub_y = colors_ptr + (y2-y2start+subY) * w2;
                                for (int subX = 0; subX < 2; subX++) {
                                    if ((subX == x2start) & (subY == y2start)) continue;
                                    var colors_sub_x = colors_sub_y + (x2-x2start+subX);
                                    // colors_sub_x->r = (byte)((colors_sub_x->r*3 + colors_x->r + 3) >> 2);
                                    // colors_sub_x->g = (byte)((colors_sub_x->g*3 + colors_x->g + 3) >> 2);
                                    // colors_sub_x->b = (byte)((colors_sub_x->b*3 + colors_x->b + 3) >> 2);
                                    colors_sub_x->r = (byte)((colors_sub_x->r*inv + colors_x->r*fac + 255) >> 8);
                                    colors_sub_x->g = (byte)((colors_sub_x->g*inv + colors_x->g*fac + 255) >> 8);
                                    colors_sub_x->b = (byte)((colors_sub_x->b*inv + colors_x->b*fac + 255) >> 8);
                                }
                            }
                        }
                    }
                }
            }
        }

        public unsafe void Clear(Color32 background) {
            var clear_data = default(DataItem);
            clear_data.stencil = 0;
            clear_data.depth = int.MaxValue;
            clear_data.color = background;
            clear_data.id = 0;

            int w = Width;
            int h = Height;
            int shift = TileShift;
            int tile_size = 1 << shift;

            fixed (DataItem* data_ptr = Data)
            {
                for (int y = 0; y < h; y++) {
                    var data_ptr_y = data_ptr + y * tile_size;
                    for (int x = 0; x < w; x++) {
                        var data_x = data_ptr_y + x;
                        *data_x = clear_data;
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

        void CollectRenderers(Matrix4x4 viewMatrix) {
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            
            visibleObjects.Clear();
            
            foreach (var instance in ModelInstance.All) {
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, instance.Bounds)) {
                    var pos = instance.Bounds.center;
                    float sort_z = pos.x * viewMatrix.m20 + pos.y * viewMatrix.m21 + pos.z * viewMatrix.m22;
                    visibleObjects.Add((sort_z, instance));
                }
            }
            
            visibleObjects.Sort((itemA, itemB) => {
                return itemA.Item1.CompareTo(itemB.Item1);
            });
        }

        void RenderMain() {
            // Note: Unity's camera space matches OpenGL convention (forward is -Z)
            var viewMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cam.worldToCameraMatrix;
            
            CollectRenderers(viewMatrix);
            
            buffer.RenderStart(cam.backgroundColor);
            
            splatter.RenderObjects(buffer, cam, EnumerateInstances());
            
            // float ah = cam.orthographicSize;
            // float aw = (ah * w) / h;
            // var vpMatrix = Matrix4x4.Scale(new Vector3(w * 0.5f / aw, h * 0.5f / ah, -1)) * viewMatrix;
            // vpMatrix = Matrix4x4.Translate(new Vector3(w * 0.5f, h * 0.5f, 0f)) * vpMatrix;
            
            // splatter.RenderObjects(buffer, IterateInstances(vpMatrix));
            
            buffer.RenderEnd(DepthDisplayShift);
        }
        
        IEnumerable<ModelInstance> EnumerateInstances() {
            foreach (var (sort_z, instance) in visibleObjects) {
                yield return instance;
            }
        }
        
        // IEnumerable<(ModelInstance, Matrix4x4, int)> IterateInstances(Matrix4x4 vpMatrix) {
        //     float depth_scale = (1 << DepthResolution) / (cam.farClipPlane - cam.nearClipPlane);
        //     var depth_scale_matrix = Matrix4x4.Scale(new Vector3(1, 1, depth_scale));
            
        //     Vector2 subsampleOffset = default;
        //     if (buffer.Subsample) {
        //         Subsampler.Get(out int subsampleX, out int subsampleY);
        //         subsampleOffset.x = (subsampleX - 0.5f) * 0.5f;
        //         subsampleOffset.y = (subsampleY - 0.5f) * 0.5f;
        //     }
            
        //     foreach (var (sort_z, instance) in visibleObjects) {
        //         var voxel_scale_matrix = Matrix4x4.Scale(Vector3.one * instance.Model.Bounds.size.z); // for now
        //         var obj2world = instance.CachedTransform.localToWorldMatrix * voxel_scale_matrix;
        //         var mvp_matrix = vpMatrix * obj2world;
        //         mvp_matrix = depth_scale_matrix * mvp_matrix;
        //         mvp_matrix.m03 -= subsampleOffset.x;
        //         mvp_matrix.m13 -= subsampleOffset.y;
        //         yield return (instance, mvp_matrix, 0); // for now
        //     }
        // }
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
        
        const int DepthResolution = 24;
        
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
            public Dictionary<(ModelInstance, Model3D, int, int), int> sourceCacheOffsets;
            public Dictionary<(ModelInstance, Model3D, int, int), int> targetCacheOffsets;
            
            public NodeCache(int count) {
                int size = count * 8;
                indexCache0 = new int[size];
                indexCache1 = new int[size];
                infoCache0 = new NodeInfo[size];
                infoCache1 = new NodeInfo[size];
                sourceCacheOffsets = new Dictionary<(ModelInstance, Model3D, int, int), int>();
                targetCacheOffsets = new Dictionary<(ModelInstance, Model3D, int, int), int>();
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
        
        public int MapShift = 4;
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
        
        public bool UsePoints = true;
        
        int GeneralNodeCount;
        int AffineNodeCount;
        
        struct ProjectedVertex {
            public Vector3 Position;
            public Vector3 Projection;
        }
        // Cage vertices, 3x3x3-grid vertices... 16k should probably be more than enough
        ProjectedVertex[] projectedVertices = new ProjectedVertex[1 << 14];
        
        enum GridVertex {
            MinMinMin, MidMinMin, MaxMinMin,
            MinMidMin, MidMidMin, MaxMidMin,
            MinMaxMin, MidMaxMin, MaxMaxMin,
            
            MinMinMid, MidMinMid, MaxMinMid,
            MinMidMid, MidMidMid, MaxMidMid,
            MinMaxMid, MidMaxMid, MaxMaxMid,
            
            MinMinMax, MidMinMax, MaxMinMax,
            MinMidMax, MidMidMax, MaxMidMax,
            MinMaxMax, MidMaxMax, MaxMaxMax,
        }
        
        const int GridVertexCount = 3*3*3;
        const int GridCornersCount = 2*2*2;
        const int GridNonCornersCount = GridVertexCount - GridCornersCount;
        
        static readonly int[] GridCornerIndices = new int[] {
            (int)GridVertex.MinMinMin,
            (int)GridVertex.MaxMinMin,
            (int)GridVertex.MinMaxMin,
            (int)GridVertex.MaxMaxMin,
            (int)GridVertex.MinMinMax,
            (int)GridVertex.MaxMinMax,
            (int)GridVertex.MinMaxMax,
            (int)GridVertex.MaxMaxMax,
        };
        
        static readonly int[] GridSubdivisionIndices = new int[] {
            // X edges
            (int)GridVertex.MinMinMin, (int)GridVertex.MaxMinMin, (int)GridVertex.MidMinMin,
            (int)GridVertex.MinMaxMin, (int)GridVertex.MaxMaxMin, (int)GridVertex.MidMaxMin,
            (int)GridVertex.MinMinMax, (int)GridVertex.MaxMinMax, (int)GridVertex.MidMinMax,
            (int)GridVertex.MinMaxMax, (int)GridVertex.MaxMaxMax, (int)GridVertex.MidMaxMax,
            
            // Y edges
            (int)GridVertex.MinMinMin, (int)GridVertex.MinMaxMin, (int)GridVertex.MinMidMin,
            (int)GridVertex.MaxMinMin, (int)GridVertex.MaxMaxMin, (int)GridVertex.MaxMidMin,
            (int)GridVertex.MinMinMax, (int)GridVertex.MinMaxMax, (int)GridVertex.MinMidMax,
            (int)GridVertex.MaxMinMax, (int)GridVertex.MaxMaxMax, (int)GridVertex.MaxMidMax,
            
            // Z edges
            (int)GridVertex.MinMinMin, (int)GridVertex.MinMinMax, (int)GridVertex.MinMinMid,
            (int)GridVertex.MaxMinMin, (int)GridVertex.MaxMinMax, (int)GridVertex.MaxMinMid,
            (int)GridVertex.MinMaxMin, (int)GridVertex.MinMaxMax, (int)GridVertex.MinMaxMid,
            (int)GridVertex.MaxMaxMin, (int)GridVertex.MaxMaxMax, (int)GridVertex.MaxMaxMid,
            
            // Faces
            (int)GridVertex.MinMidMin, (int)GridVertex.MaxMidMin, (int)GridVertex.MidMidMin,
            (int)GridVertex.MinMinMid, (int)GridVertex.MaxMinMid, (int)GridVertex.MidMinMid,
            (int)GridVertex.MinMinMid, (int)GridVertex.MinMaxMid, (int)GridVertex.MinMidMid,
            (int)GridVertex.MaxMinMid, (int)GridVertex.MaxMaxMid, (int)GridVertex.MaxMidMid,
            (int)GridVertex.MinMaxMid, (int)GridVertex.MaxMaxMid, (int)GridVertex.MidMaxMid,
            (int)GridVertex.MinMidMax, (int)GridVertex.MaxMidMax, (int)GridVertex.MidMidMax,
            
            // Center
            (int)GridVertex.MinMidMid, (int)GridVertex.MaxMidMid, (int)GridVertex.MidMidMid,
        };
        
        static readonly int[] SubgridCornerIndices = new int[] {
            (int)GridVertex.MinMinMin, (int)GridVertex.MidMinMin, (int)GridVertex.MinMidMin, (int)GridVertex.MidMidMin,
            (int)GridVertex.MinMinMid, (int)GridVertex.MidMinMid, (int)GridVertex.MinMidMid, (int)GridVertex.MidMidMid,
            
            (int)GridVertex.MidMinMin, (int)GridVertex.MaxMinMin, (int)GridVertex.MidMidMin, (int)GridVertex.MaxMidMin,
            (int)GridVertex.MidMinMid, (int)GridVertex.MaxMinMid, (int)GridVertex.MidMidMid, (int)GridVertex.MaxMidMid,
            
            (int)GridVertex.MinMidMin, (int)GridVertex.MidMidMin, (int)GridVertex.MinMaxMin, (int)GridVertex.MidMaxMin,
            (int)GridVertex.MinMidMid, (int)GridVertex.MidMidMid, (int)GridVertex.MinMaxMid, (int)GridVertex.MidMaxMid,
            
            (int)GridVertex.MidMidMin, (int)GridVertex.MaxMidMin, (int)GridVertex.MidMaxMin, (int)GridVertex.MaxMaxMin,
            (int)GridVertex.MidMidMid, (int)GridVertex.MaxMidMid, (int)GridVertex.MidMaxMid, (int)GridVertex.MaxMaxMid,
            
            (int)GridVertex.MinMinMid, (int)GridVertex.MidMinMid, (int)GridVertex.MinMidMid, (int)GridVertex.MidMidMid,
            (int)GridVertex.MinMinMax, (int)GridVertex.MidMinMax, (int)GridVertex.MinMidMax, (int)GridVertex.MidMidMax,
            
            (int)GridVertex.MidMinMid, (int)GridVertex.MaxMinMid, (int)GridVertex.MidMidMid, (int)GridVertex.MaxMidMid,
            (int)GridVertex.MidMinMax, (int)GridVertex.MaxMinMax, (int)GridVertex.MidMidMax, (int)GridVertex.MaxMidMax,
            
            (int)GridVertex.MinMidMid, (int)GridVertex.MidMidMid, (int)GridVertex.MinMaxMid, (int)GridVertex.MidMaxMid,
            (int)GridVertex.MinMidMax, (int)GridVertex.MidMidMax, (int)GridVertex.MinMaxMax, (int)GridVertex.MidMaxMax,
            
            (int)GridVertex.MidMidMid, (int)GridVertex.MaxMidMid, (int)GridVertex.MidMaxMid, (int)GridVertex.MaxMaxMid,
            (int)GridVertex.MidMidMax, (int)GridVertex.MaxMidMax, (int)GridVertex.MidMaxMax, (int)GridVertex.MaxMaxMax,
            
        };
        
        List<(float, int)> sortedPartIndices = new List<(float, int)>(256);
        
        unsafe struct Context {
            public Buffer.DataItem* buffer;
            public uint* queues;
            public Delta* deltas;
            public StackEntry* stack;
            public byte* map;
            public byte* xmap;
            public byte* ymap;
            public int* readIndexCache;
            public int* writeIndexCache;
            public NodeInfo* readInfoCache;
            public NodeInfo* writeInfoCache;
            public int* gridCornerIndices;
            public int* gridSubdivisionIndices;
            public int* subgridCornerIndices;
            public ProjectedVertex* projectedVertices;
            public ProjectedVertex* projectedPartCorners;
            public ProjectedVertex* projectedGridsStack;
            public ModelInstance instance;
            public Model3D model;
            public NodeCache cache;
            public int width, height, bufferShift;
            public float xCenter, yCenter, pixelScale;
            public float xMin, yMin, xMax, yMax; // in clip space
            public float zNear, zFar, depthScale;
            public bool isOrthographic;
            public int currentFrame;
            public int writeIndex;
            public int partIndex, geometryIndex;
        }
        
        public void UpdateWidgets(List<Widget<string>> infoWidgets, List<Widget<float>> sliderWidgets, List<Widget<bool>> toggleWidgets) {
            infoWidgets.Add(new Widget<string>($"Cached={CacheCount}"));
            infoWidgets.Add(new Widget<string>($"Loaded={LoadedCount}"));
            infoWidgets.Add(new Widget<string>($"General={GeneralNodeCount}"));
            infoWidgets.Add(new Widget<string>($"Affine={AffineNodeCount}"));

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
        
        public unsafe void RenderObjects(Buffer buffer, Camera camera, IEnumerable<ModelInstance> instances) {
            float halfWidth = buffer.Width * 0.5f, halfHeight = buffer.Height * 0.5f;
            
            var context = new Context();
            
            context.width = buffer.Width;
            context.height = buffer.Height;
            context.bufferShift = buffer.TileShift;
            
            Vector2 subsampleOffset = default;
            if (buffer.Subsample) {
                Subsampler.Get(out int subsampleX, out int subsampleY);
                subsampleOffset.x = (0.5f - subsampleX) * 0.5f;
                subsampleOffset.y = (0.5f - subsampleY) * 0.5f;
            }
            
            context.xCenter = halfWidth + subsampleOffset.x;
            context.yCenter = halfHeight + subsampleOffset.y;
            
            var aperture = camera.orthographic ? camera.orthographicSize : Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f);
            context.pixelScale = halfHeight / aperture;
            
            const float BorderMargin = 0.0001f;
            context.xMin = -halfWidth + subsampleOffset.x + BorderMargin;
            context.yMin = -halfHeight + subsampleOffset.y + BorderMargin;
            context.xMax = halfWidth + subsampleOffset.x - BorderMargin;
            context.yMax = halfHeight + subsampleOffset.y - BorderMargin;
            
            context.zNear = camera.nearClipPlane * context.pixelScale;
            context.zFar = camera.farClipPlane * context.pixelScale;
            context.depthScale = (1 << DepthResolution) / (context.zFar - context.zNear);
            
            context.isOrthographic = camera.orthographic;
            
            context.currentFrame = Time.frameCount;
            
            // Note: Unity's camera space matches OpenGL convention (forward is -Z)
            var viewMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1) * context.pixelScale) * camera.worldToCameraMatrix;
            
            ////////////////////////////////////////////////////////////
            
            if (buffer.Subsample) {
                currentCacheIndex = context.currentFrame & 0b11;
            }
            context.cache = caches[currentCacheIndex];

            CacheCount = 0;
            LoadedCount = 0;
            GeneralNodeCount = 0;
            AffineNodeCount = 0;
            context.cache.targetCacheOffsets.Clear();

            octantMap.Resize(MapShift);

            // The zeroth element is used for "writing cached parent reference" of root nodes
            // (the value is not used, this just avoids extra checks)
            context.writeIndex = 2;

            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (uint* queues = OctantOrder.Queues)
            fixed (Delta* deltas = this.deltas)
            fixed (StackEntry* stack = this.stack)
            fixed (byte* map = octantMap.Data)
            fixed (byte* xmap = octantMap.DataX)
            fixed (byte* ymap = octantMap.DataY)
            fixed (int* readIndexCache = context.cache.indexCache0)
            fixed (int* writeIndexCache = context.cache.indexCache1)
            fixed (NodeInfo* readInfoCache = context.cache.infoCache0)
            fixed (NodeInfo* writeInfoCache = context.cache.infoCache1)
            fixed (int* gridCornerIndices = GridCornerIndices)
            fixed (int* gridSubdivisionIndices = GridSubdivisionIndices)
            fixed (int* subgridCornerIndices = SubgridCornerIndices)
            fixed (ProjectedVertex* projectedVertices = this.projectedVertices)
            {
                context.buffer = buf;
                context.queues = queues;
                context.deltas = deltas;
                context.stack = stack;
                context.map = map;
                context.xmap = xmap;
                context.ymap = ymap;
                context.readIndexCache = readIndexCache;
                context.writeIndexCache = writeIndexCache;
                context.readInfoCache = readInfoCache;
                context.writeInfoCache = writeInfoCache;
                context.gridCornerIndices = gridCornerIndices;
                context.gridSubdivisionIndices = gridSubdivisionIndices;
                context.subgridCornerIndices = subgridCornerIndices;
                context.projectedVertices = projectedVertices;
                context.projectedPartCorners = projectedVertices;
                context.projectedGridsStack = projectedVertices;
                
                foreach (var instance in instances) {
                    context.instance = instance;
                    context.model = instance.Model;
                    
                    var modelViewMatrix = viewMatrix * instance.CachedTransform.localToWorldMatrix;
                    
                    // TODO: occlusion test with the whole model's bounds?
                    // (probably makes sense only if the model has more than a few parts)
                    
                    ProjectCageVertices(ref context, ref modelViewMatrix);
                    SetupParts(ref context);
                    RenderParts(ref context);
                }
            }
            
            CacheCount = context.writeIndex;
            
            context.cache.Swap();
        }
        
        unsafe void ProjectCageVertices(ref Context context, ref Matrix4x4 modelViewMatrix) {
            if (context.instance.LastCageUpdateFrame != context.currentFrame) {
                context.instance.UpdateCage();
            }
            
            var cageVertices = context.instance.CageVertices;
            
            ProjectedVertex projectedVertex = default;
            
            for (int i = 0; i < cageVertices.Length; i++) {
                projectedVertex.Position = modelViewMatrix.MultiplyPoint3x4(cageVertices[i]);
                
                if (context.isOrthographic) {
                    projectedVertex.Projection = projectedVertex.Position;
                } else {
                    projectedVertex.Projection.z = context.pixelScale / projectedVertex.Position.z;
                    projectedVertex.Projection.x = projectedVertex.Position.x * projectedVertex.Projection.z;
                    projectedVertex.Projection.y = projectedVertex.Position.y * projectedVertex.Projection.z;
                }
                
                context.projectedVertices[i] = projectedVertex;
            }
            
            context.projectedPartCorners = context.projectedVertices + cageVertices.Length;
        }
        
        unsafe void SetupParts(ref Context context) {
            var parts = context.model.Parts;
            
            int cornersCount = 0;
            
            sortedPartIndices.Clear();
            
            for (int partIndex = 0; partIndex < parts.Length; partIndex++) {
                var part = parts[partIndex];
                
                float minZ = float.PositiveInfinity;
                for (int corner = 0; corner < GridCornersCount; corner++) {
                    var vertexPointer = context.projectedVertices + part.Vertices[corner];
                    context.projectedPartCorners[cornersCount] = *vertexPointer;
                    if (vertexPointer->Position.z < minZ) minZ = vertexPointer->Position.z;
                    cornersCount++;
                }
                
                sortedPartIndices.Add((minZ, partIndex));
            }
            
            sortedPartIndices.Sort();
            
            context.projectedGridsStack = context.projectedPartCorners + cornersCount;
        }
        
        unsafe void RenderParts(ref Context context) {
            var parts = context.model.Parts;
            var frames = context.instance.Frames;
            
            Matrix4x4 matrix = default;
            IndexCache8 emptyIndices = new IndexCache8 {n0=-1, n1=-1, n2=-1, n3=-1, n4=-1, n5=-1, n6=-1, n7=-1};
            
            foreach (var (minZ, partIndex) in sortedPartIndices) {
                int frame = ((frames != null) && (partIndex < frames.Length)) ? frames[partIndex] : 0;
                context.partIndex = partIndex;
                context.geometryIndex = parts[partIndex].Geometries[frame];
                
                var projectedPartCorners = context.projectedPartCorners + partIndex*GridCornersCount;
                var projectedGrid = context.projectedGridsStack;
                
                for (int corner = 0; corner < GridCornersCount; corner++) {
                    projectedGrid[context.gridCornerIndices[corner]] = projectedPartCorners[corner];
                }
                
                var geometry = context.model.Geometries[context.geometryIndex];
                var octree = geometry as RawOctree;
                // int maxLevel = octree.MaxLevel;
                int maxLevel = Mathf.Min(octree.MaxLevel, MaxLevel);
                int node = octree.RootNode;
                var color = octree.RootColor;
                int mask = node & 0xFF;
                fixed (int* nodes = octree.Nodes)
                fixed (Color32* colors = octree.Colors)
                {
                    var cacheOffsetKey = (context.instance, context.model, context.partIndex, context.geometryIndex);
                    
                    int writeIndexStart = context.writeIndex;
                    
                    if (!context.cache.sourceCacheOffsets.TryGetValue(cacheOffsetKey, out var readIndex)) {
                        readIndex = -1;
                    }
                    
                    readIndex = (readIndex << 8) | mask;
                    
                    RenderGeometry(ref context, projectedGrid, ref matrix,
                        readIndex, LoadNode, maxLevel, node, color, nodes, colors, ref emptyIndices);
                    
                    if (context.writeIndex > writeIndexStart) {
                        context.cache.targetCacheOffsets[cacheOffsetKey] = writeIndexStart;
                    }
                }
            }
        }
        
        unsafe void RenderGeometry(ref Context context, ProjectedVertex* projectedGrid,
            ref Matrix4x4 matrix, int readIndex, LoadFuncDelegate loadFunc,
            int maxLevel, int loadAddress, Color32 parentColor, int* nodes, Color32* colors,
            ref IndexCache8 emptyIndices, int minY = 0, int parentOffset = 0)
        {
            GeneralNodeCount++;
            
            // Calculate screen bounds
            CalculateBounds(ref context, projectedGrid, out var boundsMin, out var boundsMax);
            
            // Scissor test
            if (!((boundsMin.x < context.xMax) & (boundsMax.x > context.xMin))) return;
            if (!((boundsMin.y < context.yMax) & (boundsMax.y > context.yMin))) return;
            if (!((boundsMin.z < context.zFar) & (boundsMax.z > context.zNear))) return;
            
            int ixMin = (int)((boundsMin.x > context.xMin ? boundsMin.x : context.xMin) + context.xCenter + 0.5f);
            int iyMin = (int)((boundsMin.y > context.yMin ? boundsMin.y : context.yMin) + context.yCenter + 0.5f);
            int ixMax = (int)((boundsMax.x < context.xMax ? boundsMax.x : context.xMax) + context.xCenter - 0.5f);
            int iyMax = (int)((boundsMax.y < context.yMax ? boundsMax.y : context.yMax) + context.yCenter - 0.5f);
            if (iyMin < minY) iyMin = minY;
            if ((ixMax < ixMin) | (iyMax < iyMin)) return;
            
            if (boundsMin.z > context.zNear) {
                int iz = (int)((boundsMin.z - context.zNear) * context.depthScale);
                
                if ((maxLevel == 0) | ((ixMin == ixMax) & (iyMin == iyMax))) {
                    // Draw, if reached the leaf level or node occupies 1 pixel on screen
                    for (int y = iyMin; y <= iyMax; y++) {
                        var bufY = context.buffer + (y << context.bufferShift);
                        for (int x = ixMin; x <= ixMax; x++) {
                            bufY[x].id += 1;
                            if (iz < (bufY[x].depth & int.MaxValue)) {
                                bufY[x].color = parentColor;
                                bufY[x].depth = iz;
                            }
                        }
                    }
                    return;
                } else {
                    // Occlusion test
                    for (int y = iyMin; y <= iyMax; y++) {
                        var bufY = context.buffer + (y << context.bufferShift);
                        for (int x = ixMin; x <= ixMax; x++) {
                            bufY[x].id += 1;
                            if (iz < (bufY[x].depth & int.MaxValue)) {
                                minY = y;
                                goto traverse;
                            }
                        }
                    }
                    return;
                    traverse:;
                }
                
                // If distortion is less than pixel, switch to affine processing
                // (Though only if fully between near & far planes, and size isn't very large)
                if (boundsMax.z < context.zFar) {
                    const int MaxAffineSize = 1 << 15;
                    var sizeX = boundsMax.x - boundsMin.x;
                    var sizeY = boundsMax.y - boundsMin.y;
                    var size = (sizeX > sizeY ? sizeX : sizeY);
                    if ((size < MaxAffineSize) && IsApproximatelyAffine(projectedGrid, ref matrix)) {
                        RenderAffine(ref context, ref matrix, maxLevel, loadAddress, parentColor, nodes, colors,
                            readIndex, LoadNode, ref emptyIndices);
                        return;
                    }
                }
            } else {
                // If this node intersects the near plane, we can only subdivide further or skip
                
                // Skip, if reached the leaf level or node occupies 1 pixel on screen
                if ((maxLevel == 0) | ((ixMin == ixMax) & (iyMin == iyMax))) return;
            }
            
            ///////////////////////////////////////////
            
            // Calculate base parent offset for this node's children
            int subParentOffset = context.writeIndex << 3;
            var info8 = context.writeInfoCache + subParentOffset;
            var index8 = context.writeIndexCache + subParentOffset;
            var index8read = index8;
            
            // Write reference to this cached node in the parent
            context.writeIndexCache[parentOffset] = context.writeIndex;
            
            // Clear the cached node references
            *((IndexCache8*)index8) = emptyIndices;
            
            if (readIndex < 0) {
                loadFunc(loadAddress, info8, nodes, colors);
                LoadedCount++;
            } else {
                int _readIndex = readIndex >> 8;
                index8read = context.readIndexCache + (_readIndex << 3);
                *((InfoCache8*)info8) = ((InfoCache8*)context.readInfoCache)[_readIndex];
            }
            
            context.writeIndex += 1;
            
            ///////////////////////////////////////////
            
            int nodeMask = readIndex & 0xFF;
            
            Subdivide(ref context, projectedGrid);
            
            // Determine approximate order and queue
            var T = projectedGrid[(int)GridVertex.MidMidMid].Position;
            var X = projectedGrid[(int)GridVertex.MaxMidMid].Position - T;
            var Y = projectedGrid[(int)GridVertex.MidMaxMid].Position - T;
            var Z = projectedGrid[(int)GridVertex.MidMidMax].Position - T;
            matrix.m00 = X.x; matrix.m10 = X.y; matrix.m20 = X.z;
            matrix.m01 = Y.x; matrix.m11 = Y.y; matrix.m21 = Y.z;
            matrix.m02 = Z.x; matrix.m12 = Z.y; matrix.m22 = Z.z;
            int orderKey = OctantOrder.Key(in matrix);
            var queue = context.queues[nodeMask];
            
            // Process subnodes
            for (; queue != 0; queue >>= 4) {
                int octant = unchecked((int)(queue & 7));
                
                int octantParentOffset = subParentOffset | octant;
                int octantReadIndex = (index8read[octant] << 8) | info8[octant].Mask;
                int octantLoadAddress = info8[octant].Address;
                var octantColor24 = info8[octant].Color;
                var octantColor = new Color32 {r = octantColor24.R, g = octantColor24.G, b = octantColor24.B, a = 255};
                
                var octantGrid = projectedGrid + GridVertexCount;
                var octantCorners = context.subgridCornerIndices + (octant << 3);
                for (int corner = 0; corner < GridCornersCount; corner++) {
                    octantGrid[context.gridCornerIndices[corner]] = projectedGrid[octantCorners[corner]];
                }
                
                RenderGeometry(ref context, octantGrid,
                    ref matrix, octantReadIndex, loadFunc,
                    maxLevel-1, octantLoadAddress, octantColor, nodes, colors,
                    ref emptyIndices, minY, octantParentOffset);
            }
        }
        
        unsafe void CalculateBounds(ref Context context, ProjectedVertex* projectedGrid, out Vector3 min, out Vector3 max) {
            var vertex = projectedGrid + context.gridCornerIndices[0];
            min = max = new Vector3 {x = vertex->Projection.x, y = vertex->Projection.y, z = vertex->Position.z};
            
            for (int corner = 1; corner < GridCornersCount; corner++) {
                vertex = projectedGrid + context.gridCornerIndices[corner];
                if (vertex->Projection.x < min.x) min.x = vertex->Projection.x;
                if (vertex->Projection.x > max.x) max.x = vertex->Projection.x;
                if (vertex->Projection.y < min.y) min.y = vertex->Projection.y;
                if (vertex->Projection.y > max.y) max.y = vertex->Projection.y;
                if (vertex->Position.z < min.z) min.z = vertex->Position.z;
                if (vertex->Position.z > max.z) max.z = vertex->Position.z;
            }
        }
        
        unsafe bool IsApproximatelyAffine(ProjectedVertex* projectedGrid, ref Matrix4x4 matrix) {
            var TMinX = projectedGrid[(int)GridVertex.MinMinMin].Projection.x;
            var XMinX = projectedGrid[(int)GridVertex.MaxMinMin].Projection.x - TMinX;
            var YMinX = projectedGrid[(int)GridVertex.MinMaxMin].Projection.x - TMinX;
            var ZMinX = projectedGrid[(int)GridVertex.MinMinMax].Projection.x - TMinX;
            var TMinY = projectedGrid[(int)GridVertex.MinMinMin].Projection.y;
            var XMinY = projectedGrid[(int)GridVertex.MaxMinMin].Projection.y - TMinY;
            var YMinY = projectedGrid[(int)GridVertex.MinMaxMin].Projection.y - TMinY;
            var ZMinY = projectedGrid[(int)GridVertex.MinMinMax].Projection.y - TMinY;
            
            var TMaxX = projectedGrid[(int)GridVertex.MaxMaxMax].Projection.x;
            var XMaxX = projectedGrid[(int)GridVertex.MinMaxMax].Projection.x - TMaxX;
            var YMaxX = projectedGrid[(int)GridVertex.MaxMinMax].Projection.x - TMaxX;
            var ZMaxX = projectedGrid[(int)GridVertex.MaxMaxMin].Projection.x - TMaxX;
            var TMaxY = projectedGrid[(int)GridVertex.MaxMaxMax].Projection.y;
            var XMaxY = projectedGrid[(int)GridVertex.MinMaxMax].Projection.y - TMaxY;
            var YMaxY = projectedGrid[(int)GridVertex.MaxMinMax].Projection.y - TMaxY;
            var ZMaxY = projectedGrid[(int)GridVertex.MaxMaxMin].Projection.y - TMaxY;
            
            // Theoretically, checking the distortion of any 2 axes should be enough?
            float distortion, maxDistortion = 0f;
            distortion = XMinX + XMaxX; if (distortion < 0f) distortion = -distortion;
            if (distortion > maxDistortion) maxDistortion = distortion;
            distortion = XMinY + XMaxY; if (distortion < 0f) distortion = -distortion;
            if (distortion > maxDistortion) maxDistortion = distortion;
            distortion = YMinX + YMaxX; if (distortion < 0f) distortion = -distortion;
            if (distortion > maxDistortion) maxDistortion = distortion;
            distortion = YMinY + YMaxY; if (distortion < 0f) distortion = -distortion;
            if (distortion > maxDistortion) maxDistortion = distortion;
            distortion = ZMinX + ZMaxX; if (distortion < 0f) distortion = -distortion;
            if (distortion > maxDistortion) maxDistortion = distortion;
            distortion = ZMinY + ZMaxY; if (distortion < 0f) distortion = -distortion;
            if (distortion > maxDistortion) maxDistortion = distortion;
            
            if (maxDistortion > 1f) return false;
            
            var TMinZ = projectedGrid[(int)GridVertex.MinMinMin].Position.z;
            var XMinZ = projectedGrid[(int)GridVertex.MaxMinMin].Position.z - TMinZ;
            var YMinZ = projectedGrid[(int)GridVertex.MinMaxMin].Position.z - TMinZ;
            var ZMinZ = projectedGrid[(int)GridVertex.MinMinMax].Position.z - TMinZ;
            var TMaxZ = projectedGrid[(int)GridVertex.MaxMaxMax].Position.z;
            var XMaxZ = projectedGrid[(int)GridVertex.MinMaxMax].Position.z - TMaxZ;
            var YMaxZ = projectedGrid[(int)GridVertex.MaxMinMax].Position.z - TMaxZ;
            var ZMaxZ = projectedGrid[(int)GridVertex.MaxMaxMin].Position.z - TMaxZ;
            
            matrix.m00 = (XMinX - XMaxX) * 0.5f;
            matrix.m10 = (XMinY - XMaxY) * 0.5f;
            matrix.m20 = (XMinZ - XMaxZ) * 0.5f;
            
            matrix.m01 = (YMinX - YMaxX) * 0.5f;
            matrix.m11 = (YMinY - YMaxY) * 0.5f;
            matrix.m21 = (YMinZ - YMaxZ) * 0.5f;
            
            matrix.m02 = (ZMinX - ZMaxX) * 0.5f;
            matrix.m12 = (ZMinY - ZMaxY) * 0.5f;
            matrix.m22 = (ZMinZ - ZMaxZ) * 0.5f;
            
            matrix.m03 = (TMinX + TMaxX) * 0.5f;
            matrix.m13 = (TMinY + TMaxY) * 0.5f;
            matrix.m23 = (TMinZ + TMaxZ) * 0.5f;
            
            return true;
        }
        
        unsafe void Subdivide(ref Context context, ProjectedVertex* projectedGrid) {
            const int SubdivisionIndicesCount = GridNonCornersCount * 3;
            
            for (int i = 0; i < SubdivisionIndicesCount; i += 3) {
                var vertex0 = projectedGrid + context.gridSubdivisionIndices[i+0];
                var vertex1 = projectedGrid + context.gridSubdivisionIndices[i+1];
                var midpoint = projectedGrid + context.gridSubdivisionIndices[i+2];
                
                midpoint->Position.x = (vertex0->Position.x + vertex1->Position.x) * 0.5f;
                midpoint->Position.y = (vertex0->Position.y + vertex1->Position.y) * 0.5f;
                midpoint->Position.z = (vertex0->Position.z + vertex1->Position.z) * 0.5f;
                
                if (context.isOrthographic) {
                    midpoint->Projection = midpoint->Position;
                } else {
                    midpoint->Projection.z = context.pixelScale / midpoint->Position.z;
                    midpoint->Projection.x = midpoint->Position.x * midpoint->Projection.z;
                    midpoint->Projection.y = midpoint->Position.y * midpoint->Projection.z;
                }
            }
        }
        
        unsafe void RenderAffine(ref Context context, ref Matrix4x4 matrix, int maxLevel,
            int loadAddress, Color32 parentColor, int* nodes, Color32* colors,
            int readIndex, LoadFuncDelegate loadFunc, ref IndexCache8 emptyIndices)
        {
            SetupAffine(ref context, in matrix, out int potShift,
                out int centerX, out int centerY, out int extentX, out int extentY, out int startZ);
            
            if (maxLevel > potShift+1) maxLevel = potShift+1;
            
            int marginX = extentX >> (potShift+1);
            int marginY = extentY >> (potShift+1);
            
            int forwardKey = OctantOrder.Key(in matrix);
            int reverseKey = forwardKey ^ 0b11100000000;
            uint* forwardQueues = context.queues + forwardKey;
            uint* reverseQueues = context.queues + reverseKey;
            
            int bufMask = (1 << context.bufferShift) - 1;
            int bufMaskInv = ~bufMask;
            
            int ignoreStencil = (UseStencil ? -1 : 0);
            int blendFactor = BlendFactor;
            int blendFactorInv = 255 - blendFactor;
            bool updateCache = UpdateCache;
            
            int splatAt = SplatAt;
            
            var curr = context.stack + 1;
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
            if (curr->state.x1 >= context.width) curr->state.x1 = context.width-1;
            if (curr->state.y1 >= context.height) curr->state.y1 = context.height-1;
            
            curr->state.parentOffset = 0;
            curr->state.readIndex = readIndex;
            curr->state.loadAddress = loadAddress;
            curr->state.color = new Color24 {R=parentColor.r, G=parentColor.g, B=parentColor.b};
            
            {
                var state = curr->state;
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = context.buffer + (y << context.bufferShift);
                    for (int x = state.x0; x <= state.x1; x++) {
                        bufY[x].id += 1;
                        bufY[x].stencil = 0;
                    }
                }
            }
            
            while (curr > context.stack) {
                AffineNodeCount++;
                
                var state = curr->state;
                --curr;
                
                int nodeMask = state.readIndex & 0xFF;
                
                int lastY = state.y0;
                
                // Occlusion test
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = context.buffer + (y << context.bufferShift);
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
                int parentOffset = context.writeIndex << 3;
                var info8 = context.writeInfoCache + parentOffset;
                var index8 = context.writeIndexCache + parentOffset;
                var index8read = index8;
                
                // Write reference to this cached node in the parent
                context.writeIndexCache[state.parentOffset] = context.writeIndex;
                
                // Clear the cached node references
                *((IndexCache8*)index8) = emptyIndices;
                
                if (state.readIndex < 0) {
                    loadFunc(state.loadAddress, info8, nodes, colors);
                    LoadedCount++;
                } else {
                    int _readIndex = state.readIndex >> 8;
                    index8read = context.readIndexCache + (_readIndex << 3);
                    *((InfoCache8*)info8) = ((InfoCache8*)context.readInfoCache)[_readIndex];
                }
                
                context.writeIndex += 1;
                
                ///////////////////////////////////////////
                
                bool sizeCondition = (state.x1-state.x0 < splatAt) & (state.y1-state.y0 < splatAt);
                bool shouldDraw = (state.depth >= maxLevel) | sizeCondition;
                shouldDraw |= !updateCache & (state.readIndex < 0);
                
                if (!shouldDraw) {
                    var queue = reverseQueues[nodeMask];
                    
                    int subExtentX = (extentX >> state.depth) - SubpixelHalf;
                    int subExtentY = (extentY >> state.depth) - SubpixelHalf;
                    
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
                } else {
                    int mapHalf = 1 << (SubpixelShift + potShift - state.depth);
                    int toMapShift = (SubpixelShift + potShift - state.depth + 1) - octantMap.SizeShift;
                    int sx0 = (state.x0 << SubpixelShift) + SubpixelHalf - (state.x - mapHalf);
                    int sy0 = (state.y0 << SubpixelShift) + SubpixelHalf - (state.y - mapHalf);
                    
                    for (int y = state.y0, my = sy0; y <= state.y1; y++, my += SubpixelSize) {
                        var bufY = context.buffer + (y << context.bufferShift);
                        int maskY = context.ymap[my >> toMapShift] & nodeMask;
                        for (int x = state.x0, mx = sx0; x <= state.x1; x++, mx += SubpixelSize) {
                            int mask = context.xmap[mx >> toMapShift] & maskY;
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
        
        void SetupAffine(ref Context context, in Matrix4x4 matrix, out int potShift,
            out int Cx, out int Cy, out int extentX, out int extentY, out int minZ)
        {
            var X = new Vector3 {x = matrix.m00, y = matrix.m10, z = matrix.m20};
            var Y = new Vector3 {x = matrix.m01, y = matrix.m11, z = matrix.m21};
            var Z = new Vector3 {x = matrix.m02, y = matrix.m12, z = matrix.m22};
            var T = new Vector3 {x = matrix.m03, y = matrix.m13, z = matrix.m23};
            
            float maxSpan = 0.5f * CalculateMaxGap(X.x, X.y, Y.x, Y.y, Z.x, Z.y) * DrawBias;
            
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
            
            T.x += context.xCenter;
            T.y += context.yCenter;
            
            float coordScale = 1 << SubpixelShift;
            int Tx = (int)(T.x*coordScale + 0.5f);
            int Ty = (int)(T.y*coordScale + 0.5f);
            Cx = Tx;
            Cy = Ty;
            
            X.z *= context.depthScale;
            Y.z *= context.depthScale;
            Z.z *= context.depthScale;
            T.z = (T.z - context.zNear) * context.depthScale;
            
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
        
        public unsafe void RenderObjects(Buffer buffer, IEnumerable<(ModelInstance, Matrix4x4, int)> instances) {
            if (buffer.Subsample) {
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
                        var cacheOffsetKey = (instance, model, partIndex, geometryIndex);
                        
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
            
            bool useMap = UseMap;
            int splatAt = useMap ? SplatAt : 2;
            
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
                
                bool sizeCondition = (state.x1-state.x0 < splatAt) & (state.y1-state.y0 < splatAt);
                bool shouldDraw = (state.depth >= maxDepth) | sizeCondition;
                shouldDraw |= !updateCache & (state.readIndex < 0);
                
                if (!shouldDraw) {
                    var queue = reverseQueues[nodeMask];
                    
                    int subExtentX = (extentX >> state.depth);
                    int subExtentY = (extentY >> state.depth);
                    if (useMap) {
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
                } else if (useMap) {
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
                } else {
                    var queue = forwardQueues[nodeMask];
                    
                    for (; queue != 0; queue >>= 4) {
                        int octant = unchecked((int)(queue & 7));
                        
                        int x = state.x + (deltas[octant].x >> state.depth);
                        int y = state.y + (deltas[octant].y >> state.depth);
                        
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
            
            float maxSpan = 0.5f * CalculateMaxGap(X.x, X.y, Y.x, Y.y, Z.x, Z.y) * DrawBias;
            
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
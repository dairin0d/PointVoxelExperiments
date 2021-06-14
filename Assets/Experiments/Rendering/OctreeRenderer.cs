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
using UnityEngine;

namespace dairin0d.Rendering {
    class Buffer {
        public struct DataItem {
            public int address;
            public int depth;
            public Color32 color;
            public int id;
        }

        public DataItem[] Data;

        public Texture2D Texture;
        private Color32[] colors;

        public int Width;
        public int Height;
        public int BufferShiftX;
        public int BufferShiftY;
        
        public bool Subsample = false;
        private bool wasSubsample = false;

        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        public float FrameTime {get; private set;}

        public void RenderStart(Color32 background) {
            stopwatch.Restart();
            Clear(background);
        }
        
        public void RenderEnd(bool useAddress, int depth_shift = -1) {
            Blit(useAddress, depth_shift);
            stopwatch.Stop();
            FrameTime = stopwatch.ElapsedMilliseconds / 1000f;
            UpdateTexture();
        }

        public void Resize(int w, int h, int renderSize) {
            if (renderSize > 0) {
                int maxTexSize = SystemInfo.maxTextureSize;
                float scale = Mathf.Min(renderSize, maxTexSize) / (float)Mathf.Max(w, h);
                w = Mathf.Max(Mathf.RoundToInt(w * scale), 1);
                h = Mathf.Max(Mathf.RoundToInt(h * scale), 1);
            }

            if (Texture && (w == Width) && (h == Height) && (wasSubsample == Subsample)) {
                Resize(w, h);
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

            Resize(w, h);
        }
        
        private void Resize(int width, int height) {
            Width = width;
            Height = height;

            // BufferShiftX = NextPow2(width);
            // BufferShiftY = NextPow2(height);
            BufferShiftX = BufferShiftY = NextPow2(Mathf.Max(width, height));

            int dataSize = (1 << BufferShiftX) * (1 << BufferShiftY);
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
        
        public unsafe void Blit(bool useAddress, int depth_shift = -1) {
            int w = Width;
            int h = Height;
            int shift = BufferShiftX;

            bool show_normals = (depth_shift == 0);
            bool show_depth = (depth_shift > 0);
            bool show_complexity = (depth_shift < -1);
            int complexity_shift = -1 - depth_shift;

            int w2 = Texture.width, h2 = Texture.height;
            int x2step = 1, y2step = 1;
            int x2start = 0, y2start = 0;
            
            int xmax = w-1;
            int ymax = h-1;
            int ystep = 1 << shift;
            
            bool useSubsample = Subsample;
            if (Subsample) {
                x2step *= 2;
                y2step *= 2;
                Subsampler.Get(out x2start, out y2start);
            }

            fixed (OctreeNode* nodeDataPtr = ChunkedOctree.DataArray)
            fixed (DataItem* data_ptr = Data)
            fixed (Color32* colors_ptr = colors)
            {
                for (int y = 0, y2 = y2start; y < h; y++, y2 += y2step) {
                    var data_ptr_y = data_ptr + (y << shift);
                    var colors_ptr_y = colors_ptr + y2 * w2;
                    for (int x = 0, x2 = x2start; x < w; x++, x2 += x2step) {
                        var data_x = data_ptr_y + x;
                        var colors_x = colors_ptr_y + x2;
                        
                        byte r0 = colors_x->r;
                        byte g0 = colors_x->g;
                        byte b0 = colors_x->b;
                        
                        if (show_normals) {
                            int vC = data_x->depth & int.MaxValue;
                            int dL = (x == 0) ? int.MaxValue : vC - ((data_x-1)->depth & int.MaxValue);
                            int dR = (x == xmax) ? int.MaxValue : ((data_x+1)->depth & int.MaxValue) - vC;
                            int dD = (y == 0) ? int.MaxValue : vC - ((data_x-ystep)->depth & int.MaxValue);
                            int dU = (y == ymax) ? int.MaxValue : ((data_x+ystep)->depth & int.MaxValue) - vC;
                            int aL = (dL < 0 ? -dL : dL);
                            int aR = (dR < 0 ? -dR : dR);
                            int aD = (dD < 0 ? -dD : dD);
                            int aU = (dU < 0 ? -dU : dU);
                            int dX = (aL < aR ? dL : dR);
                            int dY = (aD < aU ? dD : dU);
                            float scale = 127f / (float) System.Math.Sqrt(dX*dX + dY*dY + 256*256);
                            colors_x->r = (byte)(128 + dX * scale);
                            colors_x->g = (byte)(128 + dY * scale);
                            colors_x->b = 128;
                            colors_x->a = 255;
                        } else if (show_depth) {
                            byte d = (byte)((data_x->depth & int.MaxValue) >> depth_shift);
                            colors_x->r = colors_x->g = colors_x->b = d;
                            colors_x->a = 255;
                        } else if (show_complexity) {
                            byte d = (byte)(data_x->id << complexity_shift);
                            colors_x->r = colors_x->g = colors_x->b = d;
                            colors_x->a = 255;
                        } else if (useAddress & (data_x->address >= 0)) {
                            // colors_x->r = (byte) (128 | (127 & (data_x->address >> 0)));
                            // colors_x->g = (byte) (128 | (127 & (data_x->address >> 8)));
                            // colors_x->b = (byte) (128 | (127 & (data_x->address >> 16)));
                            ref var color = ref nodeDataPtr[data_x->address].BaseColor;
                            colors_x->r = color.R;
                            colors_x->g = color.G;
                            colors_x->b = color.B;
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
            clear_data.address = -1;
            clear_data.depth = int.MaxValue;
            clear_data.color = background;
            clear_data.id = 0;

            int w = Width;
            int h = Height;
            int shift = BufferShiftX;

            fixed (DataItem* data_ptr = Data)
            {
                for (int y = 0; y < h; y++) {
                    var data_ptr_y = data_ptr + (y << shift);
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

    public class OctreeRenderer : MonoBehaviour {
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
                SliderWidgets.Add(new Widget<float>("Move Speed", () => Controller.speed, (value) => { Controller.speed = value; }, 0.1f, 5f));
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
            
            buffer.RenderEnd(splatter.UseAddress, DepthDisplayShift);
        }
        
        IEnumerable<ModelInstance> EnumerateInstances() {
            foreach (var (sort_z, instance) in visibleObjects) {
                yield return instance;
            }
        }
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
        }
        Delta[] deltas = new Delta[8 * 32];
        
        struct NodeState {
            public int x0, y0, x1, y1;
            public int level, x, y, z;
            public int address; // address of this node, not of the first child node
            public OctreeNode info;
        }
        NodeState[] stack = new NodeState[8 * 32];
        
        public int MapShift = 4;
        OctantMap octantMap = new OctantMap();
        
        public int MaxLevel = 0;
        
        public bool UpdateCache = true;
        
        public int SplatAt = 2;
        
        public float DistortionTolerance = 1;
        
        public bool DrawCubes = false;
        
        public bool DrawCircles = false;
        
        public int RadiusShift = 3;
        
        public bool UseAddress = false;
        
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
        
        // source vertex A, source vertex B, target vertex
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
            public int* octantToIndex;
            public int* indexToOctant;
            public OctantOrder.Queue* sparseQueues;
            public OctantOrder.Queue* packedQueues;
            public Delta* deltas;
            public NodeState* stack;
            public byte* xmap;
            public byte* ymap;
            public GCHandle nodeDataHandle;
            public OctreeNode* nodeData;
            public int* gridCornerIndices;
            public int* gridSubdivisionIndices;
            public int* subgridCornerIndices;
            public ProjectedVertex* projectedVertices;
            public ProjectedVertex* projectedPartCorners;
            public ProjectedVertex* projectedGridsStack;
            public ModelInstance instance;
            public Model3D model;
            public ChunkedOctree octree;
            public OctantOrder.Queue* queues;
            public ChunkedOctree.ChunkInfo* chunkInfos;
            public bool isPacked;
            public int chunkShift, chunkMask;
            public int width, height, bufferShift;
            public float xCenter, yCenter, pixelScale;
            public float xMin, yMin, xMax, yMax; // in clip space
            public float zNear, zFar, depthScale;
            public bool isOrthographic;
            public int currentFrame;
            public int partIndex, geometryIndex;
        }
        
        public void UpdateWidgets(List<Widget<string>> infoWidgets, List<Widget<float>> sliderWidgets, List<Widget<bool>> toggleWidgets) {
            // infoWidgets.Add(new Widget<string>($"Cached={CacheCount}"));
            // infoWidgets.Add(new Widget<string>($"Loaded={LoadedCount}"));
            infoWidgets.Add(new Widget<string>($"General={GeneralNodeCount}"));
            infoWidgets.Add(new Widget<string>($"Affine={AffineNodeCount}"));

            sliderWidgets.Add(new Widget<float>("Level", () => MaxLevel, (value) => { MaxLevel = (int)value; }, 0, 16));
            sliderWidgets.Add(new Widget<float>("MapShift", () => MapShift, (value) => { MapShift = (int)value; }, OctantMap.MinShift, OctantMap.MaxShift));
            sliderWidgets.Add(new Widget<float>("Splat At", () => SplatAt, (value) => { SplatAt = (int)value; }, 1, 8));
            sliderWidgets.Add(new Widget<float>("Distortion", () => DistortionTolerance, (value) => { DistortionTolerance = value; }, 0.25f, 8f));
            sliderWidgets.Add(new Widget<float>("RadiusShift", () => RadiusShift, (value) => { RadiusShift = (int)value; }, 0, 8));

            toggleWidgets.Add(new Widget<bool>("Draw Circles", () => DrawCircles, (value) => { DrawCircles = value; }));
            toggleWidgets.Add(new Widget<bool>("Draw Cubes", () => DrawCubes, (value) => { DrawCubes = value; }));
            toggleWidgets.Add(new Widget<bool>("Update Cache", () => UpdateCache, (value) => { UpdateCache = value; }));
            toggleWidgets.Add(new Widget<bool>("Use Address", () => UseAddress, (value) => { UseAddress = value; }));
        }
        
        public unsafe void RenderObjects(Buffer buffer, Camera camera, IEnumerable<ModelInstance> instances) {
            float halfWidth = buffer.Width * 0.5f, halfHeight = buffer.Height * 0.5f;
            
            var context = new Context();
            
            context.width = buffer.Width;
            context.height = buffer.Height;
            context.bufferShift = buffer.BufferShiftX;
            
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
            
            GeneralNodeCount = 0;
            AffineNodeCount = 0;
            
            octantMap.Resize(MapShift);
            
            fixed (Buffer.DataItem* buf = buffer.Data)
            fixed (int* octantToIndex = OctantOrder.OctantToIndex)
            fixed (int* indexToOctant = OctantOrder.IndexToOctant)
            fixed (OctantOrder.Queue* sparseQueues = OctantOrder.SparseQueues)
            fixed (OctantOrder.Queue* packedQueues = OctantOrder.PackedQueues)
            fixed (Delta* deltas = this.deltas)
            fixed (NodeState* stack = this.stack)
            fixed (byte* xmap = octantMap.DataX)
            fixed (byte* ymap = octantMap.DataY)
            fixed (int* gridCornerIndices = GridCornerIndices)
            fixed (int* gridSubdivisionIndices = GridSubdivisionIndices)
            fixed (int* subgridCornerIndices = SubgridCornerIndices)
            fixed (ProjectedVertex* projectedVertices = this.projectedVertices)
            {
                context.buffer = buf;
                context.octantToIndex = octantToIndex;
                context.indexToOctant = indexToOctant;
                context.sparseQueues = sparseQueues;
                context.packedQueues = packedQueues;
                context.deltas = deltas;
                context.stack = stack;
                context.xmap = xmap;
                context.ymap = ymap;
                context.gridCornerIndices = gridCornerIndices;
                context.gridSubdivisionIndices = gridSubdivisionIndices;
                context.subgridCornerIndices = subgridCornerIndices;
                context.projectedVertices = projectedVertices;
                context.projectedPartCorners = projectedVertices;
                context.projectedGridsStack = projectedVertices;
                
                context.nodeDataHandle = GCHandle.Alloc(ChunkedOctree.DataArray, GCHandleType.Pinned);
                context.nodeData = (OctreeNode*) context.nodeDataHandle.AddrOfPinnedObject();
                
                try {
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
                } finally {
                    context.nodeDataHandle.Free();
                    context.nodeDataHandle = default;
                }
            }
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
                var octree = geometry as ChunkedOctree;
                context.octree = octree;
                context.isPacked = octree.IsPacked;
                fixed (ChunkedOctree.ChunkInfo* chunkInfos = octree.ChunkInfos) {
                    context.chunkInfos = chunkInfos;
                    context.chunkShift = octree.ChunkShift;
                    context.chunkMask = (1 << context.chunkShift) - 1;
                    context.queues = octree.IsPacked ? context.packedQueues : context.sparseQueues;
                    var root = octree.Root;
                    RenderGeometry(ref context, projectedGrid, ref matrix, MaxLevel,
                        root.Address, root.Mask, -1, (Color32) root.BaseColor);
                }
            }
        }
        
        unsafe void RenderGeometry(ref Context context, ProjectedVertex* projectedGrid, ref Matrix4x4 matrix,
            int maxLevel, int loadAddress, int nodeMask, int parentAddress, Color32 parentColor, int minY = 0)
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
            
            if (loadAddress >= 0) {
                int chunkIndex = loadAddress >> context.chunkShift;
                var chunkInfo = context.chunkInfos + chunkIndex;
                if (!UpdateCache & (chunkInfo->ChunkStart < 0)) nodeMask = 0;
            }
            
            if (maxLevel <= 0) nodeMask = 0;
            
            if (boundsMin.z > context.zNear) {
                int iz = (int)((boundsMin.z - context.zNear) * context.depthScale);
                
                bool isSinglePixel = (ixMin == ixMax) & (iyMin == iyMax);
                
                if ((!DrawCubes & (nodeMask == 0)) | isSinglePixel) {
                    if (DrawCircles & !isSinglePixel) {
                        float cx = (boundsMin.x + boundsMax.x) * 0.5f;
                        float cy = (boundsMin.y + boundsMax.y) * 0.5f;
                        float dx = boundsMax.x - boundsMin.x;
                        float dy = boundsMax.y - boundsMin.y;
                        float r = (float)System.Math.Sqrt((dx*dx + dy*dy) * 0.25f);
                        float fxMin = cx - r, fyMin = cy - r, fxMax = cx + r, fyMax = cy + r;
                        
                        ixMin = (int)((fxMin > context.xMin ? fxMin : context.xMin) + context.xCenter + 0.5f);
                        iyMin = (int)((fyMin > context.yMin ? fyMin : context.yMin) + context.yCenter + 0.5f);
                        ixMax = (int)((fxMax < context.xMax ? fxMax : context.xMax) + context.xCenter - 0.5f);
                        iyMax = (int)((fyMax < context.yMax ? fyMax : context.yMax) + context.yCenter - 0.5f);
                        
                        int icx = (int)(cx + context.xCenter);
                        int icy = (int)(cy + context.yCenter);
                        int r2max = (int)(r + 0.5f);
                        r2max *= r2max;
                        
                        int idx = ixMin - icx, idy = iyMin - icy;
                        int r2y = idx*idx + idy*idy;
                        for (int y = iyMin; y <= iyMax; y++) {
                            var bufY = context.buffer + (y << context.bufferShift);
                            int _idx = idx, r2 = r2y;
                            for (int x = ixMin; x <= ixMax; x++) {
                                var pixel = bufY + x;
                                pixel->id += 1;
                                if ((iz < (pixel->depth & int.MaxValue)) & (r2 <= r2max)) {
                                    pixel->address = parentAddress;
                                    pixel->color = parentColor;
                                    pixel->depth = iz;
                                }
                                r2 += _idx + _idx + 1;
                                _idx++;
                            }
                            r2y += idy + idy + 1;
                            idy++;
                        }
                    } else {
                        // Draw, if reached the leaf level or node occupies 1 pixel on screen
                        for (int y = iyMin; y <= iyMax; y++) {
                            var bufY = context.buffer + (y << context.bufferShift);
                            for (int x = ixMin; x <= ixMax; x++) {
                                var pixel = bufY + x;
                                pixel->id += 1;
                                if (iz < (pixel->depth & int.MaxValue)) {
                                    pixel->address = parentAddress;
                                    pixel->color = parentColor;
                                    pixel->depth = iz;
                                }
                            }
                        }
                    }
                    return;
                } else {
                    // Occlusion test
                    for (int y = iyMin; y <= iyMax; y++) {
                        var bufY = context.buffer + (y << context.bufferShift);
                        for (int x = ixMin; x <= ixMax; x++) {
                            var pixel = bufY + x;
                            pixel->id += 1;
                            if (iz < (pixel->depth & int.MaxValue)) {
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
                        RenderAffine(ref context, ref matrix, maxLevel, loadAddress, nodeMask, parentAddress, parentColor);
                        return;
                    }
                }
            } else {
                // If this node intersects the near plane, we can only subdivide further or skip
                
                // Skip, if reached the leaf level or node occupies 1 pixel on screen
                if ((nodeMask == 0) | ((ixMin == ixMax) & (iyMin == iyMax))) return;
            }
            
            bool isCube = DrawCubes & ((nodeMask == 0) | (loadAddress < 0));
            if (isCube) nodeMask = 0xFF;
            
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
            var queue = context.queues[orderKey|nodeMask];
            
            ///////////////////////////////////////////
            
            // Calculate base parent offset for this node's children
            OctreeNode* subnodes = null;
            
            int absAddress = int.MinValue;
            
            if (loadAddress >= 0) {
                int chunkIndex = loadAddress >> context.chunkShift;
                var chunkInfo = context.chunkInfos + chunkIndex;
                chunkInfo->AccessTime = context.currentFrame;
                
                if (chunkInfo->ChunkStart < 0) {
                    context.octree.Unpack(chunkIndex, ref context.nodeDataHandle, ref context.nodeData);
                }
                
                subnodes = context.nodeData + chunkInfo->ChunkStart + (loadAddress & context.chunkMask);
                
                absAddress = (int)(subnodes - context.nodeData);
            }
            
            ///////////////////////////////////////////
            
            // Process subnodes
            for (; queue.Indices != 0; queue.Indices >>= 4, queue.Octants >>= 4) {
                var octantGrid = projectedGrid + GridVertexCount;
                var octantCorners = context.subgridCornerIndices + ((queue.Octants & 7) << 3);
                for (int corner = 0; corner < GridCornersCount; corner++) {
                    octantGrid[context.gridCornerIndices[corner]] = projectedGrid[octantCorners[corner]];
                }
                
                int octantMask;
                int octantLoadAddress;
                int octantAddress;
                Color32 octantColor;
                
                if (isCube) {
                    octantAddress = parentAddress;
                    octantMask = 0xFF;
                    octantLoadAddress = -1;
                    octantColor = parentColor;
                } else {
                    octantAddress = absAddress + unchecked((int)(queue.Indices & 7));
                    var subnode = subnodes + (queue.Indices & 7);
                    octantMask = subnode->Mask;
                    octantLoadAddress = subnode->Address;
                    var octantColor24 = subnode->BaseColor;
                    octantColor = new Color32 {r = octantColor24.R, g = octantColor24.G, b = octantColor24.B, a = 255};
                }
                
                RenderGeometry(ref context, octantGrid, ref matrix,
                    maxLevel-1, octantLoadAddress, octantMask, octantAddress, octantColor, minY);
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
            
            if (maxDistortion > DistortionTolerance) return false;
            
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
        
        unsafe void RenderAffine(ref Context context, ref Matrix4x4 matrix,
            int maxLevel, int loadAddress, int parentMask, int parentAddress, Color32 parentColor)
        {
            SetupAffine(ref context, in matrix, out int potShift,
                out int centerX, out int centerY, out int extentX, out int extentY, out int startZ, out float radius);
            
            if (maxLevel > potShift+1) maxLevel = potShift+1;
            
            int r = Mathf.CeilToInt(SubpixelSize * radius * Mathf.Sqrt(1.5f)) + SubpixelHalf;
            r >>= maxLevel;
            int rShift = Mathf.Min(RadiusShift, SubpixelShift);
            int drShift = SubpixelShift - rShift;
            int r2max = r >> drShift;
            r2max = r2max * r2max;
            int rStep = 1 << rShift;
            int r2Shift = rShift+1;
            int r2Add = rStep * rStep;
            
            int forwardKey = OctantOrder.Key(in matrix);
            int reverseKey = forwardKey ^ 0b11100000000;
            var forwardQueues = context.queues + forwardKey;
            var reverseQueues = context.queues + reverseKey;
            
            int bufMask = (1 << context.bufferShift) - 1;
            int bufMaskInv = ~bufMask;
            
            int maxExtent = (extentX > extentY ? extentX : extentY);
            float subpixelFactor = (maxExtent >> (maxLevel - 1)) / (float)SubpixelSize;
            
            int blendFactor = (int)(Mathf.Clamp01(0.5f - subpixelFactor) * 4 * 255);
            int blendFactorInv = 255 - blendFactor;
            
            bool updateCache = UpdateCache;
            
            int splatAt = SplatAt;
            
            var curr = context.stack + 1;
            curr->level = 1;
            curr->x = centerX;
            curr->y = centerY;
            curr->z = startZ;
            curr->x0 = (curr->x + SubpixelHalf - extentX) >> SubpixelShift;
            curr->y0 = (curr->y + SubpixelHalf - extentY) >> SubpixelShift;
            curr->x1 = (curr->x - SubpixelHalf + extentX) >> SubpixelShift;
            curr->y1 = (curr->y - SubpixelHalf + extentY) >> SubpixelShift;
            if (curr->x0 < 0) curr->x0 = 0;
            if (curr->y0 < 0) curr->y0 = 0;
            if (curr->x1 >= context.width) curr->x1 = context.width-1;
            if (curr->y1 >= context.height) curr->y1 = context.height-1;
            
            curr->address = parentAddress;
            
            curr->info = new OctreeNode {
                Address = loadAddress,
                Mask = (byte)parentMask,
                BaseColor = new Color24 {R=parentColor.r, G=parentColor.g, B=parentColor.b},
            };
            
            {
                var state = *curr;
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = context.buffer + (y << context.bufferShift);
                    for (int x = state.x0; x <= state.x1; x++) {
                        var pixel = bufY + x;
                        pixel->id += 1;
                        pixel->depth &= int.MaxValue;
                    }
                }
            }
            
            while (curr > context.stack) {
                AffineNodeCount++;
                
                var state = *curr;
                --curr;
                
                if (state.info.Address >= 0) {
                    int chunkIndex = state.info.Address >> context.chunkShift;
                    var chunkInfo = context.chunkInfos + chunkIndex;
                    if (!UpdateCache & (chunkInfo->ChunkStart < 0)) state.info.Mask = 0;
                }
                
                if (state.level >= maxLevel) state.info.Mask = 0;
                
                bool isCube = DrawCubes & ((state.info.Mask == 0) | (state.info.Address < 0));
                if (isCube) state.info.Mask = 0xFF;
                
                if (state.info.Mask == 0) {
                    for (int y = state.y0; y <= state.y1; y++) {
                        var bufY = context.buffer + (y << context.bufferShift);
                        for (int x = state.x0; x <= state.x1; x++) {
                            var pixel = bufY + x;
                            pixel->id += 1;
                            
                            if (state.z < pixel->depth) {
                                pixel->address = state.address;
                                var color24 = state.info.BaseColor;
                                pixel->color = new Color32 {
                                    r = color24.R,
                                    g = color24.G,
                                    b = color24.B,
                                    a = 255
                                };
                                pixel->depth = state.z | int.MinValue;
                            }
                        }
                    }
                    continue;
                }
                
                int lastY = state.y0;
                
                // Occlusion test
                for (int y = state.y0; y <= state.y1; y++) {
                    var bufY = context.buffer + (y << context.bufferShift);
                    for (int x = state.x0; x <= state.x1; x++) {
                        var pixel = bufY + x;
                        pixel->id += 1;
                        if (state.z < pixel->depth) {
                            lastY = y;
                            goto traverse;
                        }
                    }
                }
                continue;
                traverse:;
                
                // Calculate base parent offset for this node's children
                OctreeNode* subnodes = null;
                
                int absAddress = int.MinValue;
                
                if (state.info.Address >= 0) {
                    int chunkIndex = state.info.Address >> context.chunkShift;
                    var chunkInfo = context.chunkInfos + chunkIndex;
                    chunkInfo->AccessTime = context.currentFrame;
                    
                    if (chunkInfo->ChunkStart < 0) {
                        context.octree.Unpack(chunkIndex, ref context.nodeDataHandle, ref context.nodeData);
                    }
                    
                    subnodes = context.nodeData + chunkInfo->ChunkStart + (state.info.Address & context.chunkMask);
                    
                    absAddress = (int)(subnodes - context.nodeData);
                }
                
                ///////////////////////////////////////////
                
                bool sizeCondition = (state.x1-state.x0 < splatAt) & (state.y1-state.y0 < splatAt);
                bool shouldDraw = (!DrawCubes & (state.info.Mask == 0)) | sizeCondition;
                
                int nodeMask = state.info.Mask;
                
                if (!shouldDraw) {
                    var queue = reverseQueues[nodeMask];
                    
                    int subExtentX = (extentX >> state.level) - SubpixelHalf;
                    int subExtentY = (extentY >> state.level) - SubpixelHalf;
                    
                    for (; queue.Indices != 0; queue.Indices >>= 4, queue.Octants >>= 4) {
                        var delta = context.deltas + (queue.Octants & 7);
                        
                        int x = state.x + (delta->x >> state.level);
                        int y = state.y + (delta->y >> state.level);
                        
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
                        curr->level = state.level+1;
                        curr->x = x;
                        curr->y = y;
                        curr->z = state.z + (delta->z >> state.level);
                        curr->x0 = x0;
                        curr->y0 = y0;
                        curr->x1 = x1;
                        curr->y1 = y1;
                        
                        if (isCube) {
                            curr->address = state.address;
                            curr->info.Address = -1;
                            curr->info.Mask = 0xFF;
                            curr->info.BaseColor = state.info.BaseColor;
                        } else {
                            curr->address = absAddress + unchecked((int)(queue.Indices & 7));
                            curr->info = subnodes[queue.Indices & 7];
                        }
                    }
                } else if (DrawCircles & !sizeCondition) {
                    var queue = forwardQueues[nodeMask];
                    
                    int subExtentX = (extentX >> state.level) - SubpixelHalf;
                    int subExtentY = (extentY >> state.level) - SubpixelHalf;
                    
                    for (; queue.Indices != 0; queue.Indices >>= 4, queue.Octants >>= 4) {
                        var delta = context.deltas + (queue.Octants & 7);
                        
                        int cx = (state.x + (delta->x >> state.level));
                        int cy = (state.y + (delta->y >> state.level));
                        
                        int x0 = (cx - r) >> SubpixelShift; if (x0 < 0) x0 = 0;
                        int y0 = (cy - r) >> SubpixelShift; if (y0 < 0) y0 = 0;
                        int x1 = (cx + r) >> SubpixelShift; if (x1 >= context.width) x1 = context.width-1;
                        int y1 = (cy + r) >> SubpixelShift; if (y1 >= context.height) y1 = context.height-1;
                        
                        if ((x0 > x1) | (y0 > y1)) continue;
                        
                        int icx = cx >> SubpixelShift;
                        int icy = cy >> SubpixelShift;
                        
                        int z = state.z + (delta->z >> state.level);
                        
                        int idx = ((x0 - icx) << rShift) + ((SubpixelHalf - (cx & SubpixelMask)) >> drShift);
                        int idy = ((y0 - icy) << rShift) + ((SubpixelHalf - (cy & SubpixelMask)) >> drShift);
                        int r2y = idx*idx + idy*idy;
                        for (int y = y0; y <= y1; y++) {
                            var bufY = context.buffer + (y << context.bufferShift);
                            int _idx = idx, r2 = r2y;
                            for (int x = x0; x <= x1; x++) {
                                var pixel = bufY + x;
                                pixel->id += 1;
                                
                                if ((z < pixel->depth) & (r2 <= r2max)) {
                                    pixel->address = absAddress + unchecked((int)(queue.Indices & 7));
                                    if (!UseAddress) {
                                        var color24 = subnodes[queue.Indices & 7].BaseColor;
                                        pixel->color = new Color32 {
                                            r = color24.R,
                                            g = color24.G,
                                            b = color24.B,
                                            a = 255
                                        };
                                    }
                                    pixel->depth = z | int.MinValue;
                                }
                                
                                r2 += (_idx << r2Shift) + r2Add;
                                _idx += rStep;
                            }
                            r2y += (idy << r2Shift) + r2Add;
                            idy += rStep;
                        }
                    }
                } else {
                    int nodeMaskKey = nodeMask << 3;
                    
                    int mapHalf = 1 << (SubpixelShift + potShift - state.level);
                    int toMapShift = (SubpixelShift + potShift - state.level + 1) - octantMap.SizeShift;
                    int sx0 = (state.x0 << SubpixelShift) + SubpixelHalf - (state.x - mapHalf);
                    int sy0 = (state.y0 << SubpixelShift) + SubpixelHalf - (state.y - mapHalf);
                    
                    for (int y = state.y0, my = sy0; y <= state.y1; y++, my += SubpixelSize) {
                        var bufY = context.buffer + (y << context.bufferShift);
                        int maskY = context.ymap[my >> toMapShift] & nodeMask;
                        for (int x = state.x0, mx = sx0; x <= state.x1; x++, mx += SubpixelSize) {
                            var pixel = bufY + x;
                            int mask = context.xmap[mx >> toMapShift] & maskY;
                            
                            if ((mask != 0) & (state.z < pixel->depth)) {
                                var octant = unchecked((int)(forwardQueues[mask].Octants & 7));
                                
                                int z = state.z + (context.deltas[octant].z >> state.level);
                                pixel->id += 1;
                                
                                if (z < pixel->depth) {
                                    if (isCube) {
                                        pixel->address = state.address;
                                        pixel->color = new Color32 {
                                            r = state.info.BaseColor.R,
                                            g = state.info.BaseColor.G,
                                            b = state.info.BaseColor.B,
                                            a = 255
                                        };
                                    } else {
                                        int index = context.isPacked ? context.octantToIndex[nodeMaskKey | octant] : octant;
                                        pixel->address = absAddress + index;
                                        if (!UseAddress) {
                                            var color24 = subnodes[index].BaseColor;
                                            if (state.level >= maxLevel) {
                                                pixel->color = new Color32 {
                                                    r = (byte)((color24.R * blendFactorInv + state.info.BaseColor.R * blendFactor + 255) >> 8),
                                                    g = (byte)((color24.G * blendFactorInv + state.info.BaseColor.G * blendFactor + 255) >> 8),
                                                    b = (byte)((color24.B * blendFactorInv + state.info.BaseColor.B * blendFactor + 255) >> 8),
                                                    a = 255
                                                };
                                            } else {
                                                pixel->color = new Color32 {
                                                    r = color24.R,
                                                    g = color24.G,
                                                    b = color24.B,
                                                    a = 255
                                                };
                                            }
                                        }
                                    }
                                    pixel->depth = z | int.MinValue;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        void SetupAffine(ref Context context, in Matrix4x4 matrix, out int potShift,
            out int Cx, out int Cy, out int extentX, out int extentY, out int minZ, out float radius)
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
            
            float dx2 = X.x*X.x + X.y*X.y;
            float dy2 = Y.x*Y.x + Y.y*Y.y;
            float dz2 = Z.x*Z.x + Z.y*Z.y;
            radius = (float)System.Math.Sqrt((dx2 + dy2 + dz2) * 0.25f);
            
            octantMap.Bake(Xx >> 1, Xy >> 1, Yx >> 1, Yy >> 1, Zx >> 1, Zy >> 1, SubpixelHalf, SubpixelHalf, SubpixelShift, 1);
            
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
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

namespace dairin0d.Rendering.Octree {
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
        public int vSyncCount = 0;
        public int targetFrameRate = 30;

        Camera cam;
        int cullingMask;
        CameraClearFlags clearFlags;

        Matrix4x4 vp_matrix;
        Plane[] frustum_planes;

        public int RenderSize = 0;
        public int TilePow = 0;
        Buffer buffer;

        public int subpixel_shift = 8;
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
            buffer.Resize(cam.pixelWidth, cam.pixelHeight, RenderSize, Mathf.Clamp(TilePow, 0, 12));
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
        }

        void OnGUI() {
            int x = 0, y = 0, panelWidth = 160, lineHeight = 20, sliderHeight = 12;

            GUI.DrawTexture(cam.pixelRect, buffer.Texture, ScaleMode.StretchToFill, true);

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
            SliderWidgets.Add(new Widget<float>("Tile", () => TilePow, (value) => { TilePow = (int)value; }, 0, 12));
            SliderWidgets.Add(new Widget<float>("Subpixel", () => subpixel_shift, (value) => { subpixel_shift = (int)value; }, 0, 8));
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
            
            splatter.RenderObjects(buffer, subpixel_shift, IterateInstances(octree));
            
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
}
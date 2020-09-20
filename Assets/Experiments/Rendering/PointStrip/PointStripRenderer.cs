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

namespace dairin0d.Rendering.PointStrip {
    public class PointStripRenderer : MonoBehaviour {
        struct VCOInstance {
            public Transform tfm;
            public Matrix4x4 mvp_matrix;
        }

        public Transform PointCloudsParent;

        public int RenderSize = 128;

        public bool CustomRendering = false;

        public string modelPath = "";
        public float voxelSize = -1;
        public int modelAggregate = 0;

        public float voxel_scale = 1f / 128f;

        public int dviz = 0;

        Camera cam;

        System.Diagnostics.Stopwatch stopwatch = null;

        Texture2D tex;
        Color32[] cbuf;
        BufData[] buf;

        // Matrix4x4 contains translation vector
        // in the last column: m03=tX, m13=tY, m23=tZ

        int BufW_shift, BufH_shift;
        int BufW, BufH;

        float dt = 0;

        int _cull_mask;
        CameraClearFlags _clear_flags;

        AcceleratedPointCloud apc;

        List<VCOInstance> vco_visible = new List<VCOInstance>(64);

        void Start() {
            cam = GetComponent<Camera>();
            stopwatch = new System.Diagnostics.Stopwatch();
            ResizeDisplay();
        }

        void Update() {
            if (Input.GetKeyDown(KeyCode.Space)) CustomRendering = !CustomRendering;
            if (Input.GetKeyDown(KeyCode.PageUp)) dviz += 1;
            if (Input.GetKeyDown(KeyCode.PageDown)) dviz -= 1;
            dviz = Mathf.Clamp(dviz, -5, 0);

            ResizeDisplay();
        }

        void OnPreCull() {
            if (CustomRendering) {
                _cull_mask = cam.cullingMask;
                _clear_flags = cam.clearFlags;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.Nothing;
            }
        }
        void OnPostRender() {
            if (CustomRendering) {
                cam.cullingMask = _cull_mask;
                cam.clearFlags = _clear_flags;
                RenderPointClouds();
            }
        }

        void OnGUI() {
            if (CustomRendering) {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), tex);
                GUI.color = Color.black;
                string txt = string.Format("dt={0:0.000}", dt);
                txt += ", dviz=" + dviz + " (PageUp/PageDown)";
                GUI.Label(new Rect(0, Screen.height - 20, Screen.width, 20), txt);
                if (apc != null) {
                    txt = "Abs=" + apc.n_abs + ", Rel=" + apc.n_rel + ", Oct=" + apc.n_oct + ", dMax=" + apc.delta_max_rel;
                    GUI.Label(new Rect(0, Screen.height - 40, Screen.width, 20), txt);
                }
                GUI.color = Color.white;
            }
        }

        void ResizeDisplay() {
            float max_sz = Mathf.Max(Screen.width, Screen.height);
            var scale = new Vector2(Screen.width / max_sz, Screen.height / max_sz);
            int w = Screen.width, h = Screen.height;
            if (RenderSize > 0) {
                w = Mathf.Max(1, Mathf.RoundToInt(RenderSize * scale.x));
                h = Mathf.Max(1, Mathf.RoundToInt(RenderSize * scale.y));
            }
            if ((tex != null) && (w == tex.width) && (h == tex.height)) return;
            if (tex != null) Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            int size = w * h;
            cbuf = new Color32[size];

            BufW_shift = NextPow2(w);
            BufH_shift = NextPow2(h);
            BufH_shift = BufW_shift = Mathf.Max(BufW_shift, BufH_shift);
            BufW = 1 << BufW_shift;
            BufH = 1 << BufH_shift;

            buf = new BufData[BufW * BufH];

            BlitClearBuf();
        }

        static int NextPow2(int v) {
            return Mathf.CeilToInt(Mathf.Log(v) / Mathf.Log(2));
        }

        void WalkPointClouds(System.Action<Renderer> clb, Transform parent = null) {
            if (parent == null) parent = PointCloudsParent;
            foreach (Transform child in parent) {
                var pco = child.GetComponent<Renderer>();
                if (pco) clb(pco);
                WalkPointClouds(clb, child);
            }
        }

        static bool IsVisibleFrom(Renderer renderer, Camera camera) {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }

        void RenderPointClouds() {
            var bkg = (Color32)cam.backgroundColor;
            bkg.a = 255;
            int w = tex.width, h = tex.height;
            int wh = w * h;

            // var vp_matrix = cam.projectionMatrix * cam.worldToCameraMatrix;
            // vp_matrix = Matrix4x4.Scale(new Vector3(tex.width*0.5f, tex.height*0.5f, 1)) * vp_matrix;
            // vp_matrix = Matrix4x4.Translate(new Vector3(tex.width*0.5f, tex.height*0.5f, 0f)) * vp_matrix;
            var vp_matrix = cam.worldToCameraMatrix;
            float ah = cam.orthographicSize;
            float aw = (ah * tex.width) / tex.height;
            vp_matrix = Matrix4x4.Scale(new Vector3(tex.width * 0.5f / aw, tex.height * 0.5f / ah, -1)) * vp_matrix;
            vp_matrix = Matrix4x4.Translate(new Vector3(tex.width * 0.5f, tex.height * 0.5f, 0f)) * vp_matrix;

            vco_visible.Clear();
            WalkPointClouds((obj_rndr) => {
                if (IsVisibleFrom(obj_rndr, cam)) {
                    var vco_inst = new VCOInstance();
                    vco_inst.tfm = obj_rndr.transform;
                    vco_inst.mvp_matrix = vp_matrix * obj_rndr.transform.localToWorldMatrix;
                    vco_visible.Add(vco_inst);
                }
            });

            vco_visible.Sort((vcoA, vcoB) => {
                return vcoA.mvp_matrix.m23.CompareTo(vcoB.mvp_matrix.m23);
            });

            if (apc == null) {
                apc = new AcceleratedPointCloud(modelPath, voxelSize, modelAggregate);
            }

            // var obj2world = Matrix4x4.Scale(Vector3.one / 127f);
            // var mvp_matrix = vp_matrix * obj2world;

            // var world2obj = obj2world.inverse;

            // var cam_pos = world2obj.MultiplyPoint3x4(cam.transform.position);

            // var cam_near_pos = cam.transform.position + cam.transform.forward * cam.nearClipPlane;
            // cam_near_pos = world2obj.MultiplyPoint3x4(cam_near_pos);

            // // inverted for convenience (can use the same octant-determining code)
            // //var cam_dir = world2obj.MultiplyPoint3x4(cam.transform.forward);
            // var cam_dir = (cam_near_pos - cam_pos).normalized;
            // int bit_x = (cam_dir.x <= 0 ? 0 : 1);
            // int bit_y = (cam_dir.y <= 0 ? 0 : 2);
            // int bit_z = (cam_dir.z <= 0 ? 0 : 4);
            // int key_octant = bit_x | bit_y | bit_z;
            // var axis_order = AcceleratedPointCloud.CalcAxisOrder(mvp_matrix.m20, mvp_matrix.m21, mvp_matrix.m22);
            // int key_order = ((int)axis_order) << 3;
            // int queue_key = key_order | key_octant;

            apc.n_oct = apc.n_abs = apc.n_rel = 0;

            stopwatch.Reset();
            stopwatch.Start();

            // apc.Render(mvp_matrix, buf, BufW_shift, w, h, queue_key, dviz);

            foreach (var vco in vco_visible) {
                var obj2world = vco.tfm.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * voxel_scale); ;
                var mvp_matrix = vp_matrix * obj2world;
                var world2obj = obj2world.inverse;

                var cam_pos = world2obj.MultiplyPoint3x4(cam.transform.position);

                var cam_near_pos = cam.transform.position + cam.transform.forward * cam.nearClipPlane;
                cam_near_pos = world2obj.MultiplyPoint3x4(cam_near_pos);

                // inverted for convenience (can use the same octant-determining code)
                //var cam_dir = world2obj.MultiplyPoint3x4(cam.transform.forward);
                var cam_dir = (cam_near_pos - cam_pos).normalized;
                int bit_x = (cam_dir.x <= 0 ? 0 : 1);
                int bit_y = (cam_dir.y <= 0 ? 0 : 2);
                int bit_z = (cam_dir.z <= 0 ? 0 : 4);
                int key_octant = bit_x | bit_y | bit_z;
                int axis_order = OctantOrder.Order(mvp_matrix.m20, mvp_matrix.m21, mvp_matrix.m22);
                int queue_key = (axis_order << 3) | key_octant;

                apc.Render(mvp_matrix, buf, BufW_shift, w, h, queue_key, dviz);
            }

            BlitClearBuf();

            stopwatch.Stop();
            dt = (stopwatch.ElapsedMilliseconds / 1000f);

            tex.SetPixels32(0, 0, tex.width, tex.height, cbuf, 0);
            tex.Apply(false);

            // Debug.Log("Oct="+apc.n_oct+", Abs="+apc.n_abs+", Rel="+apc.n_rel);
        }

        void BlitClearBuf() {
            int w = tex.width, h = tex.height, wh = w * h;
            var clear_data = default(BufData);
            clear_data.zi = int.MaxValue;
            clear_data.c = cam.backgroundColor;
            clear_data.c.a = 255;
            int iy = 0, iyB = 0;
            for (; iy < wh;) {
                for (int x = 0; x < w; ++x) {
                    cbuf[iy + x] = buf[iyB + x].c;
                    buf[iyB + x] = clear_data;
                }
                iy += w; iyB += BufW;
            }
        }
    }
}

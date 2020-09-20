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

namespace dairin0d.Tests {
	public class HexagonRasterizationTest : MonoBehaviour {
		public int vSyncCount = 0;
		public int targetFrameRate = 30;

		public Transform PointCloudsParent;

		public int RenderSize = 0;

		public bool CustomRendering = false;

		public float voxel_scale = 0.5f;

		Camera cam;

		System.Diagnostics.Stopwatch stopwatch = null;

		struct BufData {
			public int zi;
			public Color32 c;
		}

		Texture2D tex;
		Color32[] cbuf;
		BufData[] buf;

		// Matrix4x4 contains translation vector
		// in the last column: m03=tX, m13=tY, m23=tZ

		void Start() {
			cam = GetComponent<Camera>();
			stopwatch = new System.Diagnostics.Stopwatch();
			ResizeDisplay();
		}
		
		void Update() {
			QualitySettings.vSyncCount = vSyncCount;
			Application.targetFrameRate = targetFrameRate;
			
			if (Input.GetKeyDown(KeyCode.Space)) CustomRendering = !CustomRendering;
			
			ResizeDisplay();
		}

		int _cull_mask;
		CameraClearFlags _clear_flags;
		void OnPreCull() {
			if (CustomRendering) {
				// _cull_mask = cam.cullingMask;
				// _clear_flags = cam.clearFlags;
				// cam.cullingMask = 0;
				// cam.clearFlags = CameraClearFlags.Nothing;
			}
		}
		void OnPostRender() {
			if (CustomRendering) {
				// cam.cullingMask = _cull_mask;
				// cam.clearFlags = _clear_flags;
				RenderPointClouds();
			}
		}

		public Color quad_color = Color.white;

		float dt = 0;
		void OnGUI() {
			if (CustomRendering) {
				GUI.color = quad_color;
				GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), tex);
				GUI.color = Color.black;
				string txt = string.Format("dt={0:0.000}", dt);
				GUI.Label(new Rect(0, Screen.height-20, Screen.width, 20), txt);
				GUI.color = Color.white;
			}
		}

		void ResizeDisplay() {
			float max_sz = Mathf.Max(Screen.width, Screen.height);
			var scale = new Vector2(Screen.width/max_sz, Screen.height/max_sz);
			int w = Screen.width, h = Screen.height;
			if (RenderSize > 0) {
				w = Mathf.Max(1, Mathf.RoundToInt(RenderSize*scale.x));
				h = Mathf.Max(1, Mathf.RoundToInt(RenderSize*scale.y));
			}
			if ((tex != null) && (w == tex.width) && (h == tex.height)) return;
			if (tex != null) Destroy(tex);
			tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
			tex.filterMode = FilterMode.Point;

			int size = w*h;
			cbuf = new Color32[size];

			BufW_shift = NextPow2(w);
			BufH_shift = NextPow2(h);
			BufH_shift = BufW_shift = Mathf.Max(BufW_shift, BufH_shift);
			BufW = 1 << BufW_shift;
			BufH = 1 << BufH_shift;

			buf = new BufData[BufW*BufH];

			BlitClearBuf();
		}

		int BufW_shift, BufH_shift;
		int BufW, BufH;

		static int NextPow2(int v) {
			return Mathf.CeilToInt(Mathf.Log(v) / Mathf.Log(2));
		}

		void WalkPointClouds(System.Action<Renderer> clb, Transform parent=null) {
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

		struct VCOInstance {
			public Transform tfm;
			public Matrix4x4 mvp_matrix;
		}

		List<VCOInstance> vco_visible = new List<VCOInstance>(64);
		void RenderPointClouds() {
			var bkg = (Color32)cam.backgroundColor;
			bkg.a = 255;
			int w = tex.width, h = tex.height;
			int wh = w * h;

			//var vp_matrix = cam.projectionMatrix * cam.worldToCameraMatrix;
			//vp_matrix = Matrix4x4.Scale(new Vector3(tex.width*0.5f, tex.height*0.5f, 1)) * vp_matrix;
			//vp_matrix = Matrix4x4.Translate(new Vector3(tex.width*0.5f, tex.height*0.5f, 0f)) * vp_matrix;
			var vp_matrix = cam.worldToCameraMatrix;
			float ah = cam.orthographicSize;
			float aw = (ah * tex.width) / tex.height;
			vp_matrix = Matrix4x4.Scale(new Vector3(tex.width*0.5f/aw, tex.height*0.5f/ah, -1)) * vp_matrix;
			vp_matrix = Matrix4x4.Translate(new Vector3(tex.width*0.5f, tex.height*0.5f, 0f)) * vp_matrix;

			vco_visible.Clear();
			WalkPointClouds((obj_rndr) => {
				if (!obj_rndr.enabled) return;
				if (!obj_rndr.gameObject.activeInHierarchy) return;
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

			stopwatch.Reset();
			stopwatch.Start();

			foreach (var vco in vco_visible) {
				var obj2world = vco.tfm.localToWorldMatrix * Matrix4x4.Scale(Vector3.one*voxel_scale);
				var mvp_matrix = vp_matrix * obj2world;
				Render(mvp_matrix, buf, BufW_shift, w, h);
			}

			BlitClearBuf();

			stopwatch.Stop();
			dt = (stopwatch.ElapsedMilliseconds/1000f);

			tex.SetPixels32(0, 0, tex.width, tex.height, cbuf, 0);
			tex.Apply(false);
		}

		void BlitClearBuf() {
			int w = tex.width, h = tex.height, wh = w * h;
			var clear_data = default(BufData);
			clear_data.zi = int.MaxValue;
			clear_data.c = cam.backgroundColor;
			clear_data.c.a = 255;
			int iy = 0, iyB = 0;
			for (;iy < wh;) {
				for (int x = 0; x < w; ++x) {
					cbuf[iy+x] = buf[iyB+x].c;
					buf[iyB+x] = clear_data;
				}
				iy += w; iyB += BufW;
			}
		}

		void Render(Matrix4x4 matrix, BufData[] buf, int buf_shift, int w, int h) {
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
			
			// int Xx=(int)(matrix.m00*pixel_size), Yx=(int)(matrix.m01*pixel_size), Zx=(int)(matrix.m02*pixel_size), Tx=(int)(matrix.m03*pixel_size);
			// int Xy=(int)(matrix.m10*pixel_size), Yy=(int)(matrix.m11*pixel_size), Zy=(int)(matrix.m12*pixel_size), Ty=(int)(matrix.m13*pixel_size);
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
			
			for (int i = 0; i < buf.Length; ++i) {
				buf[i].zi = 0;
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
									buf[ixy].zi |= mask;
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
			
			for (int i = 0; i < buf.Length; ++i) {
				int mask = buf[i].zi;
				buf[i].c.r = (byte)((mask & 0b1111) << 4);
				buf[i].c.g = (byte)(mask & 0b11110000);
				buf[i].c.b = 0;
				buf[i].c.a = 255;
			}
		}
	}
}
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
using dairin0d.Rendering;

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
				var rect = new Rect(0, Screen.height, Screen.width, 20);
				rect.y -= rect.height;
				GUI.Label(rect, $"dt={dt:0.000}");
				rect.y -= rect.height;
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

		OctantMap octantMap = new OctantMap();
		
		public int map_shift = 5;
		
		void Render(Matrix4x4 matrix, BufData[] buf, int buf_shift, int w, int h) {
			// Shape / size distortion is less noticeable than presence of gaps
			
			var X = new Vector2 {x = matrix.m00, y = matrix.m10};
			var Y = new Vector2 {x = matrix.m01, y = matrix.m11};
			var Z = new Vector2 {x = matrix.m02, y = matrix.m12};
			var T = new Vector2 {x = matrix.m03, y = matrix.m13};
			
			// Make hexagon slightly larger to make sure there will be
			// no gaps between this node and the neighboring nodes
			float sizeX = Mathf.Max(Mathf.Abs(X.x), Mathf.Abs(X.y));
			float sizeY = Mathf.Max(Mathf.Abs(Y.x), Mathf.Abs(Y.y));
			float sizeZ = Mathf.Max(Mathf.Abs(Z.x), Mathf.Abs(Z.y));
			float sizeMax = Mathf.Max(Mathf.Max(sizeX, sizeY), sizeZ);
			var scaleV = (sizeMax + 2f) / sizeMax;
			X *= scaleV;
			Y *= scaleV;
			Z *= scaleV;
			
			int Xx = (int)X.x, Yx = (int)Y.x, Zx = (int)Z.x, Tx = (int)T.x;
			int Xy = (int)X.y, Yy = (int)Y.y, Zy = (int)Z.y, Ty = (int)T.y;
			
			// Snap to 2-grid to align N+1 map with integer coordiantes in N map
			Xx = SnapTo2(Xx); Xy = SnapTo2(Xy);
			Yx = SnapTo2(Yx); Yy = SnapTo2(Yy);
			Zx = SnapTo2(Zx); Zy = SnapTo2(Zy);
			Tx = SnapTo2(Tx); Ty = SnapTo2(Ty);
			
			int extentX = (Xx < 0 ? -Xx : Xx) + (Yx < 0 ? -Yx : Yx) + (Zx < 0 ? -Zx : Zx);
			int extentY = (Xy < 0 ? -Xy : Xy) + (Yy < 0 ? -Yy : Yy) + (Zy < 0 ? -Zy : Zy);
			
			int map_size = 1 << map_shift;
			// We need 2-pixel margin to make sure that an intersection at level N is inside the map at level N+1
			float map_scale_factor = map_size / (map_size - 4f);
			
			// Use 1-pixel margin on all sides to make sure the hexagon is always inside
			int hexSize = (int)(((extentX > extentY ? extentX : extentY) + 1) * 2 * map_scale_factor);
			
			// Power-of-two bounding square
			int pot_shift = 2, pot_size = 1 << pot_shift;
			for (; pot_size < hexSize; pot_shift++, pot_size <<= 1);
			
			// Testing:
			
			void draw(int x, int y, Color32 color) {
				if ((x < 0) | (x >= w) | (y < 0) | (y >= h)) return;
				if (color.a == 255) {
					buf[x | (y << buf_shift)].c = color;
				} else {
					buf[x | (y << buf_shift)].c = Color32.Lerp(buf[x | (y << buf_shift)].c, color, color.a / 255f);
				}
			}
			
			int pot_half = pot_size >> 1;
			
			int minX = Tx - pot_half;
			int minY = Ty - pot_half;
			int maxX = minX + pot_size - 1;
			int maxY = minY + pot_size - 1;
			
			octantMap.Resize(map_shift);
			octantMap.Bake(Xx, Xy, Yx, Yy, Zx, Zy, Tx - minX, Ty - minY, pot_shift);
			
			Color32 bounds_color = Color.white;
			DrawLine(minX, minY, minX, maxY, bounds_color, draw);
			DrawLine(maxX, minY, maxX, maxY, bounds_color, draw);
			DrawLine(minX, minY, maxX, minY, bounds_color, draw);
			DrawLine(minX, maxY, maxX, maxY, bounds_color, draw);
			
			// int x0 = Mathf.Max(minX, 0);
			// int y0 = Mathf.Max(minY, 0);
			// int x1 = Mathf.Min(maxX, w-1);
			// int y1 = Mathf.Min(maxY, h-1);
			int x0 = Mathf.Max(Tx - extentX, 0);
			int y0 = Mathf.Max(Ty - extentY, 0);
			int x1 = Mathf.Min(Tx + extentX, w-1);
			int y1 = Mathf.Min(Ty + extentY, h-1);
			var mapData = octantMap.Data;
			int mapShift = octantMap.SizeShift;
			int mapSize = octantMap.Size;
			for (int y = y0, mapY = ((y - minY) << mapShift) ; y <= y1; y++, mapY += mapSize) {
				int my = mapY >> pot_shift;
				for (int x = x0, mapX = ((x - minX) << mapShift); x <= x1; x++, mapX += mapSize) {
					int mx = mapX >> pot_shift;
					int mask = mapData[mx | (my << mapShift)];
					if (mask != 0) {
						int i = x | (y << buf_shift);
						buf[i].c.r = (byte)((mask & 0b1111) << 4);
						buf[i].c.g = (byte)(mask & 0b11110000);
					}
				}
			}
			
			minX = Tx - extentX;
			minY = Ty - extentY;
			maxX = Tx + extentX;
			maxY = Ty + extentY;
			
			bounds_color = Color.magenta;
			bounds_color.a = 64;
			DrawLine(minX, minY, minX, maxY, bounds_color, draw);
			DrawLine(maxX, minY, maxX, maxY, bounds_color, draw);
			DrawLine(minX, minY, maxX, minY, bounds_color, draw);
			DrawLine(minX, maxY, maxX, maxY, bounds_color, draw);
			
			draw(Tx, Ty, Color.gray);
			draw(Tx+Xx, Ty+Xy, Color.red);
			draw(Tx+Yx, Ty+Yy, Color.green);
			draw(Tx+Zx, Ty+Zy, Color.blue);
			
			draw(Tx-Xx-Yx-Zx, Ty-Xy-Yy-Zy, Color.yellow);
			draw(Tx-Xx-Yx+Zx, Ty-Xy-Yy+Zy, Color.yellow);
			draw(Tx-Xx+Yx-Zx, Ty-Xy+Yy-Zy, Color.yellow);
			draw(Tx-Xx+Yx+Zx, Ty-Xy+Yy+Zy, Color.yellow);
			draw(Tx+Xx-Yx-Zx, Ty+Xy-Yy-Zy, Color.yellow);
			draw(Tx+Xx-Yx+Zx, Ty+Xy-Yy+Zy, Color.yellow);
			draw(Tx+Xx+Yx-Zx, Ty+Xy+Yy-Zy, Color.yellow);
			draw(Tx+Xx+Yx+Zx, Ty+Xy+Yy+Zy, Color.yellow);
		}
		
		int SnapTo2(int value) {
			if ((value & 1) == 0) return value;
			return value < 0 ? value-1 : value+1;
		}
		
		// http://ericw.ca/notes/bresenhams-line-algorithm-in-csharp.html
		static void DrawLine<T>(int x0, int y0, int x1, int y1, T value, System.Action<int, int, T> callback) {
			bool steep = Mathf.Abs(y1 - y0) > Mathf.Abs(x1 - x0);
			
			if (steep) {
				int t;
				t = x0; // swap x0 and y0
				x0 = y0;
				y0 = t;
				t = x1; // swap x1 and y1
				x1 = y1;
				y1 = t;
			}
			
			if (x0 > x1) {
				int t;
				t = x0; // swap x0 and x1
				x0 = x1;
				x1 = t;
				t = y0; // swap y0 and y1
				y0 = y1;
				y1 = t;
			}
			
			int dx = x1 - x0;
			int dy = Mathf.Abs(y1 - y0);
			int error = dx / 2;
			int ystep = (y0 < y1) ? 1 : -1;
			int y = y0;
			
			for (int x = x0; x <= x1; x++) {
				callback((steep ? y : x), (steep ? x : y), value);
				
				error = error - dy;
				
				if (error < 0) {
					y += ystep;
					error += dx;
				}
			}
		}
	}
}
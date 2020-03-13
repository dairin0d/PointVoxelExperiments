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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
		int subpixel_shift = 8;
		int pix = (1 << subpixel_shift), pix_half = pix >> 1;
		int xX=(int)(matrix.m00*pix), xY=(int)(matrix.m01*pix), xZ=(int)(matrix.m02*pix), xT=(int)(matrix.m03*pix);
		int yX=(int)(matrix.m10*pix), yY=(int)(matrix.m11*pix), yZ=(int)(matrix.m12*pix), yT=(int)(matrix.m13*pix);

		var nX = (new Vector2(-yX, xX));
		if (nX.x < 0) nX.x = -nX.x; if (nX.y < 0) nX.y = -nX.y;
		var nY = (new Vector2(-yY, xY));
		if (nY.x < 0) nY.x = -nY.x; if (nY.y < 0) nY.y = -nY.y;
		var nZ = (new Vector2(-yZ, xZ));
		if (nZ.x < 0) nZ.x = -nZ.x; if (nZ.y < 0) nZ.y = -nZ.y;

		xT -= pix_half;
		yT -= pix_half;

		int dotXM = Mathf.Max(Mathf.Abs(xX*(yY+yZ) - yX*(xY+xZ)), Mathf.Abs(xX*(yY-yZ) - yX*(xY-xZ)));
		int dotYM = Mathf.Max(Mathf.Abs(xY*(yX+yZ) - yY*(xX+xZ)), Mathf.Abs(xY*(yX-yZ) - yY*(xX-xZ)));
		int dotZM = Mathf.Max(Mathf.Abs(xZ*(yX+yY) - yZ*(xX+xY)), Mathf.Abs(xZ*(yX-yY) - yZ*(xX-xY)));

		dotXM >>= 1;
		dotYM >>= 1;
		dotZM >>= 1;

		dotXM += (int)((nX.x + nX.y) * pix_half + 0.5f);
		dotYM += (int)((nY.x + nY.y) * pix_half + 0.5f);
		dotZM += (int)((nZ.x + nZ.y) * pix_half + 0.5f);

		var c = default(Color32);
		c.a = 255;
		c.g = 255;

		int dotXdx = -yX << subpixel_shift;
		int dotXdy = xX << subpixel_shift;
		int dotYdx = -yY << subpixel_shift;
		int dotYdy = xY << subpixel_shift;
		int dotZdx = -yZ << subpixel_shift;
		int dotZdy = xZ << subpixel_shift;
		
		for (int i = 0; i < buf.Length; ++i) {
			buf[i].zi = 0;
		}
		
		int octant = 0;
		for (int subZ = -1; subZ <= 1; subZ += 2) {
			for (int subY = -1; subY <= 1; subY += 2) {
				for (int subX = -1; subX <= 1; subX += 2) {
					int cx = xT + ((xX*subX + xY*subY + xZ*subZ) >> 1);
					int cy = yT + ((yX*subX + yY*subY + yZ*subZ) >> 1);
					
					int dotXr = xX*(0-cy) - yX*(0-cx);
					int dotYr = xY*(0-cy) - yY*(0-cx);
					int dotZr = xZ*(0-cy) - yZ*(0-cx);
					
					int mask = 1 << octant;
					
					for (int iy = 0; iy < h; ++iy) {
						int ixy0 = (iy << buf_shift);
						int ixy1 = ixy0+w;
						int dotX = dotXr;
						int dotY = dotYr;
						int dotZ = dotZr;
						//for (int ix = 0; ix < w; ++ix) {
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

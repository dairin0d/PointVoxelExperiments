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
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

public class PointCloudRenderer2 : MonoBehaviour {
	public Transform PointCloudsParent;

	public int RenderSize = 0;

	public bool CustomRendering = false;

	public string modelPath = "";

	public float voxel_scale = 1f/128f;

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

    VoxModel vox_model;

	void Start() {
		cam = GetComponent<Camera>();
		stopwatch = new System.Diagnostics.Stopwatch();
		ResizeDisplay();

        vox_model = new VoxModel();
        vox_model.Load_VCO(modelPath);
	}
	
	void Update() {
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
			var obj2world = vco.tfm.localToWorldMatrix * Matrix4x4.Scale(Vector3.one*voxel_scale);;
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
			// var axis_order = AcceleratedPointCloud.CalcAxisOrder(mvp_matrix.m20, mvp_matrix.m21, mvp_matrix.m22);
			// int key_order = ((int)axis_order) << 3;
			// int queue_key = key_order | key_octant;

            vox_model.Render(mvp_matrix, buf, BufW_shift, w, h, key_octant);
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

    class VoxModel {
    	int octree_levels;
        public byte[] masks;
        public int[] nodes;
        public Color32[] colors;
        public byte[] color_ids;
        public Color32[] palette;

        struct Vector3i {
            public int x, y, z;
            public Vector3i(int x, int y, int z) {
                this.x = x; this.y = y; this.z = z;
            }
        }
        Vector3i[] deltas = new Vector3i[8];

        public void Render(Matrix4x4 matrix, BufData[] buf, int buf_shift, int w, int h, int key_octant) {
            int shift = 8;
            int s = (1 << shift);
            int S = s << 8;
            int xX=(int)(matrix.m00*s), xY=(int)(matrix.m01*s), xZ=(int)(matrix.m02*s), xT=(int)(matrix.m03*s);
            int yX=(int)(matrix.m10*s), yY=(int)(matrix.m11*s), yZ=(int)(matrix.m12*s), yT=(int)(matrix.m13*s);
            //int zX=(int)(matrix.m20*s), zY=(int)(matrix.m21*s), zZ=(int)(matrix.m22*s), zT=(int)(matrix.m23*s);
            int zX=(int)(matrix.m20*S), zY=(int)(matrix.m21*S), zZ=(int)(matrix.m22*S), zT=(int)(matrix.m23*S);

            deltas[0] = new Vector3i(-xX-xY-xZ, -yX-yY-yZ, -zX-zY-zZ);
            deltas[1] = new Vector3i(+xX-xY-xZ, +yX-yY-yZ, +zX-zY-zZ);
            deltas[2] = new Vector3i(-xX+xY-xZ, -yX+yY-yZ, -zX+zY-zZ);
            deltas[3] = new Vector3i(+xX+xY-xZ, +yX+yY-yZ, +zX+zY-zZ);
            deltas[4] = new Vector3i(-xX-xY+xZ, -yX-yY+yZ, -zX-zY+zZ);
            deltas[5] = new Vector3i(+xX-xY+xZ, +yX-yY+yZ, +zX-zY+zZ);
            deltas[6] = new Vector3i(-xX+xY+xZ, -yX+yY+yZ, -zX+zY+zZ);
            deltas[7] = new Vector3i(+xX+xY+xZ, +yX+yY+yZ, +zX+zY+zZ);

            int bx0 = 0, by0 = 0, bz0 = 0;
            int bx1 = 0, by1 = 0, bz1 = 0;
            for (int i = 0; i < 8; i++) {
                bx0 = Mathf.Min(bx0, deltas[i].x);
                by0 = Mathf.Min(by0, deltas[i].y);
                bz0 = Mathf.Min(bz0, deltas[i].z);
                bx1 = Mathf.Max(bx1, deltas[i].x);
                by1 = Mathf.Max(by1, deltas[i].y);
                bz1 = Mathf.Max(bz1, deltas[i].z);
            }

            int buf_size = 1 << buf_shift;
            int buf_mask = ~(buf_size-1);

            int level = octree_levels - 1;


            int xW0 = (xY+xZ) << level;
            int yW0 = (yY+yZ) << level;
            int xW1 = (xY-xZ) << level;
            int yW1 = (yY-yZ) << level;
            int dotXM = Mathf.Max(Mathf.Abs(xX*yW0 - yX*xW0), Mathf.Abs(xX*yW1 - yX*xW1));
            xW0 = (xX+xZ) << level;
            yW0 = (yX+yZ) << level;
            xW1 = (xX-xZ) << level;
            yW1 = (yX-yZ) << level;
            int dotYM = Mathf.Max(Mathf.Abs(xY*yW0 - yY*xW0), Mathf.Abs(xY*yW1 - yY*xW1));
            xW0 = (xX+xY) << level;
            yW0 = (yX+yY) << level;
            xW1 = (xX-xY) << level;
            yW1 = (yX-yY) << level;
            int dotZM = Mathf.Max(Mathf.Abs(xZ*yW0 - yZ*xW0), Mathf.Abs(xZ*yW1 - yZ*xW1));

            int dotXr = xX*(0-yT) - yX*(0-xT);
			int dotXdx = -yX << shift;
			int dotXdy = xX << shift;
            int dotYr = xY*(0-yT) - yY*(0-xT);
			int dotYdx = -yY << shift;
			int dotYdy = xY << shift;
            int dotZr = xZ*(0-yT) - yZ*(0-xT);
			int dotZdx = -yZ << shift;
			int dotZdy = xZ << shift;
            var c = default(Color32);
            c.a = 255;
			c.g = 255;
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
						buf[ixy].c = c;
					}
					dotX += dotXdx;
					dotY += dotYdx;
					dotZ += dotZdx;
                }
				dotXr += dotXdy;
				dotYr += dotYdy;
				dotZr += dotZdy;
            }

            // PlotDot(buf, buf_shift, buf_mask, (xT >> shift), (yT >> shift), Color.green);
            // for (int i = 0; i < 8; i++) {
            //     int px = xT + (deltas[i].x << level);
            //     int py = yT + (deltas[i].y << level);
            //     PlotDot(buf, buf_shift, buf_mask, (px >> shift), (py >> shift), Color.red);
            // }
            // {
            //     int px=0, py=0;
            //     px = xT + (bx0 << level); py = yT + (by0 << level);
            //     PlotDot(buf, buf_shift, buf_mask, (px >> shift), (py >> shift), Color.black);
            //     px = xT + (bx1 << level); py = yT + (by0 << level);
            //     PlotDot(buf, buf_shift, buf_mask, (px >> shift), (py >> shift), Color.black);
            //     px = xT + (bx0 << level); py = yT + (by1 << level);
            //     PlotDot(buf, buf_shift, buf_mask, (px >> shift), (py >> shift), Color.black);
            //     px = xT + (bx1 << level); py = yT + (by1 << level);
            //     PlotDot(buf, buf_shift, buf_mask, (px >> shift), (py >> shift), Color.black);
            // }
            
        }

		static int SignedArea32(int ax, int ay, int bx, int by, int cx, int cy) {
			return (bx-ax)*(cy-ay) - (by-ay)*(cx-ax);
		}

        void PlotDot(BufData[] buf, int buf_shift, int buf_mask, int ix, int iy, Color32 c) {
            if (((ix|iy) & buf_mask) == 0) {
                int ixy = ix | (iy << buf_shift);
                buf[ixy].c = c;
            }
        }

        public void Load_VCO(string cached_path) {
            var stream = new FileStream(cached_path, FileMode.Open, FileAccess.Read);
            var br = new BinaryReader(stream); {
                palette = new Color32[br.ReadInt32()];
                for (int i = 0; i < palette.Length; i++) {
                    palette[i].r = br.ReadByte();
                    palette[i].g = br.ReadByte();
                    palette[i].b = br.ReadByte();
                    palette[i].a = br.ReadByte();
                }
                octree_levels = br.ReadInt32();
                int viz_level = br.ReadInt32();
                masks = br.ReadBytes(br.ReadInt32());
                nodes = new int[br.ReadInt32()];
                for (int i = 0; i < nodes.Length; i++) {
                    nodes[i] = br.ReadInt32();
                }
                colors = new Color32[masks.Length];
                color_ids = new byte[masks.Length];
                int n_datas = br.ReadInt32();
                for (int i = 0; i < n_datas; i++) {
                    var datas_bytes = br.ReadBytes(br.ReadInt32());
                    if (datas_bytes.Length > 2) color_ids[i] = datas_bytes[2];
                    colors[i] = palette[color_ids[i]];
                }
            }
            stream.Close();
            stream.Dispose();

            MipmapColors(0);

            Debug.Log("Octree levels: "+octree_levels);
            Debug.Log("palette: "+palette.Length);
            Debug.Log("masks: "+masks.Length);
            Debug.Log("nodes: "+nodes.Length);
            Debug.Log("colors: "+colors.Length);
            Debug.Log("color_ids: "+color_ids.Length);
        }

        Color32 MipmapColors(int id) {
            int r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < 8; i++) {
                int child_id = nodes[(id << 3)|i];
                if (child_id < 0) continue;
                var c = MipmapColors(child_id);
                r += c.r; g += c.g; b += c.b; ++n;
            }
            if (n > 0) colors[id] = new Color32((byte)(r/n), (byte)(g/n), (byte)(b/n), 255);
            return colors[id];
        }
    }
}

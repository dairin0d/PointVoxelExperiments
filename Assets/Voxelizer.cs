using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/*
Render-based voxelizer
Pros:
	WYSIWYG (takes into account shader transformations)
	possibly better geometric quality (less oversampling)
	textures don't need to be set as readable
Cons:
	doesn't work due to ReadPixels/GetColors32 bug
	tiny/thin elements might not get rasterized
	likely slower than direct polygon sampling
*/

[ExecuteInEditMode]
public class Voxelizer : MonoBehaviour {
	public string savePath = "";

	public Vector3 size = Vector3.one;
	public float numerator = 1;
	public float denominator = 64;
	public bool relative = true;

	public bool handles1D = true;
	public bool handles2D = false;
	public bool handles3D = false;
	public bool colorizeHandles = false;

	public float pixelSize {
		get {
			float unit = numerator / denominator;
			if (relative) {
				unit *= Mathf.Max(Mathf.Max(size.x, size.y), size.z);
			}
			return unit;
		}
	}
	public float resolution {
		get { return 1f / pixelSize; }
	}

	public Vector3Int gridSize {
		get {
			var grid_size = size * resolution;
			return new Vector3Int(
				Mathf.Max(Mathf.RoundToInt(grid_size.x), 1),
				Mathf.Max(Mathf.RoundToInt(grid_size.y), 1),
				Mathf.Max(Mathf.RoundToInt(grid_size.z), 1));
		}
	}
	public Vector3 actualSize {
		get { return ((Vector3)gridSize) * pixelSize; }
	}
	public Bounds actualBounds {
		get { return new Bounds(Vector3.zero, actualSize); }
	}

	public string debugStr = "";

	// Harware may support bigger sizes, but we need it to be
	// small enough for interactive progress updates
	static int maxTexSize = 1024;
	Vector3Int slicesXYZ {
		get {
			var grid_size = gridSize;
			float coef = 1f / maxTexSize;
			return new Vector3Int(
				Mathf.CeilToInt(grid_size.x * coef),
				Mathf.CeilToInt(grid_size.x * coef),
				Mathf.CeilToInt(grid_size.x * coef));
		}
	}
	public int slicesCount {
		get {
			var grid_size = gridSize;
			var sn = slicesXYZ;
			return 2*(grid_size.x*sn.y*sn.z + grid_size.y*sn.x*sn.z + grid_size.z*sn.x*sn.y);
		}
	}
	public int sliceIndex = -1;

	Camera cam;
	public void SetupCamera() {
		if (sliceIndex < 0) {
			debugStr = (sliceIndex+"/"+slicesCount+" : OFF");
			//if (cam) { ContextualDestroy(cam.gameObject); cam = null; }
			if (cam) cam.gameObject.SetActive(false);
			return;
		}

		if (!cam) {
			cam = GetComponentInChildren<Camera>(true);
			if (!cam) cam = (new GameObject("VoxelizerCamera")).AddComponent<Camera>();
		}

		var cam_tfm = cam.transform;
		cam_tfm.SetParent(transform, false);
		cam.orthographic = true;
		cam.cullingMask = (projector ? ~projector.ignoreLayers : -1);
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = Color.clear;

		var grid_size = gridSize;
		var sn = slicesXYZ;
		int npx = sn.y*sn.z; // pieces per slice in X-directions
		int npy = sn.x*sn.z; // pieces per slice in Y-directions
		int npz = sn.x*sn.y; // pieces per slice in Z-directions
		int nx = grid_size.x*npx; // total pieces in X-direction
		int ny = grid_size.y*npy; // total pieces in Y-direction
		int nz = grid_size.z*npz; // total pieces in Z-direction
		int x_end = 2*nx;
		int y_end = x_end + 2*ny;
		int z_end = y_end + 2*nz;

		int slice_id = sliceIndex;
		int sign = 1;
		// Note: camera always looks in +Z direction
		var dirX = Vector3.right;
		var dirY = Vector3.up;
		var dirZ = Vector3.forward;
		int piece_z = 0, slice_d = 0;
		int piece_x = 0, piece_y = 0;
		int slice_w = 0, slice_h = 0;
		int piece_w = 0, piece_h = 0;
		if (slice_id < x_end) {
			if (slice_id >= nx) { slice_id -= nx; sign = -1; }
			piece_z = slice_id / npx; slice_d = grid_size.x;
			int piece_id = (slice_id - piece_z * npx);
			piece_x = piece_id % sn.z;
			piece_y = (piece_id - piece_x) / sn.z;
			slice_w = grid_size.z; slice_h = grid_size.y;
			piece_w = Mathf.CeilToInt(slice_w / (float)sn.z);
			piece_h = Mathf.CeilToInt(slice_h / (float)sn.y);
			dirX = new Vector3(0, 0, -sign);
			dirY = new Vector3(0, 1, 0);
			dirZ = new Vector3(sign, 0, 0);
			debugStr = (sliceIndex+"/"+slicesCount+" = "+sign+"*X: "+piece_z+", "+piece_x+", "+piece_y);
		} else if (slice_id < y_end) {
			slice_id -= x_end;
			if (slice_id >= ny) { slice_id -= ny; sign = -1; }
			piece_z = slice_id / npy; slice_d = grid_size.y;
			int piece_id = (slice_id - piece_z * npy);
			piece_x = piece_id % sn.x;
			piece_y = (piece_id - piece_x) / sn.x;
			slice_w = grid_size.x; slice_h = grid_size.z;
			piece_w = Mathf.CeilToInt(slice_w / (float)sn.x);
			piece_h = Mathf.CeilToInt(slice_h / (float)sn.z);
			dirX = new Vector3(1, 0, 0);
			dirY = new Vector3(0, 0, -sign);
			dirZ = new Vector3(0, sign, 0);
			debugStr = (sliceIndex+"/"+slicesCount+" = "+sign+"*Y: "+piece_z+", "+piece_x+", "+piece_y);
		} else if (slice_id < z_end) {
			slice_id -= y_end;
			if (slice_id >= nz) { slice_id -= nz; sign = -1; }
			piece_z = slice_id / npz; slice_d = grid_size.z;
			int piece_id = (slice_id - piece_z * npz);
			piece_x = piece_id % sn.x;
			piece_y = (piece_id - piece_x) / sn.x;
			slice_w = grid_size.x; slice_h = grid_size.y;
			piece_w = Mathf.CeilToInt(slice_w / (float)sn.x);
			piece_h = Mathf.CeilToInt(slice_h / (float)sn.y);
			dirX = new Vector3(0, 0, sign);
			dirY = new Vector3(0, 1, 0);
			dirZ = new Vector3(0, 0, sign);
			debugStr = (sliceIndex+"/"+slicesCount+" = "+sign+"*Z: "+piece_z+", "+piece_x+", "+piece_y);
		} else {
			cam.gameObject.SetActive(false);
			debugStr = (sliceIndex+"/"+slicesCount+" : FINISHED");
			return;
		}

		piece_x *= piece_w; piece_w = Mathf.Min(piece_w, slice_w - piece_x);
		piece_y *= piece_h; piece_h = Mathf.Min(piece_h, slice_h - piece_y);

		pieceW = piece_w; pieceH = piece_h;
		pieceDirX = Vector3RoundToInt(dirX);
		pieceDirY = Vector3RoundToInt(dirY);
		piecePos0 = new Vector3Int(piece_x, piece_y, piece_z);

		var position = new Vector3(piece_x - (slice_w-piece_w)*0.5f, piece_y - (slice_h-piece_h)*0.5f, piece_z+0.5f - slice_d*0.5f);
		position = (dirX * position.x + dirY * position.y + dirZ * position.z) * pixelSize;
		var rotation = Quaternion.LookRotation(dirZ, dirY);

		dirX = transform.TransformVector(dirX);
		dirY = transform.TransformVector(dirY);
		dirZ = transform.TransformVector(dirZ);

		var dirScaled = new Vector3(dirX.magnitude, dirY.magnitude, dirZ.magnitude);
		var global_size = Vector3.Scale(dirScaled, new Vector3(piece_w, piece_h, 1)*pixelSize);

		cam.gameObject.SetActive(true);
		cam_tfm.localRotation = rotation;
		cam_tfm.localPosition = position;
		cam.orthographicSize = global_size.y * 0.5f;
		cam.aspect = global_size.x / global_size.y;
		cam.nearClipPlane = -global_size.z*0.5f;
		cam.farClipPlane = global_size.z*0.5f;
	}

	Vector3Int piecePos0, pieceDirX, pieceDirY;
	int pieceW, pieceH;
	static Vector3Int Vector3RoundToInt(Vector3 v) {
		return new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
	}

	Texture2D tex2D;
	void RenderPiece() {
		if (!voxelize) {
			if (tex2D) {
				Destroy(tex2D);
				tex2D = null;
			}
			return;
		}
		if (tex2D) Destroy(tex2D);
		//var tex2D = new Texture2D(pieceW, pieceH, TextureFormat.ARGB32, false);
		tex2D = new Texture2D(pieceW, pieceH, TextureFormat.ARGB32, false);
		var rt_prev = RenderTexture.active;
		//var rt = new RenderTexture(pieceW, pieceH, 24, RenderTextureFormat.ARGB32);
		var rt = RenderTexture.GetTemporary(pieceW, pieceH, 24, RenderTextureFormat.ARGB32);
		rt.filterMode = FilterMode.Point;
//		rt.antiAliasing = 1;
//		rt.autoGenerateMips = false;
//		rt.useMipMap = false;
//		rt.useDynamicScale = false;
		var pixels0 = new Color32[pieceW*pieceH];
		pixels0[0] = Color.white;
		RenderTexture.active = rt;
		//cam.targetTexture = rt;
		cam.pixelRect = new Rect(0, 0, pieceW, pieceH);
		cam.Render();
		tex2D.ReadPixels(cam.pixelRect, 0, 0, false);
		tex2D.Apply(false, false);
//		tex2D.SetPixels32(pixels0);
//		tex2D.Apply(false, false);
		//cam.targetTexture = null;
		//RenderTexture.active = rt_prev;
		RenderTexture.active = null;
		RenderTexture.ReleaseTemporary(rt);
//		rt.Release();
//		Destroy(rt);
		//Destroy(tex2D);
		var pixels = tex2D.GetPixels32();
		int opaque_cnt = 0;
		for (int i = 0; i < pixels.Length; i++) {
			var c = pixels[i];
			opaque_cnt += c.a;
		}
		Debug.Log("Slice Index "+sliceIndex+" : "+opaque_cnt);
		var bytes = tex2D.EncodeToPNG();
		Debug.Log("PNG size "+bytes.Length);
	}

	void OnGUI() {
		if (tex2D) {
			GUI.DrawTexture(new Rect(0, 0, tex2D.width, tex2D.height), tex2D);
		}
	}

	static void ContextualDestroy(UnityEngine.Object obj) {
		if (Application.isPlaying) {
			UnityEngine.Object.Destroy(obj);
		} else {
			UnityEngine.Object.DestroyImmediate(obj);
		}
	}

	Projector projector;
	Material proj_mat;
	void SetupProjector() {
		if (!projector) {
			projector = GetComponent<Projector>();
			if (!projector) projector = gameObject.AddComponent<Projector>();
		}
		if (!proj_mat) {
			proj_mat = new Material(Shader.Find("Voxel/VoxelResolutionShader"));
			//proj_mat.hideFlags = HideFlags.DontSave;
		}
		// Hide projector when actually voxelizing
		if (Application.isPlaying) projector.enabled = false;
		projector.orthographic = true;
		var proj_size = Vector3.Scale(actualSize, transform.lossyScale);
		projector.orthographicSize = proj_size.y*0.5f;
		projector.aspectRatio = proj_size.x/proj_size.y;
		projector.nearClipPlane = -proj_size.z*0.5f;
		projector.farClipPlane = proj_size.z*0.5f;
		projector.material = proj_mat;
		var grid_size = gridSize;
		var _PixelScale = proj_mat.GetVector("_PixelScale");
		_PixelScale.x = grid_size.x;
		_PixelScale.y = grid_size.y;
		_PixelScale.z = grid_size.z;
		proj_mat.SetVector("_PixelScale", _PixelScale);
	}

	public bool voxelize = false;
	ProgressInfo progressInfo = null;
	void UpdateSliceIndex() {
		if (!Application.isPlaying) return;
		if (progressInfo != null) {
			voxelize &= (progressInfo.sliceIndex < slicesCount);
		}
		if (!voxelize) {
			if (progressInfo != null) {
				progressInfo.Dispose();
				progressInfo = null;
			}
			sliceIndex = -1;
			Time.timeScale = 1;
			return;
		}
		if (progressInfo == null) {
			progressInfo = new ProgressInfo();
			progressInfo.size = size;
			progressInfo.numerator = numerator;
			progressInfo.denominator = denominator;
			progressInfo.relative = relative;
			progressInfo.sliceIndex = 0;
			Time.timeScale = 0;
		}
		size = progressInfo.size;
		numerator = progressInfo.numerator;
		denominator = progressInfo.denominator;
		relative = progressInfo.relative;
		sliceIndex = progressInfo.sliceIndex;
		progressInfo.sliceIndex++;
	}

	void OnDestroy() {
		if (!Application.isPlaying) return;
		if (progressInfo != null) {
			progressInfo.Dispose();
			progressInfo = null;
		}
	}

	class ProgressInfo {
		public Vector3 size = Vector3.one;
		public float numerator = 1;
		public float denominator = 64;
		public bool relative = true;
		public int sliceIndex = 0;
		public void Dispose() {
			// close file, destroy texture, etc.
		}
	}

	void Update() {
		SetupProjector();
//		UpdateSliceIndex();
//		SetupCamera();
	}

	void LateUpdate() {
		if (!Application.isPlaying) return;
		UpdateSliceIndex();
		SetupCamera();
		RenderPiece();
	}

	void OnDrawGizmos() {
		var m = Gizmos.matrix;
		Gizmos.matrix = transform.localToWorldMatrix;
		//Gizmos.color = new Color(0, 0.5f, 0.7f, 0.25f);
		//Gizmos.DrawCube(Vector3.zero, actualSize);
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireCube(Vector3.zero, actualSize);
		Gizmos.matrix = m;
	}

	public static Voxelizer AddVoxelizer(Transform tfm) {
		if (!tfm) tfm = null;
		var bounds = CalcBounds(tfm);
		if (bounds.size.magnitude < Mathf.Epsilon) {
			bounds = new Bounds(Vector3.zero, Vector3.one);
		}
		var voxelizer_obj = new GameObject("Voxelizer");
		var voxelizer = voxelizer_obj.AddComponent<Voxelizer>();
		voxelizer.transform.SetParent(tfm, false);
		voxelizer.transform.localPosition = bounds.center;
		// Make it slightly larger so that boundary faces are included
		voxelizer.size = bounds.size * 1.0001f;
		return voxelizer;
	}

	static Bounds CalcBounds(Transform tfm) {
		Bounds bounds = default(Bounds); int count = 0;
		UpdateBounds(tfm, tfm.worldToLocalMatrix, ref bounds, ref count);
		return bounds;
	}
	static void UpdateBounds(Transform tfm, Matrix4x4 dst_matrix, ref Bounds bounds, ref int count) {
		var mesh = GetMesh(tfm);
		if (mesh) {
			var verts = mesh.vertices;
			if ((verts != null) && (verts.Length > 0)) {
				var matrix = dst_matrix * tfm.localToWorldMatrix;
				for (int vi = 0; vi < verts.Length; vi++) {
					var v = matrix.MultiplyPoint3x4(verts[vi]);
					if (count == 0) {
						bounds = new Bounds(v, Vector3.zero);
					} else {
						bounds.Encapsulate(v);
					}
					++count;
				}
			}
		}
		for (int i = 0; i < tfm.childCount; i++) {
			UpdateBounds(tfm.GetChild(i), dst_matrix, ref bounds, ref count);
		}
	}
	static Mesh GetMesh(Transform tfm) {
		var skin_renderer = tfm.GetComponent<SkinnedMeshRenderer>();
		if (skin_renderer) {
			var mesh = new Mesh();
			skin_renderer.BakeMesh(mesh);
			mesh.hideFlags = HideFlags.HideAndDontSave;
			mesh.name = "BakedMesh:Delete";
			return mesh;
		}
		var mesh_filter = tfm.GetComponent<MeshFilter>();
		if (mesh_filter) {
			return mesh_filter.sharedMesh;
		}
		return null;
	}
}

#if UNITY_EDITOR
[CustomEditor(typeof(Voxelizer)), CanEditMultipleObjects]
public class VoxelizerEditor : Editor {
	protected virtual void OnSceneGUI() {
		var voxelizer = (Voxelizer)target;
		var m = Handles.matrix;
		var c = Handles.color;
		Handles.matrix = voxelizer.transform.localToWorldMatrix;
		Handles.color = Color.white;
		for (int z = -1; z <= 1; z++) {
			int az = System.Math.Abs(z);
			for (int y = -1; y <= 1; y++) {
				int ay = System.Math.Abs(y);
				for (int x = -1; x <= 1; x++) {
					int ax = System.Math.Abs(x);
					int sum = ax+ay+az;
					if (sum == 0) continue;
					if ((sum == 1) & !voxelizer.handles1D) continue;
					if ((sum == 2) & !voxelizer.handles2D) continue;
					if ((sum == 3) & !voxelizer.handles3D) continue;
					if (voxelizer.colorizeHandles) {
						Handles.color = new Color(ax, ay, az, 0.5f);
					}
					SizeHandle(voxelizer, new Vector3(x, y, z));
				}
			}
		}
		Handles.color = c;
		Handles.matrix = m;
	}

	void SizeHandle(Voxelizer voxelizer, Vector3 direction) {
		Vector3 size = Vector3.Max(voxelizer.size, Vector3.zero);
		Vector3 hpos = Vector3.Scale(size, direction)*0.5f;
		Vector3 mask = Vector3.Max(direction, -direction);
		Vector3 inv_pos = voxelizer.transform.TransformPoint(-hpos);
		EditorGUI.BeginChangeCheck();
		hpos = Vector3.Scale(PointHandle(hpos), mask);
		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(voxelizer, "Change voxelizer bounds");
			Undo.RecordObject(voxelizer.transform, "Change voxelizer bounds");
			var masked_size = Vector3.Scale(hpos, direction)*2f;
			voxelizer.size = Vector3.Scale(size, Vector3.one - mask) + masked_size;
			Vector3 delta = inv_pos - voxelizer.transform.TransformPoint(-hpos);
			voxelizer.transform.position += delta;
		}
	}

	Vector3 PointHandle(Vector3 p) {
		float size = HandleUtility.GetHandleSize(p) * 0.05f;
		return Handles.FreeMoveHandle(p, Quaternion.identity, size, Vector3.zero, Handles.DotHandleCap);
	}

	[MenuItem("Voxel Tools/Add Voxelizer", priority=1)]
	static void AddVoxelizer() {
		Voxelizer.AddVoxelizer(Selection.activeTransform);
	}
}
#endif

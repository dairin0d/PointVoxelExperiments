﻿// MIT License
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

using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;

public class IsoCamera : MonoBehaviour {
	public Transform target;

	public float distance = 10f;
	public float xSpeed = 250f;
	public float ySpeed = 250f;
	public float yMinLimit = -85f;
	public float yMaxLimit = 85f;

	public bool onlyDrag = false;
	public int dragButton = 0;
	
	public float persp_adjust = 1f;
	
	public float distance_adjustment = 0f;
	public float x = 0f;
	public float y = 0f;
	public float orthoSize0 = 0f;
			
	public int pixel_scale = 1;
	public float angle_snap_x = -1f;
	public float angle_snap_y = -1f;

	private float dt = 0f;
	private float next_dt_update = 0f;
	private float dt_accum = 0f, dt_count = 0f;

	private Stopwatch stopwatch = null;

	Camera cam = null;

	void Start() {
		var angles = transform.eulerAngles;
		x = angles.y;
		y = angles.x;

		cam = GetComponent<Camera>();
		orthoSize0 = cam.orthographicSize;

		stopwatch = new Stopwatch();
		stopwatch.Start();
	}
	
	void Update() {
		if (Input.GetKeyDown(KeyCode.Space)) {
			cam.orthographic = !cam.orthographic;
		}
	}

	void LateUpdate() {
		//dt = Time.deltaTime
		dt_accum += (stopwatch.ElapsedMilliseconds * 0.001f);
		dt_count += 1;
		stopwatch.Reset();
		stopwatch.Start();

		if (Time.realtimeSinceStartup > next_dt_update) {
			next_dt_update = Time.realtimeSinceStartup + 0.5f;
			dt = dt_accum / dt_count;
			dt_accum = 0;
			dt_count = 0;
		}
				
		if (target == null) return;
		
		if (onlyDrag && !Input.GetMouseButton(dragButton)) {
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
			//Screen.lockCursor = false; // obsolete
		} else {
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
			//Screen.lockCursor = true; // obsolete
			x += Input.GetAxisRaw("Mouse X") * xSpeed * 0.02f;
			y -= Input.GetAxisRaw("Mouse Y") * ySpeed * 0.02f;
		}
		
		y = ClampAngle(y, yMinLimit, yMaxLimit);
		
		var mousePos = Input.mousePosition;
		if (cam.pixelRect.Contains(mousePos)) {
			distance_adjustment -= Input.mouseScrollDelta.y;
		}
		
		var dist = distance;
		if (cam.orthographic) {
			cam.orthographicSize = orthoSize0 * Mathf.Pow(2f, (distance_adjustment/4f));
		} else {
			dist = dist * Mathf.Pow(2f, (distance_adjustment/4f)) * persp_adjust;
		}
		
		var rotation = Quaternion.Euler(SnapAngle(y, angle_snap_y), SnapAngle(x, angle_snap_x), 0f);
		var position = rotation * (new Vector3(0f, 0f, -dist)) + target.position;
		
		transform.rotation = rotation;
		transform.position = position;
		
		if (cam.orthographic) {
			var pixel_size = (cam.orthographicSize / cam.pixelHeight) * 2 * pixel_scale;
			var axis_x = transform.right.normalized;
			var axis_y = transform.up.normalized;
			var axis_z = transform.forward.normalized;
			var pixel_x = Mathf.Round(Vector3.Dot(axis_x, position) / pixel_size) * pixel_size;
			var pixel_y = Mathf.Round(Vector3.Dot(axis_y, position) / pixel_size) * pixel_size;
			var pixel_z = Mathf.Round(Vector3.Dot(axis_z, position) / pixel_size) * pixel_size;
			position = axis_x*pixel_x + axis_y*pixel_y + axis_z*pixel_z;
			transform.position = position;
		}
	}

	static float SnapAngle(float a, float angle_snap) {
		if (angle_snap <= 0f) return a;
		return Mathf.Round(a / angle_snap) * angle_snap;
	}
	
	static float ClampAngle(float angle, float min, float max) {
		if (angle < -360f) angle += 360f;
		if (angle > 360f) angle -= 360f;
		return Mathf.Clamp(angle, min, max);
	}

	void OnGUI() {
		int line_h = 20;
		int line_y = 0;
		
		DrawBox(new Rect(0, 0, 100, line_h*2));
		
		GUI.Label(new Rect(0, line_y, 200, 20), Screen.width+" x "+Screen.height);
		line_y += line_h;
		GUI.Label(new Rect(0, line_y, 200, 20), "FPS="+(1f/dt).ToString("0.000"));
		line_y += line_h;
		
		void DrawBox(Rect rect, int repeats=2) {
			for (; repeats > 0; repeats--) {
				GUI.Box(rect, "");
			}
		}
	}
}

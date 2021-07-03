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

using UnityEngine;

namespace dairin0d.Controls {
	public class PlayerController : MonoBehaviour {
		public float speed = 1;
		public float speed_modifier = 4;

		void Start() {
		}
		
		void Update() {
			var cam = Camera.main;
			var dir_right = cam.transform.right;
			var dir_up = Vector3.up;
			var dir_forward = Vector3.Cross(dir_right, dir_up);

			bool is_x_pos = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow));
			bool is_x_neg = (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow));
			bool is_y_pos = (Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.PageUp));
			bool is_y_neg = (Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.PageDown));
			bool is_z_pos = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow));
			bool is_z_neg = (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow));

			int speed_x = (is_x_pos?1:0) - (is_x_neg?1:0);
			int speed_y = (is_y_pos?1:0) - (is_y_neg?1:0);
			int speed_z = (is_z_pos?1:0) - (is_z_neg?1:0);
			Vector3 speed_v = (speed_x * dir_right) + (speed_y * dir_up) + (speed_z * dir_forward);

			if (speed_v.magnitude > 0) {
				if (speed_modifier > 1e-5f) {
					if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed_v /= speed_modifier;
					if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) speed_v *= speed_modifier;
				}

				transform.position += speed_v * speed * Time.deltaTime;
				transform.rotation = Quaternion.LookRotation(dir_forward);
			}

			if (Input.GetKey(KeyCode.Escape)) Application.Quit();
		}
	}
}
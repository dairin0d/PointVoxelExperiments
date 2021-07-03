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

namespace dairin0d.Data.Colors {
    public struct ColorHSV {
		public float h, s, v, a;
		
		public ColorHSV(float h, float s, float v, float a=1f) {
			this.h = h;
			this.s = s;
			this.v = v;
			this.a = a;
		}
		
		public void Sanitize() {
			h = ((h % 1f) + 1f) % 1f;
			s = Mathf.Clamp01(s);
			v = Mathf.Clamp01(v);
			a = Mathf.Clamp01(a);
		}

		public Vector4 ToCoord() {
			float height = Mathf.Clamp01(v) - 0.5f;
			float radius = Mathf.Clamp01(s);
			float angle = (((h % 1f) + 1f) % 1f) * (Mathf.PI * 2f);
			return new Vector4(radius*Mathf.Cos(angle), radius*Mathf.Sin(angle), height, a);
		}

		// expects/returns values in range [0..1]
		public static implicit operator ColorHSV(Color c) {
			float minVal = Mathf.Min(Mathf.Min(c.r, c.g), c.b);
			float maxVal = Mathf.Max(Mathf.Max(c.r, c.g), c.b);
			float delta = maxVal - minVal;
			
			float v = maxVal;
			
			if (delta == 0f) return new ColorHSV(0f, 0f, v, c.a);
			float s = delta / maxVal;
			
			float h;
			if (c.r == maxVal) {
				h = (c.g - c.b) / (6f * delta);
			} else if (c.g == maxVal) {
				h = (1f / 3f) + (c.b - c.r) / (6f * delta);
			} else {
				h = (2f / 3f) + (c.r - c.g) / (6f * delta);
			}
			h = ((h % 1f) + 1f) % 1f;
			
			return new ColorHSV(h, s, v, c.a);
		}
		public static implicit operator ColorHSV(Color32 c) {
			return (Color)c;
		}
		
		// expects/returns values in range [0..1]
		public static implicit operator Color(ColorHSV c) {
			float s = Mathf.Clamp01(c.s);
			float v = Mathf.Clamp01(c.v);
			if (s == 0f) return new Color(v, v, v, c.a);
			float h = ((c.h % 1f) + 1f) % 1f;
			var var_h = h * 6f;
			var var_i = Mathf.FloorToInt(var_h);
			var var_1 = v * (1f - s);
			var var_2 = v * (1f - s * (var_h - var_i));
			var var_3 = v * (1f - s * (1f - (var_h - var_i)));
			
			switch (var_i) {
			case 1: return new Color(var_2, v, var_1, c.a);
			case 2: return new Color(var_1, v, var_3, c.a);
			case 3: return new Color(var_1, var_2, v, c.a);
			case 4: return new Color(var_3, var_1, v, c.a);
			case 5: return new Color(v, var_1, var_2, c.a);
			default: return new Color(v, var_3, var_1, c.a); // 0 or 6
			}
		}
		public static implicit operator Color32(ColorHSV c) {
			return (Color)c;
		}
	}
}
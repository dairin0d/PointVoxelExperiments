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
    public class ColorHSVPalette {
		struct ColorCoord {
			public float h, s, v, a;
			public float x, y, z;
			public float d, d2;
			public int id;
			public ColorCoord(ColorHSV c, int id = -1) {
				h = c.h; s = c.s; v = c.v; a = c.a;
				var coord = c.ToCoord();
				x = coord.x; y = coord.y; z = coord.z;
				d = 0f; d2 = 0f;
				this.id = id;
			}
			public ColorHSV ToColor() {
				return new ColorHSV(h, s, v, a);
			}
			public float SqrDistance(ColorCoord c, bool use_alpha) {
				float dx = x - c.x;
				float dy = y - c.y;
				float dz = z - c.z;
				if (!use_alpha) return dx*dx + dy*dy + dz*dz;
				float da = a - c.a;
				return dx*dx + dy*dy + dz*dz + da*da;
			}
			public void CalcDistance(ColorCoord c, bool use_alpha) {
				d2 = SqrDistance(c, use_alpha);
				d = Mathf.Sqrt(d2);
			}
		}
		ColorCoord[] palette;

		public ColorHSVPalette(ColorHSV[] palette) {
			this.palette = new ColorCoord[palette.Length];
			for (int i = 0; i < palette.Length; ++i) {
				this.palette[i] = new ColorCoord(palette[i], i);
			}
		}
		
		public ColorHSV this[int id] {
			get {
				for (int i = 0; i < palette.Length; ++i) {
					var c = palette[i];
					if (c.id == id) return c.ToColor();
				}
				return default(ColorHSV);
			}
		}
		
		public ColorHSV[] ToArray() {
			var result = new ColorHSV[palette.Length];
			for (int i = 0; i < result.Length; i++) {
				result[i] = this[i];
			}
			return result;
		}

		public ColorHSV FindColor(ColorHSV c, bool use_alpha=false, int size=0) {
			int index = 0;
			return Find(out index, c, use_alpha, size);
		}
		public int FindIndex(ColorHSV c, bool use_alpha=false, int size=0) {
			int index = 0;
			Find(out index, c, use_alpha, size);
			return index;
		}
		public ColorHSV Find(out int index, ColorHSV c, bool use_alpha=false, int size=0) {
			var _c = new ColorCoord(c);
			var best_color = _c;

			size = Mathf.Min(size, palette.Length);
			if (size > 1) {
				for (int i = 0; i < palette.Length; i++) {
					palette[i].CalcDistance(_c, use_alpha);
				}
				System.Array.Sort(palette, (cA, cB) => { return cA.d.CompareTo(cB.d); });
				if (palette[0].d <= Vector4.kEpsilon) {
					best_color = palette[0];
				} else {
					float inv_sum = 0f;
					for (int i = 0; i < size; i++) {
						inv_sum += 1f / palette[i].d;
					}
					float choice = Random.value * inv_sum;
					inv_sum = 0f;
					best_color = palette[size-1];
					for (int i = 0; i < size; i++) {
						inv_sum += 1f / palette[i].d;
						if (choice <= inv_sum) {
							best_color = palette[i];
							break;
						}
					}
				}
			} else {
				var best_dist = 100f; // sufficiently big
				for (int i = 0; i < palette.Length; i++) {
					var pc = palette[i];
					var dist = pc.SqrDistance(_c, use_alpha);
					if (dist < best_dist) {
						best_dist = dist;
						best_color = pc;
					}
				}
			}

			index = best_color.id;
			return best_color.ToColor();
		}
	}
}
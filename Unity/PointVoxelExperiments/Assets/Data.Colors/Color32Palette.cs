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
    public class Color32Palette {
		struct ColorCoord {
			public int r, g, b, a;
			public float d, d2;
			public int id;
			public ColorCoord(Color32 c, int id = -1) {
				r = c.r; g = c.g; b = c.b; a = c.a;
				d = 0f; d2 = 0f;
				this.id = id;
			}
			public Color32 ToColor() {
				return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
			}
			public float SqrDistance(ColorCoord c, bool use_alpha) {
				int dr = r - c.r;
				int dg = g - c.g;
				int db = b - c.b;
				if (!use_alpha) return dr*dr + dg*dg + db*db;
				int da = a - c.a;
				return dr*dr + dg*dg + db*db + da*da;
			}
			public void CalcDistance(ColorCoord c, bool use_alpha) {
				d2 = SqrDistance(c, use_alpha);
				d = Mathf.Sqrt(d2);
			}
		}
		ColorCoord[] palette;
		
		public Color32Palette(Color32[] palette) {
			this.palette = new ColorCoord[palette.Length];
			for (int i = 0; i < palette.Length; ++i) {
				this.palette[i] = new ColorCoord(palette[i], i);
			}
		}

		public Color32 this[int id] {
			get {
				for (int i = 0; i < palette.Length; ++i) {
					var c = palette[i];
					if (c.id == id) return c.ToColor();
				}
				return default(Color32);
			}
		}

		public Color32[] ToArray() {
			var result = new Color32[palette.Length];
			for (int i = 0; i < result.Length; i++) {
				result[i] = this[i];
			}
			return result;
		}

		public Color32 FindColor(Color32 c, bool use_alpha=false, int size=0) {
			int index = 0;
			return Find(out index, c, use_alpha, size);
		}
		public int FindIndex(Color32 c, bool use_alpha=false, int size=0) {
			int index = 0;
			Find(out index, c, use_alpha, size);
			return index;
		}
		public Color32 Find(out int index, Color32 c, bool use_alpha=false, int size=0) {
			var _c = new ColorCoord(c);
			var best_color = _c;
			
			size = Mathf.Min(size, palette.Length);
			if (size > 1) {
				for (int i = 0; i < palette.Length; i++) {
					palette[i].CalcDistance(_c, use_alpha);
				}
				System.Array.Sort(palette, (cA, cB) => { return cA.d2.CompareTo(cB.d2); });
				if (palette[0].d <= 0.5f) {
					best_color = palette[0];
				} else {
					float inv_sum = 0f;
					for (int i = 0; i < size; i++) {
						inv_sum += 1f / palette[i].d2;
					}
					float choice = Random.value * inv_sum;
					inv_sum = 0f;
					best_color = palette[size-1];
					for (int i = 0; i < size; i++) {
						inv_sum += 1f / palette[i].d2;
						if (choice <= inv_sum) {
							best_color = palette[i];
							break;
						}
					}
				}
			} else {
				float best_dist = 260101; // (255^2)*4 + 1
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

		public static byte ReduceChannel(byte chn, int bits) {
			if (bits > 8) return chn;
			if (bits < 0) return 255;
			int shift = 8 - bits;
			return (byte)((chn >> shift) << shift);
		}
		public static Color32 ReduceColor(Color32 c, int nr, int ng, int nb, int na) {
			c.r = ReduceChannel(c.r, nr);
			c.g = ReduceChannel(c.g, ng);
			c.b = ReduceChannel(c.b, nb);
			c.a = ReduceChannel(c.a, na);
			return c;
		}

		//static Color32Palette palette_8_bit;
		static Color32Palette palette_8_bit = new Color32Palette(new Color32[] {
			new Color32(0, 0, 0, 255),
			new Color32(0, 0, 102, 255),
			new Color32(42, 0, 51, 255),
			new Color32(0, 0, 204, 255),
			new Color32(85, 0, 0, 255),
			new Color32(42, 0, 153, 255),
			new Color32(0, 23, 51, 255),
			new Color32(42, 23, 0, 255),
			new Color32(85, 0, 102, 255),
			new Color32(42, 0, 255, 255),
			new Color32(0, 23, 153, 255),
			new Color32(127, 0, 51, 255),
			new Color32(42, 23, 102, 255),
			new Color32(85, 0, 204, 255),
			new Color32(0, 46, 0, 255),
			new Color32(0, 23, 255, 255),
			new Color32(170, 0, 0, 255),
			new Color32(127, 0, 153, 255),
			new Color32(85, 23, 51, 255),
			new Color32(42, 23, 204, 255),
			new Color32(0, 46, 102, 255),
			new Color32(127, 23, 0, 255),
			new Color32(170, 0, 102, 255),
			new Color32(127, 0, 255, 255),
			new Color32(42, 46, 51, 255),
			new Color32(85, 23, 153, 255),
			new Color32(0, 46, 204, 255),
			new Color32(212, 0, 51, 255),
			new Color32(127, 23, 102, 255),
			new Color32(170, 0, 204, 255),
			new Color32(85, 46, 0, 255),
			new Color32(51, 51, 51, 255),
			new Color32(42, 46, 153, 255),
			new Color32(85, 23, 255, 255),
			new Color32(0, 69, 51, 255),
			new Color32(255, 0, 0, 255),
			new Color32(212, 0, 153, 255),
			new Color32(170, 23, 51, 255),
			new Color32(127, 23, 204, 255),
			new Color32(42, 69, 0, 255),
			new Color32(85, 46, 102, 255),
			new Color32(42, 46, 255, 255),
			new Color32(0, 69, 153, 255),
			new Color32(212, 23, 0, 255),
			new Color32(255, 0, 102, 255),
			new Color32(212, 0, 255, 255),
			new Color32(127, 46, 51, 255),
			new Color32(170, 23, 153, 255),
			new Color32(42, 69, 102, 255),
			new Color32(85, 46, 204, 255),
			new Color32(0, 92, 0, 255),
			new Color32(0, 69, 255, 255),
			new Color32(212, 23, 102, 255),
			new Color32(255, 0, 204, 255),
			new Color32(170, 46, 0, 255),
			new Color32(127, 46, 153, 255),
			new Color32(170, 23, 255, 255),
			new Color32(85, 69, 51, 255),
			new Color32(42, 69, 204, 255),
			new Color32(0, 92, 102, 255),
			new Color32(255, 23, 51, 255),
			new Color32(212, 23, 204, 255),
			new Color32(127, 69, 0, 255),
			new Color32(170, 46, 102, 255),
			new Color32(127, 46, 255, 255),
			new Color32(42, 92, 51, 255),
			new Color32(85, 69, 153, 255),
			new Color32(0, 92, 204, 255),
			new Color32(212, 46, 51, 255),
			new Color32(255, 23, 153, 255),
			new Color32(127, 69, 102, 255),
			new Color32(170, 46, 204, 255),
			new Color32(85, 92, 0, 255),
			new Color32(42, 92, 153, 255),
			new Color32(85, 69, 255, 255),
			new Color32(0, 115, 51, 255),
			new Color32(255, 46, 0, 255),
			new Color32(212, 46, 153, 255),
			new Color32(255, 23, 255, 255),
			new Color32(170, 69, 51, 255),
			new Color32(127, 69, 204, 255),
			new Color32(42, 115, 0, 255),
			new Color32(85, 92, 102, 255),
			new Color32(42, 92, 255, 255),
			new Color32(0, 115, 153, 255),
			new Color32(212, 69, 0, 255),
			new Color32(255, 46, 102, 255),
			new Color32(212, 46, 255, 255),
			new Color32(127, 92, 51, 255),
			new Color32(170, 69, 153, 255),
			new Color32(42, 115, 102, 255),
			new Color32(85, 92, 204, 255),
			new Color32(0, 139, 0, 255),
			new Color32(0, 115, 255, 255),
			new Color32(212, 69, 102, 255),
			new Color32(255, 46, 204, 255),
			new Color32(170, 92, 0, 255),
			new Color32(102, 102, 102, 255),
			new Color32(127, 92, 153, 255),
			new Color32(170, 69, 255, 255),
			new Color32(85, 115, 51, 255),
			new Color32(42, 115, 204, 255),
			new Color32(0, 139, 102, 255),
			new Color32(255, 69, 51, 255),
			new Color32(212, 69, 204, 255),
			new Color32(127, 115, 0, 255),
			new Color32(170, 92, 102, 255),
			new Color32(127, 92, 255, 255),
			new Color32(85, 115, 153, 255),
			new Color32(42, 139, 51, 255),
			new Color32(0, 139, 204, 255),
			new Color32(212, 92, 51, 255),
			new Color32(255, 69, 153, 255),
			new Color32(127, 115, 102, 255),
			new Color32(170, 92, 204, 255),
			new Color32(85, 139, 0, 255),
			new Color32(85, 115, 255, 255),
			new Color32(42, 139, 153, 255),
			new Color32(0, 162, 51, 255),
			new Color32(255, 92, 0, 255),
			new Color32(212, 92, 153, 255),
			new Color32(255, 69, 255, 255),
			new Color32(170, 115, 51, 255),
			new Color32(127, 115, 204, 255),
			new Color32(42, 162, 0, 255),
			new Color32(85, 139, 102, 255),
			new Color32(42, 139, 255, 255),
			new Color32(0, 162, 153, 255),
			new Color32(212, 115, 0, 255),
			new Color32(255, 92, 102, 255),
			new Color32(212, 92, 255, 255),
			new Color32(170, 115, 153, 255),
			new Color32(127, 139, 51, 255),
			new Color32(42, 162, 102, 255),
			new Color32(85, 139, 204, 255),
			new Color32(0, 185, 0, 255),
			new Color32(0, 162, 255, 255),
			new Color32(212, 115, 102, 255),
			new Color32(255, 92, 204, 255),
			new Color32(170, 139, 0, 255),
			new Color32(170, 115, 255, 255),
			new Color32(127, 139, 153, 255),
			new Color32(85, 162, 51, 255),
			new Color32(42, 162, 204, 255),
			new Color32(0, 185, 102, 255),
			new Color32(255, 115, 51, 255),
			new Color32(212, 115, 204, 255),
			new Color32(127, 162, 0, 255),
			new Color32(170, 139, 102, 255),
			new Color32(127, 139, 255, 255),
			new Color32(42, 185, 51, 255),
			new Color32(85, 162, 153, 255),
			new Color32(0, 185, 204, 255),
			new Color32(255, 115, 153, 255),
			new Color32(212, 139, 51, 255),
			new Color32(127, 162, 102, 255),
			new Color32(170, 139, 204, 255),
			new Color32(85, 185, 0, 255),
			new Color32(42, 185, 153, 255),
			new Color32(85, 162, 255, 255),
			new Color32(0, 208, 51, 255),
			new Color32(153, 153, 153, 255),
			new Color32(255, 139, 0, 255),
			new Color32(255, 115, 255, 255),
			new Color32(212, 139, 153, 255),
			new Color32(170, 162, 51, 255),
			new Color32(127, 162, 204, 255),
			new Color32(42, 208, 0, 255),
			new Color32(85, 185, 102, 255),
			new Color32(42, 185, 255, 255),
			new Color32(0, 208, 153, 255),
			new Color32(212, 162, 0, 255),
			new Color32(255, 139, 102, 255),
			new Color32(212, 139, 255, 255),
			new Color32(127, 185, 51, 255),
			new Color32(170, 162, 153, 255),
			new Color32(42, 208, 102, 255),
			new Color32(85, 185, 204, 255),
			new Color32(0, 231, 0, 255),
			new Color32(0, 208, 255, 255),
			new Color32(212, 162, 102, 255),
			new Color32(255, 139, 204, 255),
			new Color32(170, 185, 0, 255),
			new Color32(127, 185, 153, 255),
			new Color32(170, 162, 255, 255),
			new Color32(85, 208, 51, 255),
			new Color32(42, 208, 204, 255),
			new Color32(0, 231, 102, 255),
			new Color32(255, 162, 51, 255),
			new Color32(212, 162, 204, 255),
			new Color32(127, 208, 0, 255),
			new Color32(170, 185, 102, 255),
			new Color32(127, 185, 255, 255),
			new Color32(42, 231, 51, 255),
			new Color32(85, 208, 153, 255),
			new Color32(0, 231, 204, 255),
			new Color32(212, 185, 51, 255),
			new Color32(255, 162, 153, 255),
			new Color32(127, 208, 102, 255),
			new Color32(170, 185, 204, 255),
			new Color32(85, 231, 0, 255),
			new Color32(42, 231, 153, 255),
			new Color32(85, 208, 255, 255),
			new Color32(0, 255, 51, 255),
			new Color32(255, 185, 0, 255),
			new Color32(212, 185, 153, 255),
			new Color32(255, 162, 255, 255),
			new Color32(170, 208, 51, 255),
			new Color32(127, 208, 204, 255),
			new Color32(85, 231, 102, 255),
			new Color32(42, 255, 0, 255),
			new Color32(42, 231, 255, 255),
			new Color32(0, 255, 153, 255),
			new Color32(212, 208, 0, 255),
			new Color32(255, 185, 102, 255),
			new Color32(212, 185, 255, 255),
			new Color32(127, 231, 51, 255),
			new Color32(170, 208, 153, 255),
			new Color32(85, 231, 204, 255),
			new Color32(42, 255, 102, 255),
			new Color32(0, 255, 255, 255),
			new Color32(212, 208, 102, 255),
			new Color32(255, 185, 204, 255),
			new Color32(170, 231, 0, 255),
			new Color32(127, 231, 153, 255),
			new Color32(170, 208, 255, 255),
			new Color32(204, 204, 204, 255),
			new Color32(85, 255, 51, 255),
			new Color32(42, 255, 204, 255),
			new Color32(255, 208, 51, 255),
			new Color32(212, 208, 204, 255),
			new Color32(170, 231, 102, 255),
			new Color32(127, 255, 0, 255),
			new Color32(127, 231, 255, 255),
			new Color32(85, 255, 153, 255),
			new Color32(212, 231, 51, 255),
			new Color32(255, 208, 153, 255),
			new Color32(170, 231, 204, 255),
			new Color32(127, 255, 102, 255),
			new Color32(85, 255, 255, 255),
			new Color32(255, 231, 0, 255),
			new Color32(212, 231, 153, 255),
			new Color32(255, 208, 255, 255),
			new Color32(170, 255, 51, 255),
			new Color32(127, 255, 204, 255),
			new Color32(255, 231, 102, 255),
			new Color32(212, 255, 0, 255),
			new Color32(212, 231, 255, 255),
			new Color32(170, 255, 153, 255),
			new Color32(255, 231, 204, 255),
			new Color32(212, 255, 102, 255),
			new Color32(170, 255, 255, 255),
			new Color32(255, 255, 51, 255),
			new Color32(212, 255, 204, 255),
			new Color32(255, 255, 153, 255),
			new Color32(255, 255, 255, 255),
		});

		public static Color32Palette Palette_8_bit {
			get {
				if (palette_8_bit == null) {
					var _palette_8_bit = new Color32[256]; // r:3 g:3 b:2
					var c = new Color32(0, 0, 0, 255);
					int i = 0;
					for (int kr = 0; kr < 8; ++kr) {
						c.r = (byte)Mathf.RoundToInt((kr/7f)*255f);
						for (int kg = 0; kg < 8; ++kg) {
							c.g = (byte)Mathf.RoundToInt((kg/7f)*255f);
							for (int kb = 0; kb < 4; ++kb) {
								c.b = (byte)Mathf.RoundToInt((kb/3f)*255f);
								_palette_8_bit[i] = c;
								++i;
							}
						}
					}
					palette_8_bit = new Color32Palette(_palette_8_bit);
				}
				return palette_8_bit;
			}
		}
	}
}
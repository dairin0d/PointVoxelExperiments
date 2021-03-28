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

namespace dairin0d.Rendering {
	public class OctantMap {
		public const int MinShift = 4;
		public const int MaxShift = 8;
		
		// Note: 17 seems to be the practical limit
		// without overflow (since nodes are centered)
		// Just to be sure, use slightly lower precision
		public const int PrecisionShift = 15;
		
		private int[] data;
		public int[] Data => data;
		
		public int SizeShift { get; private set; }
		public int Size => 1 << SizeShift;
		
		public void Resize(int sizeShift) {
			sizeShift = Mathf.Clamp(sizeShift, MinShift, MaxShift);
			if (sizeShift == SizeShift) return;
			
			SizeShift = sizeShift;
			data = new int[Size * Size];
		}
		
		// The input vectors are relative to a box of (1 << shift) size
		public void Bake(int Xx, int Xy, int Yx, int Yy, int Zx, int Zy, int Tx, int Ty, int shift) {
			if (data == null) return;
			
			if (shift > PrecisionShift) {
				int deltaShift = shift - PrecisionShift;
				Xx >>= deltaShift; Xy >>= deltaShift;
				Yx >>= deltaShift; Yy >>= deltaShift;
				Zx >>= deltaShift; Zy >>= deltaShift;
				Tx >>= deltaShift; Ty >>= deltaShift;
				shift = PrecisionShift;
			} else if (shift < PrecisionShift) {
				int deltaShift = PrecisionShift - shift;
				Xx <<= deltaShift; Xy <<= deltaShift;
				Yx <<= deltaShift; Yy <<= deltaShift;
				Zx <<= deltaShift; Zy <<= deltaShift;
				Tx <<= deltaShift; Ty <<= deltaShift;
				shift = PrecisionShift;
			}
			
			int sizeShift = SizeShift;
			
			int subpixel_shift = shift - sizeShift;
			int pixel_size = (1 << subpixel_shift), half_pixel = pixel_size >> 1;
			
			int extents_x = (Xx < 0 ? -Xx : Xx) + (Yx < 0 ? -Yx : Yx) + (Zx < 0 ? -Zx : Zx);
			int extents_y = (Xy < 0 ? -Xy : Xy) + (Yy < 0 ? -Yy : Yy) + (Zy < 0 ? -Zy : Zy);
			extents_x >>= 1;
			extents_y >>= 1;

			Tx -= half_pixel;
			Ty -= half_pixel;

			int dotXM = Mathf.Max(Mathf.Abs(Xx*(Yy+Zy) - Xy*(Yx+Zx)), Mathf.Abs(Xx*(Yy-Zy) - Xy*(Yx-Zx)));
			int dotYM = Mathf.Max(Mathf.Abs(Yx*(Xy+Zy) - Yy*(Xx+Zx)), Mathf.Abs(Yx*(Xy-Zy) - Yy*(Xx-Zx)));
			int dotZM = Mathf.Max(Mathf.Abs(Zx*(Xy+Yy) - Zy*(Xx+Yx)), Mathf.Abs(Zx*(Xy-Yy) - Zy*(Xx-Yx)));

			dotXM >>= 1;
			dotYM >>= 1;
			dotZM >>= 1;

			dotXM += (Mathf.Abs(Xx) + Mathf.Abs(Xy)) * half_pixel;
			dotYM += (Mathf.Abs(Yx) + Mathf.Abs(Yy)) * half_pixel;
			dotZM += (Mathf.Abs(Zx) + Mathf.Abs(Zy)) * half_pixel;

			int dotXdx = -Xy << subpixel_shift;
			int dotXdy = Xx << subpixel_shift;
			int dotYdx = -Yy << subpixel_shift;
			int dotYdy = Yx << subpixel_shift;
			int dotZdx = -Zy << subpixel_shift;
			int dotZdy = Zx << subpixel_shift;
			
			System.Array.Clear(data, 0, data.Length);
			
			int octant = 0;
			for (int subZ = -1; subZ <= 1; subZ += 2) {
				for (int subY = -1; subY <= 1; subY += 2) {
					for (int subX = -1; subX <= 1; subX += 2) {
						int dx = (Xx*subX + Yx*subY + Zx*subZ) >> 1;
						int dy = (Xy*subX + Yy*subY + Zy*subZ) >> 1;
						int cx = Tx + dx;
						int cy = Ty + dy;
						
						// We need at least 2-pixel margin to include the extended boundary
						int xmin = ((cx-extents_x) >> subpixel_shift) - 2;
						int ymin = ((cy-extents_y) >> subpixel_shift) - 2;
						int xmax = ((cx+extents_x) >> subpixel_shift) + 2;
						int ymax = ((cy+extents_y) >> subpixel_shift) + 2;
						
						int offset_x = (xmin << subpixel_shift) - cx;
						int offset_y = (ymin << subpixel_shift) - cy;
						
						int dotXr = Xx*offset_y - Xy*offset_x;
						int dotYr = Yx*offset_y - Yy*offset_x;
						int dotZr = Zx*offset_y - Zy*offset_x;
						
						int mask = 1 << octant;
						
						for (int iy = ymin; iy < ymax; ++iy) {
							int ixy0 = (iy << sizeShift) + xmin;
							int ixy1 = (iy << sizeShift) + xmax;
							int dotX = dotXr;
							int dotY = dotYr;
							int dotZ = dotZr;
							for (int ixy = ixy0; ixy < ixy1; ++ixy) {
								if (((dotX^(dotX>>31)) <= dotXM) & ((dotY^(dotY>>31)) <= dotYM) & ((dotZ^(dotZ>>31)) <= dotZM)) {
									data[ixy] |= mask;
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
		}
	}
}
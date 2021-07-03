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
	public enum ColorSpace {
		GammaRGB, LinearRGB, XYZ, CIE_Lab
	}

	public static class ColorSpaceConversion {
		public static Color Convert(this Color color, ColorSpace spaceFrom, ColorSpace spaceTo) {
			if (spaceFrom < spaceTo) {
				switch (spaceFrom) {
					case ColorSpace.GammaRGB: goto GammaRGB;
					case ColorSpace.LinearRGB: goto LinearRGB;
					case ColorSpace.XYZ: goto XYZ;
				}
				GammaRGB:;
				color = GammaRGB_to_LinearRGB(color);
				if (spaceTo == ColorSpace.LinearRGB) return color;
				LinearRGB:;
				color = LinearRGB_to_XYZ(color);
				if (spaceTo == ColorSpace.XYZ) return color;
				XYZ:;
				return XYZ_to_Lab(color);
			} else if (spaceFrom > spaceTo) {
				switch (spaceFrom) {
					case ColorSpace.CIE_Lab: goto Lab;
					case ColorSpace.XYZ: goto LinearRGB;
					case ColorSpace.LinearRGB: goto XYZ;
				}
				Lab:;
				color = Lab_to_XYZ(color);
				if (spaceTo == ColorSpace.XYZ) return color;
				XYZ:;
				color = XYZ_to_LinearRGB(color);
				if (spaceTo == ColorSpace.LinearRGB) return color;
				LinearRGB:;
				return LinearRGB_to_GammaRGB(color);
			}
			return color;
		}
		
		// http://www.easyrgb.com/en/math.php
		// https://github.com/antimatter15/rgb-lab/blob/master/color.js
		public static Color GammaRGB_to_LinearRGB(Color RGB) {
			RGB.r = (RGB.r > 0.04045f ? Mathf.Pow(((RGB.r + 0.055f) / 1.055f), 2.4f) : RGB.r / 12.92f);
			RGB.g = (RGB.g > 0.04045f ? Mathf.Pow(((RGB.g + 0.055f) / 1.055f), 2.4f) : RGB.g / 12.92f);
			RGB.b = (RGB.b > 0.04045f ? Mathf.Pow(((RGB.b + 0.055f) / 1.055f), 2.4f) : RGB.b / 12.92f);
			return RGB;
		}
		public static Color LinearRGB_to_GammaRGB(Color RGB) {
			RGB.r = (RGB.r > 0.0031308f ? 1.055f * Mathf.Pow(RGB.r, 1f/2.4f) - 0.055f : 12.92f * RGB.r);
			RGB.g = (RGB.g > 0.0031308f ? 1.055f * Mathf.Pow(RGB.g, 1f/2.4f) - 0.055f : 12.92f * RGB.g);
			RGB.b = (RGB.b > 0.0031308f ? 1.055f * Mathf.Pow(RGB.b, 1f/2.4f) - 0.055f : 12.92f * RGB.b);
			return RGB;
		}
		
		public static Color LinearRGB_to_XYZ(Color RGB) {
			float R = RGB.r * 100f;
			float G = RGB.g * 100f;
			float B = RGB.b * 100f;
			float X = R * 0.4124f + G * 0.3576f + B * 0.1805f;
			float Y = R * 0.2126f + G * 0.7152f + B * 0.0722f;
			float Z = R * 0.0193f + G * 0.1192f + B * 0.9505f;
			return new Color(X, Y, Z, RGB.a);
		}
		public static Color XYZ_to_LinearRGB(Color XYZ) {
			float X = XYZ.r / 100f;
			float Y = XYZ.g / 100f;
			float Z = XYZ.b / 100f;
			float R = X *  3.2406f + Y * -1.5372f + Z * -0.4986f;
			float G = X * -0.9689f + Y *  1.8758f + Z *  0.0415f;
			float B = X *  0.0557f + Y * -0.2040f + Z *  1.0570f;
			return new Color(R, G, B, XYZ.a);
		}
		
		public readonly static Vector3 Reference_D65_10 = new Vector3(94.811f, 100.000f, 107.304f); // Daylight, sRGB, Adobe-RGB, 10° (CIE 1964)
		public static Color XYZ_to_Lab(Color XYZ) {
			return XYZ_to_Lab(XYZ, Reference_D65_10);
		}
		public static Color Lab_to_XYZ(Color Lab) {
			return Lab_to_XYZ(Lab, Reference_D65_10);
		}
		public static Color XYZ_to_Lab(Color XYZ, Vector3 Reference) {
			float X = XYZ.r / Reference.x;
			float Y = XYZ.g / Reference.y;
			float Z = XYZ.b / Reference.z;
			X = (X > 0.008856f ? Mathf.Pow(X, 1f/3f) : (7.787f * X) + (16f/116f));
			Y = (Y > 0.008856f ? Mathf.Pow(Y, 1f/3f) : (7.787f * Y) + (16f/116f));
			Z = (Z > 0.008856f ? Mathf.Pow(Z, 1f/3f) : (7.787f * Z) + (16f/116f));
			float L = (116f * Y) - 16f;
			float a = 500f * (X - Y);
			float b = 200f * (Y - Z);
			return new Color(L, a, b, XYZ.a);
		}
		public static Color Lab_to_XYZ(Color Lab, Vector3 Reference) {
			float Y = (Lab.r + 16f) / 116f;
			float X = Lab.g / 500f + Y;
			float Z = Y - Lab.b / 200f;
			float Y3 = Mathf.Pow(Y, 3f);
			float X3 = Mathf.Pow(X, 3f);
			float Z3 = Mathf.Pow(Z, 3f);
			Y = (Y3 > 0.008856f ? Y3 : (Y - (16f/116f)) / 7.787f);
			X = (X3 > 0.008856f ? X3 : (X - (16f/116f)) / 7.787f);
			Z = (Z3 > 0.008856f ? Z3 : (Z - (16f/116f)) / 7.787f);
			X = X * Reference.x;
			Y = Y * Reference.y;
			Z = Z * Reference.z;
			return new Color(X, Y, Z, Lab.a);
		}
	}
}
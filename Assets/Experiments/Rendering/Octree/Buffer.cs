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

namespace dairin0d.Rendering.Octree {
    class Buffer {
        public struct DataItem {
            public int depth;
            public Color32 color;
        }

        public DataItem[] Data;

        public Texture2D Texture;
        private Color32[] colors;

        public int Width;
        public int Height;
        public int TileCountX;
        public int TileCountY;
        public int TileShift;

        public int TileSize => 1 << TileShift;
        public int TileCount => TileCountX * TileCountY;

        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        public float FrameTime {get; private set;}

        public void RenderStart(Color32 background) {
            stopwatch.Restart();
            Clear(background);
        }
        
        public void RenderEnd(int depth_shift = -1) {
            Blit(depth_shift);
            stopwatch.Stop();
            FrameTime = stopwatch.ElapsedMilliseconds / 1000f;
            UpdateTexture();
        }

        public void Resize(int w, int h, int renderSize, int tilePow) {
            if (renderSize > 0) {
                int maxTexSize = SystemInfo.maxTextureSize;
                float scale = Mathf.Min(renderSize, maxTexSize) / (float)Mathf.Max(w, h);
                w = Mathf.Max(Mathf.RoundToInt(w * scale), 1);
                h = Mathf.Max(Mathf.RoundToInt(h * scale), 1);
            }

            tilePow = Mathf.Clamp(tilePow, 0, 12);
            int tileSize = (tilePow <= 0 ? 0 : 1 << (tilePow - 1));

            if (Texture && (w == Texture.width) && (h == Texture.height)) {
                if (tileSize != TileSize) Resize(w, h, tileSize);
                return;
            }

            if (Texture) UnityEngine.Object.Destroy(Texture);

            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Texture.filterMode = FilterMode.Point;

            colors = new Color32[w * h];

            Resize(w, h, tileSize);
        }
        
        private void Resize(int width, int height, int tileSize = 0) {
            Width = width;
            Height = height;

            if (tileSize <= 0) {
                TileShift = Mathf.Max(NextPow2(width), NextPow2(height));
            } else {
                TileShift = NextPow2(tileSize);
            }

            tileSize = 1 << TileShift;

            TileCountX = (Width + tileSize - 1) >> TileShift;
            TileCountY = (Height + tileSize - 1) >> TileShift;

            Data = new DataItem[tileSize * tileSize * TileCountX * TileCountY];

            int NextPow2(int v) {
                return Mathf.CeilToInt(Mathf.Log(v) / Mathf.Log(2));
            }
        }
        
        public void UpdateTexture() {
            Texture.SetPixels32(0, 0, Texture.width, Texture.height, colors, 0);
            Texture.Apply(false);
        }
        
        public unsafe void Blit(int depth_shift = -1) {
            int w = Texture.width;
            int h = Texture.height;
            int shift = TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = TileCountX;
            int tny = TileCountY;

            bool show_depth = (depth_shift >= 0);

            fixed (DataItem* data_ptr = Data)
            fixed (Color32* colors_ptr = colors) {
                for (int ty = 0; ty < tny; ++ty) {
                    int iy = ty << shift;
                    var colors_ty = colors_ptr + iy * w;
                    int th = h - iy; if (th > tile_size) th = tile_size;
                    for (int tx = 0; tx < tnx; ++tx) {
                        int ix = tx << shift;
                        var colors_tx = colors_ty + ix;
                        int tw = w - ix; if (tw > tile_size) tw = tile_size;

                        var tile = data_ptr + ((tx + ty * tnx) << shift2);
                        var tile_end_y = tile + (th << shift);
                        var colors_y = colors_tx;
                        for (var tile_y = tile; tile_y != tile_end_y; tile_y += tile_size, colors_y += w) {
                            var tile_end_x = tile_y + tw;
                            var colors_x = colors_y;
                            for (var tile_x = tile_y; tile_x != tile_end_x; ++tile_x, ++colors_x) {
                                if (show_depth) {
                                    byte d = (byte)(tile_x->depth >> depth_shift);
                                    colors_x->r = colors_x->g = colors_x->b = d;
                                    colors_x->a = tile_x->color.a;
                                } else {
                                    *colors_x = tile_x->color;
                                }
                            }
                        }
                    }
                }
            }
        }

        public unsafe void Clear(Color32 background) {
            var clear_data = default(DataItem);
            clear_data.depth = int.MaxValue;
            clear_data.color = background;

            int w = Width;
            int h = Height;
            int shift = TileShift;
            int shift2 = shift * 2;
            int tile_size = 1 << shift;
            int tile_area = 1 << shift2;
            int tnx = TileCountX;
            int tny = TileCountY;

            fixed (DataItem* data_ptr = Data) {
                for (int ty = 0; ty < tny; ++ty) {
                    int iy = ty << shift;
                    int th = h - iy; if (th > tile_size) th = tile_size;
                    for (int tx = 0; tx < tnx; ++tx) {
                        int ix = tx << shift;
                        int tw = w - ix; if (tw > tile_size) tw = tile_size;

                        var tile = data_ptr + ((tx + ty * tnx) << shift2);
                        var tile_end_y = tile + (th << shift);
                        for (var tile_y = tile; tile_y != tile_end_y; tile_y += tile_size) {
                            var tile_end_x = tile_y + tw;
                            for (var tile_x = tile_y; tile_x != tile_end_x; ++tile_x) {
                                *tile_x = clear_data;
                            }
                        }
                    }
                }
            }
        }
    }
}
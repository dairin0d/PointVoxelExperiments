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

namespace dairin0d.Rendering.PointStrip {
    static class Directions {
        public const int Count = 3 * 3 * 3;

        private static Vector3i[] directions;

        private static void Initialize() {
            if (directions != null) return;

            directions = new Vector3i[Count];

            int i = 0;

            for (int z = -1; z <= 1; z += 2) {
                for (int y = -1; y <= 1; y += 2) {
                    for (int x = -1; x <= 1; x += 2) {
                        directions[i] = new Vector3i(x, y, z);
                        i++;
                    }
                }
            }

            for (int x = -1, y = 0, z = 0; x <= 1; x += 2) {
                directions[i] = new Vector3i(x, y, z);
                i++;
            }

            for (int x = 0, y = -1, z = 0; y <= 1; y += 2) {
                directions[i] = new Vector3i(x, y, z);
                i++;
            }

            for (int x = 0, y = 0, z = -1; z <= 1; z += 2) {
                directions[i] = new Vector3i(x, y, z);
                i++;
            }

            for (int z = -1; z <= 1; z++) {
                for (int y = -1; y <= 1; y++) {
                    for (int x = -1; x <= 1; x++) {
                        if (Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z) != 2) continue;
                        directions[i] = new Vector3i(x, y, z);
                        i++;
                    }
                }
            }
        }

        public static Vector3i[] Get() {
            if (directions == null) Initialize();
            return directions;
        }

        public static Vector3i Get(int i) {
            if (directions == null) Initialize();
            return directions[i];
        }

        public static int Index(int dx, int dy, int dz) {
            if (directions == null) Initialize();
            for (int i = 0; i < directions.Length; i++) {
                var dir = directions[i];
                if ((dir.x == dx) & (dir.y == dy) & (dir.z == dz)) return i;
            }
            return -1;
        }
    }
}

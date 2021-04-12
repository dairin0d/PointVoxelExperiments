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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace dairin0d.Tests {
    public class GridDuplicator : MonoBehaviour {
        public Vector3 Steps = Vector3.one;
        public Vector3Int Counts = Vector3Int.one;
        
        void Start() {
            var parent = transform.parent;
            var position = transform.localPosition;
            var rotation = transform.localRotation;
            
            var center = new Vector3((Counts.x-1) * Steps.x, (Counts.y-1) * Steps.y, (Counts.z-1) * Steps.z) * 0.5f;
            for (int z = 0; z < Counts.z; z++) {
                for (int y = 0; y < Counts.y; y++) {
                    for (int x = 0; x < Counts.x; x++) {
                        var offset = new Vector3(x * Steps.x, y * Steps.y, z * Steps.z) - center;
                        var instance = Instantiate(gameObject, position + offset, rotation, parent);
                        var duplicator = instance.GetComponent<GridDuplicator>();
                        Destroy(duplicator);
                    }
                }
            }
            
            gameObject.SetActive(false);
        }
    }
}
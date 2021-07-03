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

using System.Collections.Generic;
using UnityEngine;

namespace dairin0d.Data.Voxels {
    public class HashCloud<T> : IVoxelCloud<T> {
        Dictionary<Vector3Int, T> hashtable;
        T default_value;

        public HashCloud(int capacity=0, T default_value=default(T)) {
            hashtable = new Dictionary<Vector3Int, T>(capacity);
            this.default_value = default_value;
        }

        public int Count {
            get { return hashtable.Count; }
        }

        public IEnumerator<KeyValuePair<Vector3Int,T>> GetEnumerator() {
            return hashtable.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return hashtable.GetEnumerator();
        }

        public T this[Vector3Int pos] {
            get { T data; return (hashtable.TryGetValue(pos, out data) ? data : default_value); }
            set { hashtable[pos] = value; }
        }

        public bool Query(Vector3Int pos) {
            return hashtable.ContainsKey(pos);
        }
        public bool Query(Vector3Int pos, out T data) {
            return hashtable.TryGetValue(pos, out data);
        }

        public void Erase(Vector3Int pos) {
            hashtable.Remove(pos);
        }
        public void Erase() {
            hashtable.Clear();
        }
    }
}
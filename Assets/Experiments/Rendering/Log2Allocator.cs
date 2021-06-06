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
using System.Runtime.InteropServices;

namespace dairin0d.Rendering {
    /// <remarks>
    /// A utility for allocating array ranges proportional to powers of two.
    /// The core idea is similar to the buddy allocation algorithm,
    /// but without explicit split/merge operations or free block lists.
    /// https://en.wikipedia.org/wiki/Buddy_memory_allocation
    /// https://github.com/evanw/buddy-malloc/blob/master/buddy-malloc.c
    /// https://www.kuniga.me/blog/2020/07/31/buddy-memory-allocation.html
    /// https://www.geeksforgeeks.org/buddy-memory-allocation-program-set-2-deallocation/
    /// </remarks>
    public class Log2Allocator<T> {
        // Depth bits are arranged in reverse order (highest bit is min block size,
        // lowest bit is max block size), so that numerical comparison of two masks
        // would return which of the masks has a higher bit set.
        // 31 bits for each level - whether it's fully occupied (no free nodes of that size)
        // 32 bit for whether the current node is allocated
        private const int DepthBits = 31;
        private const int DepthStartBit = 1 << (DepthBits - 1);
        private const int DepthStartMask = int.MaxValue;
        private const int AllocatedBit = int.MinValue;
        
        private int elementSize;
        private int blockSize;
        private int depthMax;
        private int depthLimit;
        private int arraySize;
        private int[] infos;
        private T[] array;
        
        public int Depth => depthMax;
        public int Count => depthMax < 0 ? 0 : blockSize * (1 << depthMax);
        
        public int ElementSize => elementSize;
        public int MemorySize => (Count * elementSize) + (infos != null ? infos.Length * sizeof(int) : 0);
        
        public T[] Array => array;
        
        /// <param name="blockSize">Minimal allocatable size (measured in array elements).</param>
        /// <param name="initialDepth">Initial log2(block count) to fit in the array.</param>
        /// <param name="memoryLimit">Size limit of the backing array (measured in bytes).</param>
        public Log2Allocator(int blockSize, int initialDepth = -1, int memoryLimit = int.MaxValue) {
            if (blockSize <= 0) throw new System.ArgumentOutOfRangeException("blockSize", "blockSize must be positive");
            
            elementSize = Marshal.SizeOf<T>();
            int blockSizeInBytes = blockSize * elementSize;
            
            if (memoryLimit >= 0) {
                if (blockSizeInBytes > memoryLimit) throw new System.ArgumentOutOfRangeException("blockSize", "block is larger than memoryLimit");
            } else {
                memoryLimit = (initialDepth >= 0) ? blockSizeInBytes * (1 << initialDepth) : int.MaxValue;
            }
            
            this.blockSize = blockSize;
            depthMax = -1;
            depthLimit = (int) System.Math.Log(memoryLimit / (double) blockSizeInBytes, 2.0);
            
            if (initialDepth > depthLimit) throw new System.ArgumentOutOfRangeException("initialDepth", "initial array size is larger than memoryLimit");
            
            ResizeToDepth(initialDepth);
        }
        
        /// <param name="size">The size of the requested allocation (measured in array elements).</param>
        /// <returns>Returns the start address of the allocated block, or -1 if allocation is not possible.</returns>
        public int Allocate(int size) {
            if (size <= 0) throw new System.ArgumentOutOfRangeException("size", "size must be positive");
            
            int depth = 0, infoIndex = 0;
            if (depthMax < 0) {
                if (!Resize(size)) return -1;
            } else if (((infos[0] & (DepthStartBit >> depthMax)) == 0) & (arraySize < size)) {
                if (!Resize(size)) return -1;
            } else {
                int blockLevel = MinBlockLevel(size);
                if (blockLevel < 0) return -1;
                
                depth = depthMax - blockLevel;
                infoIndex = FindFreeBlock(depth);
                
                if (infoIndex < 0) {
                    if (!Resize(arraySize + size)) return -1;
                    depth = depthMax - blockLevel;
                    infoIndex = FindFreeBlock(depth);
                }
            }
            
            // We need to set the allocation bit and all the 0..depth range bits. Since they
            // are adjacent in our layout, we can just use arithmetic shift to fill them all.
            infos[infoIndex] = AllocatedBit >> (depthMax - depth + 1);
            
            PropagateInfo(depth, infoIndex);
            
            int depthStart = (1 << depth) - 1;
            int blockStart = (infoIndex - depthStart) << (depthMax - depth);
            return blockStart * blockSize;
        }
        
        public bool Deallocate(int address) {
            if (FindAllocatedBlock(address, out int depth, out int infoIndex) <= 0) return false;
            
            infos[infoIndex] = (AllocatedBit >> (depthMax - depth)) & DepthStartMask;
            
            PropagateInfo(depth, infoIndex);
            
            return true;
        }
        
        public bool IsAllocated(int address, out int rangeStart, out int rangeSize) {
            if (FindAllocatedBlock(address, out int depth, out int infoIndex) <= 0) {
                rangeStart = -1;
                rangeSize = 0;
                return false;
            }
            
            int depthStart = (1 << depth) - 1;
            int blockStart = (infoIndex - depthStart) << (depthMax - depth);
            rangeStart = blockStart * blockSize;
            rangeSize = arraySize >> depth;
            return true;
        }
        
        public IEnumerable<(int Start, int Size, bool Allocated)> EnumerateRanges() {
            int address = 0;
            
            while (address < arraySize) {
                int result = FindAllocatedBlock(address, out int depth, out int infoIndex);
                if (result < 0) yield break;
                
                int depthStart = (1 << depth) - 1;
                int blockStart = (infoIndex - depthStart) << (depthMax - depth);
                int rangeStart = blockStart * blockSize;
                int rangeSize = arraySize >> depth;
                yield return (rangeStart, rangeSize, (result > 0));
                
                address += rangeSize;
            }
        }
        
        #region For debug
        public IEnumerable<string> PrintInfos() {
            if (infos == null) yield break;
            
            for (int infoIndex = 0; infoIndex < infos.Length-1; infoIndex++) {
                yield return PrintBits(infos[infoIndex]);
            }
        }
        
        public void DoubleSize() {
            Resize(System.Math.Max(arraySize * 2, 1));
        }
        
        char[] b = new char[32];
        private string PrintBits(int n) {
            for (int i = 0, s = b.Length-1; i < b.Length; i++, s--) {
                b[i] = ((n >> s) & 1) != 0 ? '1' : '0';
            }
            return new string(b);
        }
        #endregion
        
        private void PropagateInfo(int depth, int infoIndex) {
            while (depth > 0) {
                infoIndex = (infoIndex - 1) >> 1; // parent
                
                int leftChild = (infoIndex << 1) + 1;
                int leftOccupied = infos[leftChild] & DepthStartMask;
                bool isLeftFree = ((leftOccupied & (DepthStartBit >> (depthMax - depth))) == 0);
                
                int rightChild = (infoIndex << 1) + 2;
                int rightOccupied = infos[rightChild] & DepthStartMask;
                bool isRightFree = ((rightOccupied & (DepthStartBit >> (depthMax - depth))) == 0);
                
                depth--;
                
                if (isLeftFree & isRightFree) {
                    infos[infoIndex] = (AllocatedBit >> (depthMax - depth)) & DepthStartMask;
                } else {
                    infos[infoIndex] = (leftOccupied & rightOccupied) | (DepthStartBit >> (depthMax - depth));
                }
            }
        }
        
        private int FindAllocatedBlock(int address, out int depth, out int infoIndex) {
            if ((address >= 0) & (address < arraySize)) {
                int addressMin = 0;
                int addressMax = arraySize;
                
                depth = 0;
                infoIndex = 0;
                
                for (; depth <= depthMax; depth++) {
                    int info = infos[infoIndex];
                    if ((info & (DepthStartBit >> (depthMax - depth))) == 0) return 0;
                    if ((info & AllocatedBit) != 0) return 1;
                    
                    int addressMid = (addressMin + addressMax) >> 1;
                    if (address < addressMid) {
                        addressMax = addressMid;
                        infoIndex = (infoIndex << 1) + 1;
                    } else {
                        addressMin = addressMid;
                        infoIndex = (infoIndex << 1) + 2;
                    }
                }
            }
            
            depth = -1;
            infoIndex = -1;
            return -1;
        }
        
        private int FindFreeBlock(int targetDepth) {
            int infoIndex = 0;
            int info = infos[infoIndex];
            
            int rangeMask = (DepthStartMask >> (depthMax + 1)) ^ (DepthStartMask >> (depthMax - targetDepth));
            
            // If all range bits are set, there are no free blocks of target size or larger
            if ((info & rangeMask) == rangeMask) return -1;
            
            for (int depth = 0; depth != targetDepth; depth++) {
                rangeMask &= rangeMask << 1; // remove the current depth from range mask
                
                int leftChild = (infoIndex << 1) + 1;
                int leftInfo = infos[leftChild];
                int leftFree = (~leftInfo) & rangeMask;
                
                int rightChild = (infoIndex << 1) + 2;
                int rightInfo = infos[rightChild];
                int rightFree = (~rightInfo) & rangeMask;
                
                // Find which of the children has a tighter-fitting free space
                infoIndex = (leftFree >= rightFree ? leftChild : rightChild);
            }
            
            return infoIndex;
        }
        
        /// <summary>Returns log2(minimal blocks) necessary to contain this size</summary>
        private int MinBlockLevel(int size) {
            if (size <= blockSize) return 0;
            int count = (size + blockSize - 1) / blockSize;
            if (count <= 0) return -1;
            return (int) System.Math.Ceiling(System.Math.Log(count, 2.0));
        }
        
        private bool Resize(int size) {
            return ResizeToDepth(MinBlockLevel(size));
        }
        
        private bool ResizeToDepth(int newDepthMax) {
            if ((newDepthMax < 0) | (newDepthMax > depthLimit)) return false;
            if (newDepthMax <= depthMax) return true;
            
            var newInfos = new int[1 << (newDepthMax+1)];
            var newArray = new T[blockSize * (1 << newDepthMax)];
            
            int deltaDepth = newDepthMax - depthMax;
            
            for (int newDepth = 0, oldDepth = -deltaDepth; newDepth <= newDepthMax; newDepth++, oldDepth++) {
                int newLength = 1 << newDepth;
                int newStart = newLength - 1;
                
                if (oldDepth >= 0) {
                    int oldLength = 1 << oldDepth;
                    int oldStart = oldLength - 1;
                    System.Array.Copy(infos, oldStart, newInfos, newStart, oldLength);
                    newStart += oldLength;
                    newLength -= oldLength;
                }
                
                for (int index = newStart, end = index + newLength; index < end; index++) {
                    newInfos[index] = (AllocatedBit >> (newDepthMax - newDepth)) & DepthStartMask;
                }
            }
            
            if (array != null) System.Array.Copy(array, newArray, array.Length);
            
            infos = newInfos;
            array = newArray;
            arraySize = array.Length;
            depthMax = newDepthMax;
            
            if (deltaDepth <= depthMax) PropagateInfo(deltaDepth, (1 << deltaDepth) - 1);
            
            return true;
        }
    }
}

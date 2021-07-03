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
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine;

using dairin0d.Data.Points;
using dairin0d.Data.Voxels;

namespace dairin0d.Rendering {
    public class Model3D {
        public string Name;
        public Bounds Bounds; // for camera frustum culling
        public ModelBone[] Bones;
        public ModelCage Cage;
        public ModelAttributeInfo[] AttributeInfos; // color, normal, UV coords...
        public ModelAttributeData[] AttributeDatas;
        public ModelPart[] Parts;
        public ModelGeometry[] Geometries;
    }

    public class ModelBone {
        public string Name;
        public int Parent; // index in Bones
        public Matrix4x4 Bindpose;
        public Matrix4x4 BindposeInverted;
    }

    public struct ModelWeight {
        public int Index;
        public float Weight;
    }

    public class ModelCage {
        public Vector3[] Positions;
        public byte[] WeightCounts;
        public ModelWeight[] Weights; // bindpose weights
    }

    public class ModelPoints {
        public byte[] WeightCounts;
        public ModelWeight[] Weights; // cage weights
        public int[] Attributes; // indices in AttributeInfos
    }

    public class ModelPart {
        // Note: if vertices are not specified, then this part is not bounded by a cage volume
        // Otherwise, 4 (for tetrahedron) or 8 (for cube) vertices are typically expected
        public int[] Vertices; // cage vertex indices (corresponding to i-th cube corner)
        public int[] Geometries; // indices in Geometries (can be multiple if there are animation frames)
    }

    public class ModelAttributeInfo {
        public string Name;
        public int Dimension;
        public int ElementSize;
        public int TotalSize;
        // Data type? (int/float, signed/unsigned, custom struct...)
    }

    public class ModelAttributeData {
        public int InfoIndex;
        public byte[] Data;
    }

    public abstract class ModelGeometry {
    }

    public enum ModelPointType {
        Rect, Circle, Cube
    }

    public interface IModelGeometrySplattable {
        Vector3 PointScale {get;}
        ModelPointType PointType {get;}
    }

    public abstract class ModelGeometryMesh : ModelGeometry {
        public ModelPoints Points;
    }

    public abstract class ModelGeometryVolume : ModelGeometry {
        public int[] Palettes; // indices in AttributeDatas
    }

    public abstract class ModelGeometryOctree : ModelGeometryVolume, IModelGeometrySplattable {
        public int MaxLevel;

        public Vector3 PointScale {get; private set;}
        public ModelPointType PointType {get; private set;}
    }

    public struct Color24 {
        public byte R, G, B; // Alpha isn't used in splatting anyway
        
        public static explicit operator Color32(Color24 color) {
            return new Color32 {r = color.R, g = color.G, b = color.B, a = 255};
        }
        
        public static explicit operator Color24(Color32 color) {
            return new Color24 {R = color.r, G = color.g, B = color.b};
        }
    }

    public struct OctreeNode {
        public int Address; // index of the first child node in the global node array
        public byte Mask; // octant mask
        public Color24 BaseColor;
    }

    public class ChunkedOctree : ModelGeometry {
        public struct ChunkInfo {
            public int ChunkStart; // index of the first node of this chunk in the global node array
            public int AccessTime; // timestamp of the last access to this chunk
        }
        
        internal struct PackInfo {
            public int StartOffset; // address of the first child node
            public int PackedStart; // starting position of this chunk in the packed data (in bytes)
            public int PackedSize; // size of this chunk in the packed data (in bytes)
            public int NodeCount; // number of nodes contained in this chunk
        }
        
        private struct ChunkUser {
            public ChunkedOctree Octree;
            public int Index;
            public int ChunkStart;
            public int ChunkCount;
        }
        
        // ======== Constants ======== //
        
        private const int MinChunkShift = 10;
        private const int MaxChunkShift = 16;
        
        private const int DefaultChunkShift = 13;
        
        private const int InitialAllocatorDepth = 12;
        
        // ======== Static fields & properties ======== //
        
        private static Log2Allocator<OctreeNode> allocator;
        
        private static byte[] decompressedBuffer;
        
        public static OctreeNode[] DataArray => allocator?.Array;
        
        // ======== Instance fields & properties ======== //
        
        private bool isPacked;
        private bool isCompressed;
        private int chunkShift;
        
        private int chunksCount;
        private ChunkInfo[] chunkInfos;
        private OctreeNode root;
        
        private PackInfo[] packInfos;
        private byte[] packedData;
        
        private int firstErasedNode;
        
        public bool IsPacked => isPacked;
        public int ChunkShift => chunkShift;
        public int ChunksCount => chunksCount;
        public ChunkInfo[] ChunkInfos => chunkInfos;
        public OctreeNode Root => root;
        
        // ======== Methods ======== //
        
        private ChunkedOctree() {
            firstErasedNode = -1;
            
            if (allocator == null) allocator = new Log2Allocator<OctreeNode>(1 << MinChunkShift, InitialAllocatorDepth);
        }
        
        public static ChunkedOctree FromRawOctree(int rootNode, Color32 rootColor, int[] nodes, Color32[] colors) {
            var octree = new ChunkedOctree();
            
            octree.isPacked = false;
            octree.isCompressed = false;
            octree.chunkShift = DefaultChunkShift;
            
            octree.root = new OctreeNode {
                Address = ((rootNode >> 8) & 0xFFFFFF) << 3,
                Mask = (byte)(rootNode & 0xFF),
                BaseColor = new Color24 {R = rootColor.r, G = rootColor.g, B = rootColor.b}
            };
            
            int chunkSize = 1 << octree.chunkShift;
            octree.chunksCount = (nodes.Length + chunkSize - 1) >> octree.chunkShift; // round up
            octree.chunkInfos = new ChunkInfo[octree.chunksCount];
            
            for (int chunkIndex = 0; chunkIndex < octree.chunksCount; chunkIndex++) {
                int srcOffset = chunkIndex << octree.chunkShift;
                int count = Mathf.Min(srcOffset + chunkSize, nodes.Length) - srcOffset;
                int dstOffset = allocator.Allocate(count);
                octree.chunkInfos[chunkIndex] = new ChunkInfo { ChunkStart = dstOffset };
                
                var array = allocator.Array;
                for (int i = 0; i < count; i++) {
                    var node = nodes[srcOffset+i];
                    var color = colors[srcOffset+i];
                    array[dstOffset+i] = new OctreeNode {
                        Address = ((node >> 8) & 0xFFFFFF) << 3,
                        Mask = (byte)(node & 0xFF),
                        BaseColor = new Color24 {R = color.r, G = color.g, B = color.b}
                    };
                }
            }
            
            Debug.Log($"Nodes: {nodes.Length}, Chunks: {octree.chunksCount}, Allocator: depth={allocator.Depth}, count={allocator.Count}, size={allocator.MemorySize}");
            
            return octree;
        }
        
        public static ChunkedOctree PackRawOctree(int rootNode, Color32 rootColor, int[] nodes, Color32[] colors,
            int chunkShift = DefaultChunkShift, bool compress = false, bool preload = false)
        {
            chunkShift = Mathf.Clamp(chunkShift, MinChunkShift, MaxChunkShift);
            
            var octree = new ChunkedOctree();
            
            octree.isPacked = true;
            octree.isCompressed = compress;
            octree.chunkShift = chunkShift;
            
            var packer = new OctreePacker();
            packer.Process(nodes, colors, rootNode, rootColor, chunkShift, compress, out var packInfos, out var packedData);
            
            octree.packInfos = packInfos;
            octree.packedData = packedData;
            
            octree.chunksCount = packInfos.Length;
            octree.chunkInfos = new ChunkInfo[octree.chunksCount];
            
            octree.root = new OctreeNode {
                Address = packInfos[0].StartOffset,
                Mask = (byte)(rootNode & 0xFF),
                BaseColor = new Color24 {R = rootColor.r, G = rootColor.g, B = rootColor.b}
            };
            
            if (preload) {
                for (int chunkIndex = 0; chunkIndex < octree.chunksCount; chunkIndex++) {
                    octree.Unpack(chunkIndex);
                }
            } else {
                for (int chunkIndex = 0; chunkIndex < octree.chunksCount; chunkIndex++) {
                    octree.chunkInfos[chunkIndex].ChunkStart = -1;
                }
            }
            
            Debug.Log($"Packed: {packedData.Length}, Chunks: {octree.chunksCount}, Allocator: depth={allocator.Depth}, count={allocator.Count}, size={allocator.MemorySize}");
            
            return octree;
        }
        
        public unsafe void Unpack(int chunkIndex) {
            GCHandle dataHandle = default;
            OctreeNode* dataPointer = null;
            Unpack(chunkIndex, ref dataHandle, ref dataPointer);
        }
        
        public unsafe void Unpack(int chunkIndex, ref GCHandle dataHandle, ref OctreeNode* dataPointer) {
            if (chunkInfos[chunkIndex].ChunkStart >= 0) return;
            
            var packInfo = packInfos[chunkIndex];
            int nodeCount = packInfo.NodeCount;
            Decompress(ref packInfo, out int packedStart, out int packedSize, out var packedBytes);
            
            int chunkStart = allocator.Allocate(nodeCount);
            var array = allocator.Array;
            
            if ((dataHandle != default) && (dataHandle.Target != array)) {
                dataHandle.Free();
                dataHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
                dataPointer = (OctreeNode*) dataHandle.AddrOfPinnedObject();
            }
            
            chunkInfos[chunkIndex] = new ChunkInfo {ChunkStart = chunkStart, AccessTime = Time.frameCount};
            
            int packedIndex = packedStart;
            
            var bitCounts = OctantOrder.Counts;
            int chunkSize = 1 << chunkShift;
            int childOffset = packInfo.StartOffset;
            int nextChunk = (childOffset + chunkSize) & ~(chunkSize - 1);
            
            Debug.Log($"Chunk {chunkIndex}: start={packInfo.StartOffset}, Allocator: depth={allocator.Depth}, count={allocator.Count}, size={allocator.MemorySize}");
            
            for (int index = 0; index < nodeCount; index++, packedIndex++) {
                ref var node = ref array[chunkStart+index];
                
                node.Mask = packedBytes[packedIndex];
                
                int childCount = bitCounts[node.Mask];
                
                if (childOffset + childCount > nextChunk) {
                    childOffset = nextChunk;
                    nextChunk += chunkSize;
                }
                
                node.Address = childOffset;
                
                childOffset += childCount;
            }
            
            for (int index = 0; index < nodeCount; index++, packedIndex += 3) {
                ref var color = ref array[chunkStart+index].BaseColor;
                color.R = packedBytes[packedIndex + 0];
                color.G = packedBytes[packedIndex + 1];
                color.B = packedBytes[packedIndex + 2];
            }
        }
        
        private void Decompress(ref PackInfo packInfo, out int packedStart, out int packedSize, out byte[] packedBytes) {
            if (!isCompressed) {
                packedStart = packInfo.PackedStart;
                packedSize = packInfo.PackedSize;
                packedBytes = packedData;
                return;
            }
            
            int elementSize = 1 + 3; // hard-code for now
            int bufferSize = Mathf.Max(Mathf.NextPowerOfTwo(packInfo.NodeCount), 1 << MaxChunkShift) * elementSize;
            
            if ((decompressedBuffer == null) || (decompressedBuffer.Length < bufferSize)) {
                decompressedBuffer = new byte[bufferSize];
            }
            
            packedBytes = decompressedBuffer;
            packedStart = 0;
            packedSize = packInfo.NodeCount * elementSize;
            
            LZ4.LZ4Codec.Decode32(packedData, packInfo.PackedStart, packInfo.PackedSize,
                packedBytes, packedStart, packedSize, true);
        }
    }

    internal class OctreePacker {
        private struct NodeData {
            public byte Mask;
            public Color24 Color;
        }
        
        private class LinkedNode : OctreeNode<NodeData> {
            public LinkedNode prev;
            public LinkedNode next;
            public int id;
            public int offset;
        }
        
        private class BlockInfo {
            public LinkedNode start;
            public int offset;
            public int count;
            public int startOffset;
            public byte[] bytes;
        }
        
        private List<LinkedNode> levelHeads = new List<LinkedNode>();
        private List<LinkedNode> levelTails = new List<LinkedNode>();
        private int count;
        private int idCounter;
        
        public void Process(int[] nodes, Color32[] colors, int node, Color32 color,
            int blockSizeShift, bool compress,
            out ChunkedOctree.PackInfo[] packInfos, out byte[] packedData)
        {
            var root = ToLinkedOctree(nodes, colors, node, color);
            Process(root, blockSizeShift, compress, out packInfos, out packedData);
        }
        
        private void Process(LinkedNode root, int blockSizeShift, bool compress,
            out ChunkedOctree.PackInfo[] packInfos, out byte[] packedData)
        {
            levelHeads.Clear();
            levelTails.Clear();
            count = 0;
            idCounter = 1;
            LinkNodes(root, idCounter);

            LinkLevels();

            var blockInfos = CalculateBlocks(root, blockSizeShift);

            CalculateStartOffsets(blockInfos);

            Pack(blockInfos, blockSizeShift, compress, out packInfos, out packedData);
        }
        
        private void Pack(LinkedList<BlockInfo> blockInfos, int blockSizeShift, bool compress,
            out ChunkedOctree.PackInfo[] packInfos, out byte[] packedData)
        {
            packInfos = new ChunkedOctree.PackInfo[blockInfos.Count];
            
            int sizeOfData = Marshal.SizeOf<NodeData>();
            int sizeOfMask = 1;
            int sizeOfColor = Marshal.SizeOf<Color24>();
            
            int sumUncompressed = 0;
            int sumCompressed = 0;
            
            int blockIndex = 0;
            foreach (var blockInfo in blockInfos) {
                int maskOffset = 0;
                int colorOffset = maskOffset + sizeOfMask * blockInfo.count;
                
                var uncompressedBytes = new byte[blockInfo.count * sizeOfData];
                
                var node = blockInfo.start;
                for (int index = 0; index < blockInfo.count; index++, node = node.next) {
                    var data = node.data;
                    uncompressedBytes[maskOffset + index] = data.Mask;
                    int colorIndex = colorOffset + index * sizeOfColor;
                    uncompressedBytes[colorIndex + 0] = data.Color.R;
                    uncompressedBytes[colorIndex + 1] = data.Color.G;
                    uncompressedBytes[colorIndex + 2] = data.Color.B;
                }
                
                sumUncompressed += uncompressedBytes.Length;
                
                int packedStart = sumCompressed;
                
                if (compress) {
                    var compressedBytes = LZ4.LZ4Codec.Encode32HC(uncompressedBytes, 0, uncompressedBytes.Length);
                    sumCompressed += compressedBytes.Length;
                    blockInfo.bytes = compressedBytes;
                } else {
                    sumCompressed += uncompressedBytes.Length;
                    blockInfo.bytes = uncompressedBytes;
                }
                
                ref var packedBlockInfo = ref packInfos[blockIndex];
                packedBlockInfo.StartOffset = blockInfo.startOffset;
                packedBlockInfo.PackedStart = packedStart;
                packedBlockInfo.NodeCount = blockInfo.count;
                packedBlockInfo.PackedSize = sumCompressed - packedStart;
                
                blockIndex++;
            }
            
            packedData = new byte[sumCompressed];
            
            int dataOffset = 0;
            foreach (var blockInfo in blockInfos) {
                System.Array.Copy(blockInfo.bytes, 0, packedData, dataOffset, blockInfo.bytes.Length);
                dataOffset += blockInfo.bytes.Length;
            }
            
            Debug.Log($"Nodes: {count}, Blocks: {blockInfos.Count}, Bytes: {sumUncompressed} -> {sumCompressed}");
        }
        
        private static void CalculateStartOffsets(LinkedList<BlockInfo> blockInfos) {
            foreach (var blockInfo in blockInfos) {
                blockInfo.startOffset = -1;
                for (int i = 0; i < 8; i++) {
                    var subnode = blockInfo.start[i] as LinkedNode;
                    if (subnode == null) continue;
                    blockInfo.startOffset = subnode.offset;
                    break;
                }
            }
        }
        
        private static LinkedList<BlockInfo> CalculateBlocks(LinkedNode root, int blockSizeShift) {
            // Block size can't be less than 8 nodes
            if (blockSizeShift < 3) blockSizeShift = 3;
            
            int blockSizeMax = 1 << blockSizeShift;
            
            var blockInfos = new LinkedList<BlockInfo>();
            var blockInfo = new BlockInfo();
            blockInfo.start = root;
            blockInfos.AddLast(blockInfo);
            
            for (var node = root; node != null; node = node.next) {
                node.offset = blockInfo.offset + blockInfo.count;
                
                blockInfo.count++;
                if (blockInfo.count <= blockSizeMax) continue;
                
                var newBlockInfo = new BlockInfo();
                newBlockInfo.start = node;
                newBlockInfo.count = 1;
                newBlockInfo.offset = blockInfo.offset + blockSizeMax;
                blockInfos.AddLast(newBlockInfo);
                
                // Move to a new block this node and the previous nodes with the same id
                for (var prev = node.prev; prev.id == node.id; prev = prev.prev) {
                    newBlockInfo.start = prev;
                    newBlockInfo.count++;
                }
                
                var fixNode = newBlockInfo.start;
                for (int fixIndex = 0; fixIndex < newBlockInfo.count; fixIndex++) {
                    fixNode.offset = newBlockInfo.offset + fixIndex;
                    fixNode = fixNode.next;
                }
                
                blockInfo.count -= newBlockInfo.count;
                blockInfo = newBlockInfo;
            }
            
            return blockInfos;
        }
        
        private void LinkLevels() {
            for (int level = 1; level < levelHeads.Count; level++) {
                var tail = levelTails[level - 1];
                var head = levelHeads[level];
                tail.next = head;
                head.prev = tail;
            }
        }
        
        private void LinkNodes(LinkedNode node, int id, int level = 0) {
            count++;
            
            node.id = id;
            
            if (level == levelHeads.Count) {
                levelHeads.Add(node);
                levelTails.Add(node);
            } else {
                var tail = levelTails[level];
                tail.next = node;
                node.prev = tail;
                levelTails[level] = node;
            }
            
            idCounter++;
            id = idCounter;
            
            // We need all children of a node to have the same id
            for (int i = 0; i < 8; i++) {
                var subnode = node[i] as LinkedNode;
                if (subnode == null) continue;
                LinkNodes(subnode, id, level+1);
            }
        }
        
        private LinkedNode ToLinkedOctree(int[] nodes, Color32[] colors, int node, Color32 color) {
            int mask = node & 0xFF;
            
            var data = new NodeData {
                Mask = (byte)mask,
                Color = new Color24 {R = color.r, G = color.g, B = color.b}
            };
            
            var linkedNode = new LinkedNode();
            linkedNode.data = data;
            
            int nodeIndex = (node >> 8) & 0xFFFFFF;
            int address = nodeIndex << 3;
            
            for (int i = 0; i < 8; i++) {
                if ((mask & (1 << i)) == 0) continue;
                linkedNode[i] = ToLinkedOctree(nodes, colors, nodes[address|i], colors[address|i]);
            }
            
            return linkedNode;
        }
    }
}

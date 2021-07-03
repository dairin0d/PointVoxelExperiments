using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace dairin0d.Rendering {
    public class Log2AllocatorTest : MonoBehaviour
    {
        public int Log2BlockSize = 1;
        public int Log2InitialDepth = -1;
        public int Log2MemoryLimit = -1;
        public int AllocSize = 1;
        
        private Log2Allocator<int> allocator;
        
        void Awake() {
            allocator = new Log2Allocator<int>(Log2BlockSize, Log2InitialDepth, Log2MemoryLimit);
        }
        
        void OnGUI() {
            var cellTemplate = new Rect(0, 0, 20, 20);
            
            int deallocateAddress = -1;
            
            foreach (var range in allocator.EnumerateRanges()) {
                var cell = cellTemplate;
                cell.x += range.Start * cell.width;
                cell.width *= range.Size;
                GUI.color = (range.Allocated ? Color.red : Color.green);
                if (GUI.Button(cell, (range.Allocated ? "#" : " "))) {
                    deallocateAddress = range.Start;
                }
            }
            
            if (deallocateAddress >= 0) allocator.Deallocate(deallocateAddress);
            
            GUI.color = Color.white;
            
            var allocRect = cellTemplate;
            allocRect.y += allocRect.height * 2;
            allocRect.width = 80;
            
            if (GUI.Button(allocRect, "x2 Size")) {
                allocator.DoubleSize();
            }
            
            allocRect.x += allocRect.width;
            
            if (GUI.Button(allocRect, $"Allocate {AllocSize}")) {
                allocator.Allocate(AllocSize);
            }
            
            allocRect.x += allocRect.width;
            allocRect.width = 128;
            
            AllocSize = Mathf.RoundToInt(GUI.HorizontalSlider(allocRect, AllocSize, 1, 32));
            
            var infoRect = new Rect(0, allocRect.y + allocRect.height * 2, Screen.width, allocRect.height);
            
            foreach (var info in allocator.PrintInfos()) {
                GUI.Label(infoRect, info);
                infoRect.y += infoRect.height;
            }
        }
    }
}
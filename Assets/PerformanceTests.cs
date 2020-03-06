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
using UnityEngine;

/*
Count = 10000000

Editor:
ManagedCopy: 51-52-53-54
UnsafeCopy: 54-55
PointerCopy: 54-55
IfStatement: 123-125, 132
IfExpression: 97-96, 103-104
Branchless: 57-58-59
PointerCastCopy: 55-57
UnpackField: 50, 57
UnpackMask: 52-53

Build - Mono:
ManagedCopy: 40, 42-43
UnsafeCopy: 40, 43-44
PointerCopy: 15, 16
IfStatement: 42-43
IfExpression: 47-48
Branchless: 18-19
PointerCastCopy: 40
UnpackField: 29
UnpackMask: 14-15, 19

Build - IL2CPP - Release:
ManagedCopy: 22-24
UnsafeCopy: 15, 17
PointerCopy: 14-15, 17-18
IfStatement: 14
IfExpression: 18
Branchless: 17-18
PointerCastCopy: 17-18
UnpackField: 15-17, 24
UnpackMask: 16

In Mono (or at least Editor), ((a - b) & ((a - b) >> 31)) is slightly
faster than when storing the difference in a temporary variable.
In IL2CPP, they take the same time.

Editor:
NodeMin: 291, 293
NodeMinB: 212, 196 // not full scenario
NodeMinC: 186, 168 // not full scenario
NodeMinD: 242, 230
NodeMinLevel: 325, 321
NodeMinNoShift: 221, 227
NodeMinIf: 301, 296
NodeMinIfB: 310, 295
NodeMinIfC: 318, 279
NodeMinIfD: 306, 281
NodeMinIfE: 251, 233
NodeMinIfF: 298, 291

Mono:
NodeMin: 132, 134
NodeMinB: 98, 87 // not full scenario
NodeMinC: 113, 115 // not full scenario
NodeMinD: 123, 119
NodeMinLevel: 147, 162
NodeMinNoShift: 110, 108
NodeMinIf: 81, 82
NodeMinIfB: 93, 78
NodeMinIfC: 94, 77
NodeMinIfD: 99, 82
NodeMinIfE: 93, 94
NodeMinIfF: 84, 71

IL2CPP:
NodeMin: 72
NodeMinB: 71 // not full scenario
NodeMinC: 72 // not full scenario
NodeMinD: 79
NodeMinLevel: 81
NodeMinNoShift: 68
NodeMinIf: 61
NodeMinIfB: 63
NodeMinIfC: 60
NodeMinIfD: 54
NodeMinIfE: 56
NodeMinIfF: 54


*/

public class PerformanceTests : MonoBehaviour {
    System.Diagnostics.Stopwatch stopwatch;
    
    public int vSyncCount = 0;
    public int targetFrameRate = 30;
    
    public int Count = 10000000;
    public int Seed = 0;
    
    public int Threshold;
    
    enum TestMode {
        ManagedCopy,
        UnsafeCopy,
        PointerCopy,
        IfStatement,
        IfExpression,
        Branchless,
        PointerCastCopy,
        UnpackField,
        UnpackMask,
        ReadLocal,
        ReadField,
        // DeltaCenter,
        // DeltaCenterC,
        // DeltaMinMax,
        NodeMin,
        NodeMinB,
        NodeMinC,
        NodeMinD,
        NodeMinLevel,
        NodeMinNoShift,
        NodeMinIf,
        NodeMinIfB,
        NodeMinIfC,
        NodeMinIfD,
        NodeMinIfE,
        NodeMinIfF,
    }
    
    TestMode[] test_modes;
    int[] test_times;
    
    int[] arr1, arr2;
    
    struct TestStruct {
        public short s0, s1;
    }
    
    // struct DeltaCenter {
    //     public int x, y, z;
    //     // public int pad0;
    // }
    // struct DeltaMinMax {
    //     public int x0, y0, z0;
    //     public int x1, y1;
    //     // public int pad0;
    //     // public int pad1;
    //     // public int pad2;
    // }
    
    // DeltaCenter[] deltas_center = new DeltaCenter[8];
    // DeltaMinMax[] deltas_minmax = new DeltaMinMax[8];
    
    struct StackItem {
        public int dx0, x0, px0, x0b;
        public int dy0, y0, py0, y0b;
        public int dx1, x1, px1, x1b;
        public int dy1, y1, py1, y1b;
        public int last;
        // public int t0, t1, t2;
    }
    StackItem[] stack_main = new StackItem[9];
    
    void Start() {
        if (!Application.isEditor) Screen.SetResolution(640, 480, false);
        
        stopwatch = new System.Diagnostics.Stopwatch();
        
        test_modes = (TestMode[]) System.Enum.GetValues(typeof(TestMode));
        test_times = new int[test_modes.Length];
        
        arr1 = new int[Count];
        arr2 = new int[Count];
        
        Random.InitState(Seed);
        for (int i = 0; i < Count; i++) {
            arr1[i] = Random.Range(short.MinValue, short.MaxValue);
        }
        
        int subpixel_shift = 8;
        int w = 640, h = 480;
        for (int i = 0; i < stack_main.Length; i++) {
            var item = stack_main[i];
            item.x0 = Random.Range(-1, 0) << subpixel_shift;
            item.y0 = Random.Range(-1, 0) << subpixel_shift;
            item.x1 = Random.Range(w, w+1) << subpixel_shift;
            item.y1 = Random.Range(h, h+1) << subpixel_shift;
            item.x0b = 0;
            item.y0b = 0;
            item.x1b = w;
            item.y1b = h;
            stack_main[i] = item;
        }
    }

    void Update() {
        QualitySettings.vSyncCount = vSyncCount;
        Application.targetFrameRate = targetFrameRate;
    }
    
	void OnGUI() {
        float x = 0, y = 0, btn_w = 160, screen_w = Screen.width, line_h = 20;
        GUI.Label(new Rect(x, y, screen_w, line_h), $"Count = {Count}");
        y += line_h;
        for (int test_index = 0; test_index < test_times.Length; test_index++) {
            var test_mode = test_modes[test_index];
            var test_time = test_times[test_index];
            if (GUI.Button(new Rect(x, y, btn_w, line_h), $"{test_mode}: {test_time}")) Test(test_index);
            y += line_h;
            if (y > (Screen.height-line_h)) { y = 0; x += btn_w; }
        }
	}
    
    unsafe void Test(int test_index, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        var test_mode = test_modes[test_index];
        fixed (int* _ptr1 = arr1, _ptr2 = arr2) {
            int* ptr1 = _ptr1;
            int* ptr2 = _ptr2;
            int* ptr1_end = ptr1 + arr1.Length;
            if (test_mode == TestMode.ManagedCopy) {
                stopwatch.Restart();
                for (int i = 0; i < arr1.Length; i++) {
                    arr2[i] = arr1[i];
                }
            } else if (test_mode == TestMode.UnsafeCopy) {
                stopwatch.Restart();
                for (int i = 0; i < arr1.Length; i++) {
                    ptr2[i] = ptr1[i];
                }
            } else if (test_mode == TestMode.PointerCopy) {
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = *ptr1;
                }
            } else if (test_mode == TestMode.IfStatement) {
                int threshold = Threshold;
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = *ptr1;
                    if (*ptr2 < threshold) *ptr2 = threshold;
                }
            } else if (test_mode == TestMode.IfExpression) {
                int threshold = Threshold;
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = (*ptr1 < threshold ? threshold : *ptr1);
                }
            } else if (test_mode == TestMode.Branchless) {
                int threshold = Threshold;
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = *ptr1 - ((*ptr1 - threshold) & ((*ptr1 - threshold) >> 31));
                }
            } else if (test_mode == TestMode.PointerCastCopy) {
                byte* ptr1b = (byte*)ptr1;
                byte* ptr2b = (byte*)ptr2;
                byte* ptr1b_end = (byte*)ptr1_end;
                for (stopwatch.Restart(); ptr1b != ptr1b_end; ptr1b += 4, ptr2b += 4) {
                    *((int*)ptr2b) = *((int*)ptr1b);
                }
            } else if (test_mode == TestMode.UnpackField) {
                TestStruct* fptr1 = (TestStruct*)ptr1;
                TestStruct* fptr1_end = (TestStruct*)ptr1_end;
                for (stopwatch.Restart(); fptr1 != fptr1_end; ++fptr1, ++ptr2) {
                    *ptr2 = fptr1->s0;
                }
            } else if (test_mode == TestMode.UnpackMask) {
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = (*ptr1) & 0xFFFF;
                }
            } else if (test_mode == TestMode.ReadLocal) {
                int value = targetFrameRate + 1;
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = value;
                }
            } else if (test_mode == TestMode.ReadField) {
                for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
                    *ptr2 = targetFrameRate;
                }
            // } else if (test_mode == TestMode.DeltaCenter) {
            //     int radius = 10;
            //     fixed (DeltaCenter* deltas = deltas_center) {
            //         for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
            //             var delta = (deltas + (*ptr1 & 7));
            //             int min = *ptr1 + delta->x - radius;
            //             int max = *ptr1 + delta->x + radius;
            //             *ptr2 = min + max;
            //         }
            //     }
            // } else if (test_mode == TestMode.DeltaCenterC) {
            //     int radius = 10;
            //     fixed (DeltaCenter* deltas = deltas_center) {
            //         for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
            //             var delta = (deltas + (*ptr1 & 7));
            //             int center = *ptr1 + delta->x;
            //             int min = center - radius;
            //             int max = center + radius;
            //             *ptr2 = min + max;
            //         }
            //     }
            // } else if (test_mode == TestMode.DeltaMinMax) {
            //     fixed (DeltaMinMax* deltas = deltas_minmax) {
            //         for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1, ++ptr2) {
            //             var delta = (deltas + (*ptr1 & 7));
            //             int min = *ptr1 + delta->x0;
            //             int max = *ptr1 + delta->x1;
            //             *ptr2 = min + max;
            //         }
            //     }
            } else if (test_mode == TestMode.NodeMin) {
                test_NodeMin(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinB) {
                test_NodeMinB(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinC) {
                test_NodeMinC(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinD) {
                test_NodeMinD(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinLevel) {
                test_NodeMinLevel(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinNoShift) {
                test_NodeMinNoShift(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinIf) {
                test_NodeMinIf(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinIfB) {
                test_NodeMinIfB(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinIfC) {
                test_NodeMinIfC(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinIfD) {
                test_NodeMinIfD(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinIfE) {
                test_NodeMinIfE(ptr1, ptr2, ptr1_end);
            } else if (test_mode == TestMode.NodeMinIfF) {
                test_NodeMinIfF(ptr1, ptr2, ptr1_end);
            }
            stopwatch.Stop();
            test_times[test_index] = (int)stopwatch.ElapsedMilliseconds;
        }
    }
    
    unsafe void test_NodeMin(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = current->x0 >> subpixel_shift;
                current->px0 = current->px0 - ((current->px0 - 0) & ((current->px0 - 0) >> 31));
                // current->px0 = current->px0 - ((current->px0 - stack0->last) & ((current->px0 - stack0->last) >> 31)); // Editor: 308-310
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0 >> subpixel_shift;
                // current->py0 = current->py0 - ((current->py0 - 0) & ((current->py0 - 0) >> 31)); // Editor: 291-293
                current->py0 = current->py0 - ((current->py0 - stack0->last) & ((current->py0 - stack0->last) >> 31));
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = current->x1 >> subpixel_shift;
                current->px1 = current->px1 + ((w - current->px1) & ((w - current->px1) >> 31));
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = current->y1 >> subpixel_shift;
                current->py1 = current->py1 + ((h - current->py1) & ((h - current->py1) >> 31));
            }
        }
    }
    
    unsafe void test_NodeMinB(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = (current->x0 & ~(current->x0 >> 31)) >> subpixel_shift;
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = (current->y0 & ~(current->y0 >> 31)) >> subpixel_shift;
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = (current->x1 & ~(current->x1 >> 31)) >> subpixel_shift;
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = (current->y1 & ~(current->y1 >> 31)) >> subpixel_shift;
            }
        }
    }
    
    unsafe void test_NodeMinC(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->y0 = stack0->y0 + current->dy0;
                current->x1 = stack0->x1 + current->dx1;
                current->y1 = stack0->y1 + current->dy1;
                current->px0 = (current->x0 & ~(current->x0 >> 31)) >> subpixel_shift;
                current->py0 = (current->y0 & ~(current->y0 >> 31)) >> subpixel_shift;
                current->px1 = (current->x1 & ~(current->x1 >> 31)) >> subpixel_shift;
                current->py1 = (current->y1 & ~(current->y1 >> 31)) >> subpixel_shift;
            }
        }
    }
    
    unsafe void test_NodeMinD(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = (current->x0 & ~(current->x0 >> 31)) >> subpixel_shift;
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0 >> subpixel_shift;
                current->py0 -= ((current->py0 - stack0->last) & ((current->py0 - stack0->last) >> 31));
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = w - ((current->x1 & ~(current->x1 >> 31)) >> subpixel_shift);
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = h - ((current->y1 & ~(current->y1 >> 31)) >> subpixel_shift);
            }
        }
    }
    
    unsafe void test_NodeMinLevel(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        int level = 2;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + (current->dx0 >> level);
                current->px0 = current->x0 >> subpixel_shift;
                current->px0 = current->px0 - ((current->px0 - 0) & ((current->px0 - 0) >> 31));
                current->y0 = stack0->y0 + (current->dy0 >> level);
                current->py0 = current->y0 >> subpixel_shift;
                current->py0 = current->py0 - ((current->py0 - stack0->last) & ((current->py0 - stack0->last) >> 31));
                current->x1 = stack0->x1 + (current->dx1 >> level);
                current->px1 = current->x1 >> subpixel_shift;
                current->px1 = current->px1 + ((w - current->px1) & ((w - current->px1) >> 31));
                current->y1 = stack0->y1 + (current->dy1 >> level);
                current->py1 = current->y1 >> subpixel_shift;
                current->py1 = current->py1 + ((h - current->py1) & ((h - current->py1) >> 31));
            }
        }
    }
    
    unsafe void test_NodeMinNoShift(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = current->x0 - ((current->x0 - 0) & ((current->x0 - 0) >> 31));
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0 - ((current->y0 - stack0->last) & ((current->y0 - stack0->last) >> 31));
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = current->x1 + ((w - current->x1) & ((w - current->x1) >> 31));
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = current->y1 + ((h - current->y1) & ((h - current->y1) >> 31));
            }
        }
    }
    
    unsafe void test_NodeMinIf(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = current->x0 >> subpixel_shift;
                if (current->px0 < 0) current->px0 = 0;
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0 >> subpixel_shift;
                if (current->py0 < stack0->last) current->py0 = stack0->last;
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = current->x1 >> subpixel_shift;
                if (current->px1 > w) current->px1 = w;
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = current->y1 >> subpixel_shift;
                if (current->py1 > h) current->py1 = h;
            }
        }
    }
    
    unsafe void test_NodeMinIfB(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = current->x0 >> subpixel_shift;
                if (current->px0 < stack0->x0b) current->px0 = stack0->x0b;
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0 >> subpixel_shift;
                if (current->py0 < stack0->y0b) current->py0 = stack0->y0b;
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = current->x1 >> subpixel_shift;
                if (current->px1 > stack0->x1b) current->px1 = stack0->x1b;
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = current->y1 >> subpixel_shift;
                if (current->py1 > stack0->y1b) current->py1 = stack0->y1b;
            }
        }
    }
    
    unsafe void test_NodeMinIfC(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = current->x0 >> subpixel_shift;
                if (current->px0 < x0b) current->px0 = x0b;
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0 >> subpixel_shift;
                if (current->py0 < y0b) current->py0 = y0b;
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = current->x1 >> subpixel_shift;
                if (current->px1 > x1b) current->px1 = x1b;
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = current->y1 >> subpixel_shift;
                if (current->py1 > y1b) current->py1 = y1b;
            }
        }
    }
    
    unsafe void test_NodeMinIfD(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->px0 = current->x0 = stack0->x0 + current->dx0;
                if (current->px0 < 0) current->px0 = 0;
                current->py0 = current->y0 = stack0->y0 + current->dy0;
                if (current->py0 < stack0->last) current->py0 = stack0->last;
                current->px1 = current->x1 = stack0->x1 + current->dx1;
                if (current->px1 > w) current->px1 = w;
                current->py1 = current->y1 = stack0->y1 + current->dy1;
                if (current->py1 > h) current->py1 = h;
            }
        }
    }
    
    unsafe void test_NodeMinIfE(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->px0 = current->x0 = stack0->x0 + current->dx0;
                current->py0 = current->y0 = stack0->y0 + current->dy0;
                current->px1 = current->x1 = stack0->x1 + current->dx1;
                current->py1 = current->y1 = stack0->y1 + current->dy1;
                if (current->px0 < 0) current->px0 = 0;
                if (current->py0 < stack0->last) current->py0 = stack0->last;
                if (current->px1 > w) current->px1 = w;
                if (current->py1 > h) current->py1 = h;
            }
        }
    }
    
    unsafe void test_NodeMinIfF(int* ptr1, int* ptr2, int* ptr1_end, int subpixel_shift=8, int x0b=0, int y0b=0, int x1b=640, int y1b=480) {
        int w = x1b, h = y1b;
        fixed (StackItem* stack = stack_main) {
            var stack0 = stack + 8;
            for (stopwatch.Restart(); ptr1 != ptr1_end; ++ptr1) {
                var current = (stack + (*ptr1 & 7));
                current->x0 = stack0->x0 + current->dx0;
                current->px0 = current->x0;
                if (current->px0 < 0) current->px0 = 0;
                current->y0 = stack0->y0 + current->dy0;
                current->py0 = current->y0;
                if (current->py0 < stack0->last) current->py0 = stack0->last;
                current->x1 = stack0->x1 + current->dx1;
                current->px1 = current->x1;
                if (current->px1 > w) current->px1 = w;
                current->y1 = stack0->y1 + current->dy1;
                current->py1 = current->y1;
                if (current->py1 > h) current->py1 = h;
            }
        }
    }
}

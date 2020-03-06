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
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Quick & dirty reader of some common point-cloud formats (TXT, CSV, PLY, PCD).
/// For this project, I only care about position, color and maybe normals,
/// and float's 23 bits of precision are more than enough for my goals.
/// </summary>
public static class PointCloudFile {
	enum DataMode {
		LittleEndian=0, BigEndian=1,
		Compressed=2,
		Text=4,
	}

	enum DataType {
		F4=8|2, F8=8|3, // float, double
		U1=0|0, U2=0|1, U4=0|2, U8=0|3, // byte, ushort, uint, ulong
		I1=4|0, I2=4|1, I4=4|2, I8=4|3, // sbyte, short, int, long
	}

	class ChannelInfo {
		public string name;
		public int offset;
		public DataType datatype;
		public ChannelInfo(string name, int offset, DataType datatype=DataType.F4) {
			this.name = name; this.offset = offset; this.datatype = datatype;
		}
	}

	// Supported: txt, csv, pcd, ply
	public class Reader : System.IDisposable {
		Stream stream;
		LineReader ts;
		DataMode data_mode = DataMode.Text;
		int width = -1, height = -1, ix = 0, iy = 0;
		int i_start = 0, i_end = int.MaxValue, i_delta = 1;
		List<ChannelInfo> channel_infos;
		int i;
		byte[] row;
		IList<string> comments;

		public Reader(string path, IList<string> comments=null) {
			//if (!File.Exists(path)) return;

			this.comments = comments;

			string ext = Path.GetExtension(path).ToLowerInvariant();
			channel_infos = new List<ChannelInfo>();

			stream = new FileStream(path, FileMode.Open, FileAccess.Read);
			ts = new LineReader(stream);
			if (ext == ".ply") {
				ReadHeaderPLY();
				stream.Position += i_start;
			} else if (ext == ".pcd") {
				// All data is assumed to be little-endian
				// http://www.pcl-developers.org/io-and-endians-td4645565.html
				ReadHeaderPCD();
				// NOTE: I couldn't figure out how to correctly load the compressed files from
				// https://github.com/PointCloudLibrary/pcl/tree/master/test (results appear
				// to be garbled, no idea why).
				if ((data_mode & DataMode.Compressed) == DataMode.Compressed) {
					var tmp_buf = new byte[4];
					stream.Read(tmp_buf, 0, 4);
					uint compressed_size = ReadData(tmp_buf, 0, DataType.U4, DataMode.LittleEndian).U4;
					stream.Read(tmp_buf, 0, 4);
					uint uncompressed_size = ReadData(tmp_buf, 0, DataType.U4, DataMode.LittleEndian).U4;
					var compressed_bytes = new byte[compressed_size];
					stream.Read(compressed_bytes, 0, compressed_bytes.Length);
					var decompressed_bytes = new byte[uncompressed_size];
					CLZF2.lzf_decompress(compressed_bytes, ref decompressed_bytes);
					stream = new MemoryStream(decompressed_bytes);
					data_mode &= ~DataMode.Compressed;
				}
			} else {
				ReadHeaderASCII();
			}

			if (data_mode == DataMode.Text) {
				i = 0;
			} else {
				i = i_start;
				row = new byte[i_delta];
			}
		}

		public void Dispose() {
			if (stream == null) return;
			stream.Close();
			stream.Dispose();
			stream = null;
		}

		public bool EndOfStream {
			get { return (stream == null) || (stream.Position >= stream.Length); }
		}

		public bool Read(out Vector3 pos, out Color32 color, out Vector3 normal) {
			pos = Vector3.zero;
			color = new Color32(255, 255, 255, 255);
			normal = Vector3.zero;
			if (EndOfStream || (i >= i_end)) return false;

			string[] parts = null;
			if (data_mode == DataMode.Text) {
				while (true) {
					parts = NextRow(ts);
					if (parts.Length == 0) return false;
					if (i >= i_start) break;
					i++;
				}
			} else {
				if (stream.Read(row, 0, i_delta) < i_delta) return false;
			}

			if ((width > 0) & (height > 0)) {
				pos = new Vector3(ix, iy, 0);
				if (++ix >= width) { ix = 0; --iy; }
			}

			byte vb = 0; int vi = 0;
			if (data_mode == DataMode.Text) {
				for (int ci = 0; ci < channel_infos.Count; ci++) {
					var chn_info = channel_infos[ci];
					if (chn_info.offset >= parts.Length) continue;
					switch (chn_info.name) {
					case "x": pos.x = ParseFloat(parts[chn_info.offset]); break;
					case "y": pos.y = ParseFloat(parts[chn_info.offset]); break;
					case "z": pos.z = ParseFloat(parts[chn_info.offset]); break;
					case "r": color.r = (byte)ParseInt(parts[chn_info.offset]); break;
					case "g": color.g = (byte)ParseInt(parts[chn_info.offset]); break;
					case "b": color.b = (byte)ParseInt(parts[chn_info.offset]); break;
					case "nx": normal.x = ParseFloat(parts[chn_info.offset]); break;
					case "ny": normal.y = ParseFloat(parts[chn_info.offset]); break;
					case "nz": normal.z = ParseFloat(parts[chn_info.offset]); break;
					case "intensity":
						vb = (byte)(Mathf.Clamp01(ParseFloat(parts[chn_info.offset]))*255f);
						color.r = color.g = color.b = vb;
						break;
					case "rgba":
						vi = ParseInt(parts[chn_info.offset]);
						color.r = (byte)(vi & 0xFF);
						color.g = (byte)((vi >> 8) & 0xFF);
						color.b = (byte)((vi >> 16) & 0xFF);
						color.a = (byte)((vi >> 24) & 0xFF);
						break;
					}
				}
			} else {
				for (int ci = 0; ci < channel_infos.Count; ci++) {
					var chn_info = channel_infos[ci];
					switch (chn_info.name) {
					case "x": pos.x = ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "y": pos.y = ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "z": pos.z = ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "r": color.r = (byte)ReadInt(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "g": color.g = (byte)ReadInt(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "b": color.b = (byte)ReadInt(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "nx": normal.x = ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "ny": normal.y = ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "nz": normal.z = ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode); break;
					case "intensity":
						vb = (byte)(Mathf.Clamp01(ReadFloat(row, chn_info.offset, chn_info.datatype, data_mode))*255f);
						color.r = color.g = color.b = vb;
						break;
					case "rgba":
						vi = ReadInt(row, chn_info.offset, chn_info.datatype, data_mode);
						color.r = (byte)(vi & 0xFF);
						color.g = (byte)((vi >> 8) & 0xFF);
						color.b = (byte)((vi >> 16) & 0xFF);
						color.a = (byte)((vi >> 24) & 0xFF);
						break;
					}
				}
			}

			normal.Normalize();

			i += i_delta;
			return true;
		}

		static Dictionary<string, string> channel_name_map = new Dictionary<string, string>() {
			{"x", "x"}, {"y", "y"}, {"z", "z"},
			{"r", "r"}, {"g", "g"}, {"b", "b"},
			{"nx", "nx"}, {"ny", "ny"}, {"nz", "nz"},
			{"intensity", "intensity"},
			{"red", "r"}, {"green", "g"}, {"blue", "b"},
			{"diffuse_red", "r"}, {"diffuse_green", "g"}, {"diffuse_blue", "b"},
			{"normal_x", "nx"}, {"normal_y", "ny"}, {"normal_z", "nz"},
			{"rgba", "rgba"}, {"rgb", "rgba"},
		};

		void ReadHeaderPLY() {
			int prev_count = 0, prev_size = 0, last_count = 0, vertex_count = 0, prop_offset = 0;
			bool before_vertex = true, after_vertex = false;
			i_start = i_end = 0; i_delta = 1;
			while (!ts.EndOfStream) {
				var parts = NextRow(ts);
				if (parts.Length == 0) break;
				if (parts[0] == "format") {
					if (parts.Length > 1) {
						if (parts[1] == "binary_little_endian") {
							data_mode = DataMode.LittleEndian;
						} else if (parts[1] == "binary_big_endian") {
							data_mode = DataMode.BigEndian;
						} else {
							data_mode = DataMode.Text;
						}
					}
				} else if (parts[0] == "element") {
					if (parts.Length > 2) {
						last_count = ParseInt(parts[2]);
						if (parts[1] == "vertex") {
							before_vertex = after_vertex = false;
							vertex_count = last_count;
						} else {
							if (!before_vertex) after_vertex = true;
							if (before_vertex) prev_count += last_count;
						}
					}
				} else if (parts[0] == "property") {
					if (parts.Length > 1) {
						int prop_size = 0;
						DataType dtype = DataType.F4;
						switch (parts[1]) {
						case "char": prop_size = 1; dtype = DataType.I1; break;
						case "uchar": prop_size = 1; dtype = DataType.U1; break;
						case "short": prop_size = 2; dtype = DataType.I2; break;
						case "ushort": prop_size = 2; dtype = DataType.U2; break;
						case "int": prop_size = 4; dtype = DataType.I4; break;
						case "uint": prop_size = 4; dtype = DataType.U4; break;
						case "long": prop_size = 8; dtype = DataType.I8; break;
						case "ulong": prop_size = 8; dtype = DataType.U8; break;
						case "float": prop_size = 4; dtype = DataType.F4; break;
						case "double": prop_size = 8; dtype = DataType.F8; break;
						case "int8": prop_size = 1; dtype = DataType.I1; break;
						case "uint8": prop_size = 1; dtype = DataType.U1; break;
						case "int16": prop_size = 2; dtype = DataType.I2; break;
						case "uint16": prop_size = 2; dtype = DataType.U2; break;
						case "int32": prop_size = 4; dtype = DataType.I4; break;
						case "uint32": prop_size = 4; dtype = DataType.U4; break;
						case "int64": prop_size = 8; dtype = DataType.I8; break;
						case "uint64": prop_size = 8; dtype = DataType.U8; break;
						case "float32": prop_size = 4; dtype = DataType.F4; break;
						case "float64": prop_size = 8; dtype = DataType.F4; break;
						}
						if (before_vertex | after_vertex) {
							if (before_vertex) prev_size += last_count * prop_size;
						} else {
							if (parts.Length > 2) {
								string mapped_name;
								if (channel_name_map.TryGetValue(parts[2], out mapped_name)) {
									channel_infos.Add(new ChannelInfo(mapped_name, prop_offset, dtype));
								}
							}
							if (data_mode == DataMode.Text) prop_size = 1;
							prop_offset += prop_size;
						}
					}
				} else if (parts[0] == "comment") {
					if (comments != null) comments.Add(string.Join(" ", parts, 1, parts.Length-1));
				} else if (parts[0] == "end_header") {
					break;
				}
			}
			if (data_mode == DataMode.Text) {
				i_start = prev_count;
				i_end = i_start + vertex_count;
				i_delta = 1;
			} else {
				i_start = prev_size;
				i_end = i_start + vertex_count * prop_offset;
				i_delta = prop_offset;
			}
		}

		void ReadHeaderPCD() {
			width = height = 1; i_delta = 0;
			var names = new List<string>();
			var sizes = new List<int>();
			var types = new List<string>();
			var counts = new List<int>();
			while (!ts.EndOfStream) {
				var parts = NextRow(ts);
				if (parts.Length == 0) break;
				if (parts[0] == "fields") {
					for (int i = 1; i < parts.Length; i++) {
						if (i > names.Count) {
							names.Add(""); sizes.Add(0); types.Add(""); counts.Add(0);
						}
						names[i-1] = parts[i];
					}
				} else if (parts[0] == "size") {
					for (int i = 1; i < parts.Length; i++) {
						if (i > names.Count) {
							names.Add(""); sizes.Add(0); types.Add(""); counts.Add(0);
						}
						sizes[i-1] = ParseInt(parts[i]);
					}
				} else if (parts[0] == "type") {
					for (int i = 1; i < parts.Length; i++) {
						if (i > names.Count) {
							names.Add(""); sizes.Add(0); types.Add(""); counts.Add(0);
						}
						types[i-1] = parts[i];
					}
				} else if (parts[0] == "count") {
					for (int i = 1; i < parts.Length; i++) {
						if (i > names.Count) {
							names.Add(""); sizes.Add(0); types.Add(""); counts.Add(0);
						}
						counts[i-1] = ParseInt(parts[i]);
					}
				} else if (parts[0] == "width") {
					if (parts.Length > 1) width = ParseInt(parts[1]);
				} else if (parts[0] == "height") {
					if (parts.Length > 1) height = ParseInt(parts[1]);
				} else if (parts[0] == "data") {
					if (parts.Length > 1) {
						if (parts[1] == "binary") {
							data_mode = DataMode.LittleEndian;
						} else if (parts[1] == "binary_compressed") {
							data_mode = DataMode.LittleEndian | DataMode.Compressed;
						} else {
							data_mode = DataMode.Text;
						}
					}
					break;
				}
			}
			for (int i = 0; i < names.Count; i++) {
				DataType dtype = DataType.F4;
				try {
					dtype = (DataType)System.Enum.Parse(typeof(DataType), types[i]+sizes[i], true);
				} catch (System.ArgumentException) {
				}
				string mapped_name;
				if (channel_name_map.TryGetValue(names[i], out mapped_name)) {
					channel_infos.Add(new ChannelInfo(mapped_name, i_delta, dtype));
				}
				if (data_mode == DataMode.Text) {
					i_delta += counts[i];
				} else {
					i_delta += sizes[i]*counts[i];
				}
			}
		}

		void ReadHeaderASCII() {
			var parts = NextRow(ts);
			bool is_header = false;
			for (int i = 0; i < parts.Length; i++) {
				if (!IsNumber(parts[i])) { is_header = true; break; }
			}
			if (!is_header) {
				ts.BaseStream.Position = 0;
				channel_infos.Add(new ChannelInfo("x", 0));
				channel_infos.Add(new ChannelInfo("y", 1));
				channel_infos.Add(new ChannelInfo("z", 2));
				channel_infos.Add(new ChannelInfo("r", 3));
				channel_infos.Add(new ChannelInfo("g", 4));
				channel_infos.Add(new ChannelInfo("b", 5));
				channel_infos.Add(new ChannelInfo("nx", 6));
				channel_infos.Add(new ChannelInfo("ny", 7));
				channel_infos.Add(new ChannelInfo("nz", 8));
			} else {
				for (int i = 0; i < parts.Length; i++) {
					string name = parts[i].ToLowerInvariant();
					string mapped_name;
					if (channel_name_map.TryGetValue(name, out mapped_name)) {
						channel_infos.Add(new ChannelInfo(mapped_name, i));
					}
				}
			}
		}
	}

	public class Writer : System.IDisposable {
		Stream stream;
		StreamWriter ts;
		BinaryWriter bs;
		DataMode data_mode = DataMode.Text;

		List<long> vcpos;
		string vcspace = "          "; // 10 chars is enough for int.MaxValue
		int vcount = 0;

		string[] fields = new string[]{"x","y","z","r","g","b","nx","ny","nz"};
		string sep = " ";

		public Writer(string path, bool binary=false, bool compressed=false, IList<string> comments=null) {
			string ext = Path.GetExtension(path).ToLowerInvariant();

			vcpos = new List<long>();

			stream = new FileStream(path, FileMode.Create, FileAccess.Write);
			ts = new StreamWriter(stream, Encoding.ASCII);
			ts.NewLine = "\n"; // ~better compatibility?

			if (ext == ".ply") {
				if (binary) data_mode = DataMode.LittleEndian;
				string format = (binary ? "binary_little_endian" : "ascii");
				ts.WriteLine("ply");
				ts.WriteLine("format "+format+" 1.0");
				if (comments != null) {
					foreach (var comment in comments) {
						ts.WriteLine("comment "+comment);
					}
				}
				ts.Write("element vertex "); WriteVSpace();
				ts.WriteLine("property float x");
				ts.WriteLine("property float y");
				ts.WriteLine("property float z");
				ts.WriteLine("property uchar r");
				ts.WriteLine("property uchar g");
				ts.WriteLine("property uchar b");
				ts.WriteLine("property float nx");
				ts.WriteLine("property float ny");
				ts.WriteLine("property float nz");
				ts.WriteLine("end_header");
			} else if (ext == ".pcd") {
				if (binary) data_mode = DataMode.LittleEndian;
				if (binary & compressed) data_mode |= DataMode.Compressed;
				string format = (binary ? (compressed ? "binary_compressed" : "binary") : "ascii");
				ts.WriteLine("VERSION .7");
				ts.WriteLine("FIELDS x y z r g b nx ny nz");
				ts.WriteLine("SIZE 4 4 4 1 1 1 4 4 4");
				ts.WriteLine("TYPE F F F U U U F F F");
				ts.WriteLine("COUNT 1 1 1 1 1 1 1 1 1");
				ts.Write("WIDTH "); WriteVSpace();
				ts.WriteLine("HEIGHT 1");
				ts.WriteLine("VIEWPOINT 0 0 0 1 0 0 0");
				ts.Write("POINTS "); WriteVSpace();
				ts.WriteLine("DATA "+format);
			} else {
				if (ext == ".csv") sep = ",";
				ts.WriteLine(string.Join(sep, fields));
			}
			ts.Flush(); // for the case when the data is binary

			if (data_mode != DataMode.Text) {
				if ((data_mode & DataMode.Compressed) == DataMode.Compressed) {
					bs = new BinaryWriter(new MemoryStream());
				} else {
					bs = new BinaryWriter(stream);
				}
			}
		}

		public void Dispose() {
			if (stream == null) return;
			if (data_mode == DataMode.Text) {
				ts.Flush();
			} else {
				bs.Flush();
				if ((data_mode & DataMode.Compressed) == DataMode.Compressed) {
					var uncompressed_bytes = ((MemoryStream)bs.BaseStream).ToArray();
					var compressed_bytes = CLZF2.Compress(uncompressed_bytes);
					bs = new BinaryWriter(stream);
					bs.Write((uint)compressed_bytes.Length);
					bs.Write((uint)uncompressed_bytes.Length);
					bs.Write(compressed_bytes);
					bs.Flush();
				}
			}
			for (int i = 0; i < vcpos.Count; i++) {
				stream.Position = vcpos[i];
				ts = new StreamWriter(stream, Encoding.ASCII);
				ts.Write(vcount);
				ts.Flush();
			}
			stream.Flush();
			stream.Close();
			stream.Dispose();
			stream = null;
		}

		public void Flush() {
			if (stream == null) return;
			stream.Flush();
		}

		void WriteVSpace() {
			ts.Flush();
			vcpos.Add(stream.Position);
			ts.WriteLine(vcspace);
		}

		public void Write(Vector3 p, Color32 c, Vector3 n) {
			if (data_mode == DataMode.Text) {
				ts.WriteLine(p.x+sep+p.y+sep+p.z+sep+c.r+sep+c.g+sep+c.b+sep+n.x+sep+n.y+sep+n.z);
			} else {
				bs.Write(p.x); bs.Write(p.y); bs.Write(p.z);
				bs.Write(c.r); bs.Write(c.g); bs.Write(c.b);
				bs.Write(n.x); bs.Write(n.y); bs.Write(n.z);
			}
			vcount++;
		}
	}

	#region Utility funtions/classes
	static char[] separators = new char[] {' ', '\t', '\r', '\n', ',', ';'};

	static string[] NextRow(LineReader ts) {
		return NextLine(ts).ToLowerInvariant().Split(separators, System.StringSplitOptions.RemoveEmptyEntries);
	}
	static string NextLine(LineReader ts) {
		while (!ts.EndOfStream) {
			string line = ts.ReadLine().Trim();
			if (!string.IsNullOrEmpty(line)) return line;
		}
		return string.Empty;
	}
	static bool IsNumber(string s) {
		if (s.ToLowerInvariant() == "nan") return true;
		double v;
		return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
	}
	static float ParseFloat(string s) {
		if (s.ToLowerInvariant() == "nan") return float.NaN;
		float v;
		bool ok = float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
		return (ok ? v : float.NaN);
	}
	static int ParseInt(string s) {
		int v;
		bool ok = int.TryParse(s, out v);
		return (ok ? v : 0);
	}

	[StructLayout(LayoutKind.Explicit)]
	struct BytesConverter {
		[FieldOffset(0)] public byte b0;
		[FieldOffset(1)] public byte b1;
		[FieldOffset(2)] public byte b2;
		[FieldOffset(3)] public byte b3;
		[FieldOffset(4)] public byte b4;
		[FieldOffset(5)] public byte b5;
		[FieldOffset(6)] public byte b6;
		[FieldOffset(7)] public byte b7;

		[FieldOffset(0)] public byte U1;
		[FieldOffset(0)] public sbyte I1;
		[FieldOffset(0)] public ushort U2;
		[FieldOffset(0)] public short I2;
		[FieldOffset(0)] public uint U4;
		[FieldOffset(0)] public int I4;
		[FieldOffset(0)] public ulong U8;
		[FieldOffset(0)] public long I8;
		[FieldOffset(0)] public float F4;
		[FieldOffset(0)] public double F8;
	}

	static BytesConverter ReadData(byte[] row, int offset, DataType dtype, DataMode data_mode) {
		int p = ((int)dtype) & 3;
		var c = default(BytesConverter);
		if (System.BitConverter.IsLittleEndian != (data_mode == DataMode.LittleEndian)) {
			switch (p) {
			case 0:
				c.b0 = row[offset++];
				break;
			case 1:
				c.b1 = row[offset++];
				c.b0 = row[offset++];
				break;
			case 2:
				c.b3 = row[offset++];
				c.b2 = row[offset++];
				c.b1 = row[offset++];
				c.b0 = row[offset++];
				break;
			case 3:
				c.b7 = row[offset++];
				c.b6 = row[offset++];
				c.b5 = row[offset++];
				c.b4 = row[offset++];
				c.b3 = row[offset++];
				c.b2 = row[offset++];
				c.b1 = row[offset++];
				c.b0 = row[offset++];
				break;
			}
		} else {
			c.b0 = row[offset++];
			if (p > 0) c.b1 = row[offset++];
			if (p > 1) {
				c.b2 = row[offset++];
				c.b3 = row[offset++];
			}
			if (p > 2) {
				c.b4 = row[offset++];
				c.b5 = row[offset++];
				c.b6 = row[offset++];
				c.b7 = row[offset++];
			}
		}
		return c;
	}
	static float ReadFloat(byte[] row, int offset, DataType dtype, DataMode data_mode) {
		var c = ReadData(row, offset, dtype, data_mode);
		switch (dtype) {
		case DataType.U1: return (float)c.U1;
		case DataType.I1: return (float)c.I1;
		case DataType.U2: return (float)c.U2;
		case DataType.I2: return (float)c.I2;
		case DataType.U4: return (float)c.U4;
		case DataType.I4: return (float)c.I4;
		case DataType.U8: return (float)c.U8;
		case DataType.I8: return (float)c.I8;
		case DataType.F4: return (float)c.F4;
		case DataType.F8: return (float)c.F8;
		}
		return 0f;
	}
	static int ReadInt(byte[] row, int offset, DataType dtype, DataMode data_mode) {
		var c = ReadData(row, offset, dtype, data_mode);
		switch (dtype) {
		case DataType.U1: return (int)c.U1;
		case DataType.I1: return (int)c.I1;
		case DataType.U2: return (int)c.U2;
		case DataType.I2: return (int)c.I2;
		case DataType.U4: return (int)c.U4;
		case DataType.I4: return (int)c.I4;
		case DataType.U8: return (int)c.U8;
		case DataType.I8: return (int)c.I8;
		case DataType.F4: return (int)c.F4;
		case DataType.F8: return (int)c.F8;
		}
		return 0;
	}

	// StreamReader internally reads the base stream in blocks, so Position does not reflect
	// the actual position after the last returned character. Unfortunately, StreamReader
	// does not expose a way to figure out this information, so we have to read lines ourselves.
	// Luckily, txt/csv/ply/pcd can be reasonably expected to store all pointcloud-relevant
	// information in plain ASCII (or, at least, utf-8).
	class LineReader {
		Stream stream;
		int pos0=-1, pos1=-2;
		byte[] buffer;
		char[] charbuf;
		StringBuilder sb;
		public LineReader(Stream stream, int buffer_size = 1024) {
			this.stream = stream;
			buffer = new byte[buffer_size];
			charbuf = new char[buffer_size];
			sb = new StringBuilder(buffer_size);
		}
		public Stream BaseStream {
			get { return stream; }
		}
		public bool EndOfStream {
			get { return stream.Position >= stream.Length; }
		}
		public string ReadLine() {
			char c0 = '\0'; bool line_end = false;
			int pos = (int)stream.Position;
			while (!line_end) {
				if ((pos < pos0) | (pos > pos1)) {
					pos0 = pos;
					int len = stream.Read(buffer, 0, buffer.Length);
					pos1 = pos0+len-1;
					if (len <= 0) break; // end of stream
				}
				int i0 = pos - pos0, i1 = i0;
				for (int i = i0; pos <= pos1; ++pos, ++i) {
					char c = (char)buffer[i];
					if (c == '\r') {
						// Macintosh (OS 9) or Win/DOS 1st char
					} else if (c == '\n') {
						// Unix/Mac OS X or Win/DOS 2nd char
						++pos; line_end = true; break;
					} else {
						if (c0 == '\r') {
							// Macintosh (OS 9)
							line_end = true; break;
						}
						charbuf[i] = c;
						i1 = i+1;
					}
					c0 = c;
				}
				sb.Append(charbuf, i0, i1-i0);
				stream.Position = pos;
			}
			string line = sb.ToString();
			sb.Length = 0;
			return line;
		}
	}
	#endregion
}

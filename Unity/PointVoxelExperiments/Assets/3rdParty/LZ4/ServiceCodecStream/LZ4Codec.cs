﻿#region license

/*
Copyright (c) 2013, Milosz Krajewski
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided 
that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this list of conditions 
  and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this list of conditions 
  and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED 
WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR 
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE 
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT 
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN 
IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#endregion

using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace LZ4.ServiceCodecStream
{
	/// <summary>
	/// LZ$ codec selecting best implementation depending on platform.
	/// </summary>
	public static class LZ4Codec
	{
		#region fields

		/// <summary>Encoding service.</summary>
		private static readonly ILZ4Service Encoder;

		/// <summary>Encoding service for HC algorithm.</summary>
		private static readonly ILZ4Service EncoderHC;

		/// <summary>Decoding service.</summary>
		private static readonly ILZ4Service Decoder;

		// ReSharper disable InconsistentNaming

#pragma warning disable 649 // "never assigned to"
		// mixed mode
		private static ILZ4Service _service_MM32;
		private static ILZ4Service _service_MM64;

		// c++/cli
		private static ILZ4Service _service_CC32;
		private static ILZ4Service _service_CC64;

		// unsafe c#
		private static ILZ4Service _service_N32;
		private static ILZ4Service _service_N64;

		// safe c#
		private static ILZ4Service _service_S32;
		private static ILZ4Service _service_S64;
#pragma warning restore 649

		// ReSharper restore InconsistentNaming

		#endregion

		#region initialization

		/// <summary>Initializes the <see cref="LZ4Codec"/> class.</summary>
		static LZ4Codec()
		{
			// NOTE: this method exploits the fact that assemblies are loaded first time they
			// are needed so we can safely try load and handle if not loaded
			// I may change in future versions of .NET

			/*
			Try(InitializeLZ4mm);
			Try(InitializeLZ4cc);
			Try(InitializeLZ4n);
			*/
			Try(InitializeLZ4s);

			// refer to: http://lz4net.codeplex.com/wikipage?title=Performance%20Testing
			// for explanation about this order
			// feel free to change preferred order, just don't do it willy-nilly
			// back it up with some evidence

			if (IntPtr.Size == 4)
			{
				Encoder =
					_service_MM32 ??
					_service_MM64 ??
					_service_N32 ??
					_service_CC32 ??
					_service_N64 ??
					_service_CC64 ??
					_service_S32 ??
					_service_S64;
				Decoder =
					_service_MM32 ??
					_service_MM64 ??
					_service_CC64 ??
					_service_CC32 ??
					_service_N64 ??
					_service_N32 ??
					_service_S64 ??
					_service_S32;
				EncoderHC =
					_service_MM32 ??
					_service_MM64 ??
					_service_N32 ??
					_service_CC32 ??
					_service_N64 ??
					_service_CC64 ??
					_service_S32 ??
					_service_S64;
			}
			else
			{
				Encoder =
					_service_MM64 ??
					_service_MM32 ??
					_service_N64 ??
					_service_N32 ??
					_service_CC64 ??
					_service_CC32 ??
					_service_S32 ??
					_service_S64;
				Decoder =
					_service_MM64 ??
					_service_N64 ??
					_service_N32 ??
					_service_CC64 ??
					_service_MM32 ??
					_service_CC32 ??
					_service_S64 ??
					_service_S32;
				EncoderHC =
					_service_MM64 ??
					_service_MM32 ??
					_service_CC32 ??
					_service_CC64 ??
					_service_N32 ??
					_service_N64 ??
					_service_S32 ??
					_service_S64;
			}

			if (Encoder == null || Decoder == null)
			{
				throw new NotSupportedException("No LZ4 compression service found");
			}
		}

		/// <summary>Tries to execute specified action. Ignores exception if it failed.</summary>
		/// <param name="method">The method.</param>
		private static void Try(Action method)
		{
			try
			{
				method();
			}
				// ReSharper disable EmptyGeneralCatchClause
			catch
			{
				// ignore exception
			}
			// ReSharper restore EmptyGeneralCatchClause
		}

		/// <summary>Tries to create a specified <seealso cref="ILZ4Service"/> and tests it.</summary>
		/// <typeparam name="T">Concrete <seealso cref="ILZ4Service"/> type.</typeparam>
		/// <returns>A service if suceeded or <c>null</c> if it failed.</returns>
		private static ILZ4Service Try<T>()
			where T: ILZ4Service, new()
		{
			try
			{
				return AutoTest(new T());
			}
			catch
			{
				return null;
			}
		}

		/// <summary>Perofrms the quick auto-test on given compression service.</summary>
		/// <param name="service">The service.</param>
		/// <returns>A service or <c>null</c> if it failed.</returns>
		private static ILZ4Service AutoTest(ILZ4Service service)
		{
			const string loremIpsum =
				"Lorem ipsum dolor sit amet, consectetur adipisicing elit, sed do eiusmod tempor incididunt ut " +
				"labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco " +
				"laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in " +
				"voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat " +
				"non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";

			// generate some well-known array of bytes
			const string inputText = loremIpsum + loremIpsum + loremIpsum + loremIpsum + loremIpsum;
			var original = Encoding.UTF8.GetBytes(inputText);

			// LZ4 test
			{
				// compress it
				var encoded = new byte[MaximumOutputLength(original.Length)];
				var encodedLength = service.Encode(original, 0, original.Length, encoded, 0, encoded.Length);
				if (encodedLength < 0) return null;

				// decompress it (knowing original length)
				var decoded = new byte[original.Length];
				var decodedLength1 = service.Decode(encoded, 0, encodedLength, decoded, 0, decoded.Length, true);
				if (decodedLength1 != original.Length) return null;
				var outputText1 = Encoding.UTF8.GetString(decoded);
				if (outputText1 != inputText) return null;

				// decompress it (not knowing original length)
				var decodedLength2 = service.Decode(encoded, 0, encodedLength, decoded, 0, decoded.Length, false);
				if (decodedLength2 != original.Length) return null;
				var outputText2 = Encoding.UTF8.GetString(decoded);
				if (outputText2 != inputText) return null;
			}

			// LZ4HC
			{
				// compress it
				var encoded = new byte[MaximumOutputLength(original.Length)];
				var encodedLength = service.EncodeHC(original, 0, original.Length, encoded, 0, encoded.Length);
				if (encodedLength < 0) return null;

				// decompress it (knowing original length)
				var decoded = new byte[original.Length];
				var decodedLength1 = service.Decode(encoded, 0, encodedLength, decoded, 0, decoded.Length, true);
				if (decodedLength1 != original.Length) return null;
				var outputText1 = Encoding.UTF8.GetString(decoded);
				if (outputText1 != inputText) return null;

				// decompress it (not knowing original length)
				var decodedLength2 = service.Decode(encoded, 0, encodedLength, decoded, 0, decoded.Length, false);
				if (decodedLength2 != original.Length) return null;
				var outputText2 = Encoding.UTF8.GetString(decoded);
				if (outputText2 != inputText) return null;
			}

			return service;
		}

		// ReSharper disable InconsistentNaming

		/*
		/// <summary>Initializes codecs from LZ4mm.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InitializeLZ4mm()
		{
			_service_MM32 = Try<CppMM32LZ4Service>();
			_service_MM64 = Try<CppMM64LZ4Service>();
		}

		/// <summary>Initializes codecs from LZ4cc.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InitializeLZ4cc()
		{
			_service_CC32 = Try<CppCC32LZ4Service>();
			_service_CC64 = Try<CppCC64LZ4Service>();
		}

		/// <summary>Initializes codecs from LZ4n.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InitializeLZ4n()
		{
			_service_N32 = Try<Unsafe32LZ4Service>();
			_service_N64 = Try<Unsafe64LZ4Service>();
		}
		*/

		/// <summary>Initializes codecs from LZ4s.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InitializeLZ4s()
		{
			_service_S32 = Try<Safe32LZ4Service>();
			_service_S64 = Try<Safe64LZ4Service>();
		}

		// ReSharper restore InconsistentNaming

		#endregion

		#region public interface

		/// <summary>Gets the name of selected codec(s).</summary>
		/// <value>The name of the codec.</value>
		public static string CodecName
		{
			get
			{
				return string.Format(
					"{0}/{1}/{2}HC",
					Encoder == null ? "<none>" : Encoder.CodecName,
					Decoder == null ? "<none>" : Decoder.CodecName,
					EncoderHC == null ? "<none>" : EncoderHC.CodecName);
			}
		}

		/// <summary>Get maximum output length.</summary>
		/// <param name="inputLength">Input length.</param>
		/// <returns>Output length.</returns>
		public static int MaximumOutputLength(int inputLength)
		{
			return inputLength + (inputLength/255) + 16;
		}

		#region Encode

		/// <summary>Encodes the specified input.</summary>
		/// <param name="input">The input.</param>
		/// <param name="inputOffset">The input offset.</param>
		/// <param name="inputLength">Length of the input.</param>
		/// <param name="output">The output.</param>
		/// <param name="outputOffset">The output offset.</param>
		/// <param name="outputLength">Length of the output.</param>
		/// <returns>Number of bytes written.</returns>
		public static int Encode(
			byte[] input,
			int inputOffset,
			int inputLength,
			byte[] output,
			int outputOffset,
			int outputLength)
		{
			return Encoder.Encode(input, inputOffset, inputLength, output, outputOffset, outputLength);
		}

		/// <summary>Encodes the specified input.</summary>
		/// <param name="input">The input.</param>
		/// <param name="inputOffset">The input offset.</param>
		/// <param name="inputLength">Length of the input.</param>
		/// <returns>Compressed buffer.</returns>
		public static byte[] Encode(byte[] input, int inputOffset, int inputLength)
		{
			if (inputLength < 0) inputLength = input.Length - inputOffset;

			if (input == null) throw new ArgumentNullException("input");
			if (inputOffset < 0 || inputOffset + inputLength > input.Length)
				throw new ArgumentException("inputOffset and inputLength are invalid for given input");

			var result = new byte[MaximumOutputLength(inputLength)];
			var length = Encode(input, inputOffset, inputLength, result, 0, result.Length);

			if (length != result.Length)
			{
				if (length < 0)
					throw new InvalidOperationException("Compression has been corrupted");
				var buffer = new byte[length];
				Buffer.BlockCopy(result, 0, buffer, 0, length);
				return buffer;
			}
			return result;
		}

		/// <summary>Encodes the specified input.</summary>
		/// <param name="input">The input.</param>
		/// <param name="inputOffset">The input offset.</param>
		/// <param name="inputLength">Length of the input.</param>
		/// <param name="output">The output.</param>
		/// <param name="outputOffset">The output offset.</param>
		/// <param name="outputLength">Length of the output.</param>
		/// <returns>Number of bytes written.</returns>
		public static int EncodeHC(
			byte[] input,
			int inputOffset,
			int inputLength,
			byte[] output,
			int outputOffset,
			int outputLength)
		{
			return (EncoderHC ?? Encoder)
				.EncodeHC(input, inputOffset, inputLength, output, outputOffset, outputLength);
		}

		/// <summary>Encodes the specified input.</summary>
		/// <param name="input">The input.</param>
		/// <param name="inputOffset">The input offset.</param>
		/// <param name="inputLength">Length of the input.</param>
		/// <returns>Compressed buffer.</returns>
		public static byte[] EncodeHC(byte[] input, int inputOffset, int inputLength)
		{
			if (inputLength < 0) inputLength = input.Length - inputOffset;

			if (input == null) throw new ArgumentNullException("input");
			if (inputOffset < 0 || inputOffset + inputLength > input.Length)
				throw new ArgumentException("inputOffset and inputLength are invalid for given input");

			var result = new byte[MaximumOutputLength(inputLength)];
			var length = EncodeHC(input, inputOffset, inputLength, result, 0, result.Length);

			if (length != result.Length)
			{
				if (length < 0)
					throw new InvalidOperationException("Compression has been corrupted");
				var buffer = new byte[length];
				Buffer.BlockCopy(result, 0, buffer, 0, length);
				return buffer;
			}
			return result;
		}

		#endregion

		#region Decode

		/// <summary>Decodes the specified input.</summary>
		/// <param name="input">The input.</param>
		/// <param name="inputOffset">The input offset.</param>
		/// <param name="inputLength">Length of the input.</param>
		/// <param name="output">The output.</param>
		/// <param name="outputOffset">The output offset.</param>
		/// <param name="outputLength">Length of the output.</param>
		/// <param name="knownOutputLength">Set it to <c>true</c> if output length is known.</param>
		/// <returns>Number of bytes written.</returns>
		public static int Decode(
			byte[] input,
			int inputOffset,
			int inputLength,
			byte[] output,
			int outputOffset,
			int outputLength = 0,
			bool knownOutputLength = false)
		{
			return Decoder.Decode(input, inputOffset, inputLength, output, outputOffset, outputLength, knownOutputLength);
		}

		/// <summary>Decodes the specified input.</summary>
		/// <param name="input">The input.</param>
		/// <param name="inputOffset">The input offset.</param>
		/// <param name="inputLength">Length of the input.</param>
		/// <param name="outputLength">Length of the output.</param>
		/// <returns>Decompressed buffer.</returns>
		public static byte[] Decode(byte[] input, int inputOffset, int inputLength, int outputLength)
		{
			if (inputLength < 0) inputLength = input.Length - inputOffset;

			if (input == null) throw new ArgumentNullException("input");
			if (inputOffset < 0 || inputOffset + inputLength > input.Length)
				throw new ArgumentException("inputOffset and inputLength are invalid for given input");

			var result = new byte[outputLength];
			var length = Decode(input, inputOffset, inputLength, result, 0, outputLength, true);
			if (length != outputLength)
				throw new ArgumentException("outputLength is not valid");
			return result;
		}

		#endregion

		#endregion
	}
}

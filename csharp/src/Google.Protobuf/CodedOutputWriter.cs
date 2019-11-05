#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
// https://developers.google.com/protocol-buffers/
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//     * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//     * Neither the name of Google Inc. nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
#endregion

#if GOOGLE_PROTOBUF_SUPPORT_SPAN
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Google.Protobuf
{
    /// <summary>
    /// Encodes and writes protocol message fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is generally used by generated code to write appropriate
    /// primitives to the stream. It effectively encapsulates the lowest
    /// levels of protocol buffer format. Unlike some other implementations,
    /// this does not include combined "write tag and value" methods. Generated
    /// code knows the exact byte representations of the tags they're going to write,
    /// so there's no need to re-encode them each time. Manually-written code calling
    /// this class should just call one of the <c>WriteTag</c> overloads before each value.
    /// </para>
    /// <para>
    /// Repeated fields and map fields are not handled by this class; use <c>RepeatedField&lt;T&gt;</c>
    /// and <c>MapField&lt;TKey, TValue&gt;</c> to serialize such fields.
    /// </para>
    /// </remarks>
    public ref struct CodedOutputWriter
    {
        // "Local" copy of Encoding.UTF8, for efficiency. (Yes, it makes a difference.)
        internal static readonly Encoding Utf8Encoding = Encoding.UTF8;

        private BufferWriter writer;
        private Encoder encoder;

        public CodedOutputWriter(IBufferWriter<byte> writer)
        {
            this.writer = new BufferWriter(writer);
            this.encoder = null;
        }

        #region Writing of values (not including tags)

        /// <summary>
        /// Writes a double field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteDouble(double value)
        {
            WriteRawLittleEndian64((ulong)BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>
        /// Writes a float field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteFloat(float value)
        {
            var floatSpan = writer.GetSpan(sizeof(float));

            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(floatSpan), value);

            if (!BitConverter.IsLittleEndian)
            {
                floatSpan.Reverse();
            }

            writer.Write(floatSpan);
        }

        /// <summary>
        /// Writes a uint64 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteUInt64(ulong value)
        {
            WriteRawVarint64(value);
        }

        /// <summary>
        /// Writes an int64 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteInt64(long value)
        {
            WriteRawVarint64((ulong) value);
        }

        /// <summary>
        /// Writes an int32 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteInt32(int value)
        {
            if (value >= 0)
            {
                WriteRawVarint32((uint) value);
            }
            else
            {
                // Must sign-extend.
                WriteRawVarint64((ulong) value);
            }
        }

        /// <summary>
        /// Writes a fixed64 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteFixed64(ulong value)
        {
            WriteRawLittleEndian64(value);
        }

        /// <summary>
        /// Writes a fixed32 field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteFixed32(uint value)
        {
            WriteRawLittleEndian32(value);
        }

        /// <summary>
        /// Writes a bool field value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteBool(bool value)
        {
            WriteRawByte(value ? (byte) 1 : (byte) 0);
        }

        /// <summary>
        /// Writes a string field value, without a tag, to the stream.
        /// The data is length-prefixed.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteString(string value)
        {
            int length = Utf8Encoding.GetByteCount(value);
            WriteLength(length);

            Span<byte> buffer = writer.GetSpan(length);

            if (buffer.Length >= length)
            {
                // Can write string to a single buffer

                if (length == value.Length) // Must be all ASCII...
                {
                    for (int i = 0; i < length; i++)
                    {
                        buffer[i] = (byte)value[i];
                    }

                    writer.Advance(length);
                }
                else
                {
                    ReadOnlySpan<char> source = value.AsSpan();
                    int bytesUsed = Utf8Encoding.GetBytes(source, buffer);
                    writer.Advance(bytesUsed);
                }
            }
            else
            {
                // The destination byte array might not be large enough so multiple writes are sometimes required
                if (encoder == null)
                {
                    encoder = Utf8Encoding.GetEncoder();
                }

                ReadOnlySpan<char> source = value.AsSpan();
                int written = 0;

                while (true)
                {
                    int bytesUsed;
                    int charsUsed;
                    encoder.Convert(source, buffer, false, out charsUsed, out bytesUsed, out _);

                    source = source.Slice(charsUsed);
                    written += bytesUsed;

                    writer.Advance(bytesUsed);

                    if (source.Length == 0)
                    {
                        break;
                    }

                    buffer = writer.GetSpan(length - written);
                }

            }
        }

        /// <summary>
        /// Writes a message, without a tag, to the stream.
        /// The data is length-prefixed.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteMessage(IBufferMessage value)
        {
            WriteLength(value.CalculateSize());
            value.WriteTo(ref this);
        }

        /// <summary>
        /// Writes a group, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteGroup(IBufferMessage value)
        {
            value.WriteTo(ref this);
        }

        /// <summary>
        /// Write a byte string, without a tag, to the stream.
        /// The data is length-prefixed.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteBytes(ByteString value)
        {
            WriteLength(value.Length);

            writer.Write(value.Span);
        }

        /// <summary>
        /// Writes a uint32 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteUInt32(uint value)
        {
            WriteRawVarint32(value);
        }

        /// <summary>
        /// Writes an enum value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteEnum(int value)
        {
            WriteInt32(value);
        }

        /// <summary>
        /// Writes an sfixed32 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteSFixed32(int value)
        {
            WriteRawLittleEndian32((uint) value);
        }

        /// <summary>
        /// Writes an sfixed64 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteSFixed64(long value)
        {
            WriteRawLittleEndian64((ulong) value);
        }

        /// <summary>
        /// Writes an sint32 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteSInt32(int value)
        {
            WriteRawVarint32(EncodeZigZag32(value));
        }

        /// <summary>
        /// Writes an sint64 value, without a tag, to the stream.
        /// </summary>
        /// <param name="value">The value to write</param>
        public void WriteSInt64(long value)
        {
            WriteRawVarint64(EncodeZigZag64(value));
        }

        /// <summary>
        /// Writes a length (in bytes) for length-delimited data.
        /// </summary>
        /// <remarks>
        /// This method simply writes a rawint, but exists for clarity in calling code.
        /// </remarks>
        /// <param name="length">Length value, in bytes.</param>
        public void WriteLength(int length)
        {
            WriteRawVarint32((uint) length);
        }

        #endregion

        #region Raw tag writing
        /// <summary>
        /// Encodes and writes a tag.
        /// </summary>
        /// <param name="fieldNumber">The number of the field to write the tag for</param>
        /// <param name="type">The wire format type of the tag to write</param>
        public void WriteTag(int fieldNumber, WireFormat.WireType type)
        {
            WriteRawVarint32(WireFormat.MakeTag(fieldNumber, type));
        }

        /// <summary>
        /// Writes an already-encoded tag.
        /// </summary>
        /// <param name="tag">The encoded tag</param>
        public void WriteTag(uint tag)
        {
            WriteRawVarint32(tag);
        }

        /// <summary>
        /// Writes the given single-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The encoded tag</param>
        public void WriteRawTag(byte b1)
        {
            var span = writer.GetSpan(1);
            span[0] = b1;
            writer.Advance(1);
        }

        /// <summary>
        /// Writes the given two-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2)
        {
            var span = writer.GetSpan(2);
            span[1] = b2;
            span[0] = b1;
            writer.Advance(2);
        }

        /// <summary>
        /// Writes the given three-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        /// <param name="b3">The third byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2, byte b3)
        {
            var span = writer.GetSpan(3);
            span[2] = b3;
            span[1] = b2;
            span[0] = b1;
            writer.Advance(3);
        }

        /// <summary>
        /// Writes the given four-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        /// <param name="b3">The third byte of the encoded tag</param>
        /// <param name="b4">The fourth byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2, byte b3, byte b4)
        {
            var span = writer.GetSpan(4);
            span[3] = b4;
            span[2] = b3;
            span[1] = b2;
            span[0] = b1;
            writer.Advance(4);
        }

        /// <summary>
        /// Writes the given five-byte tag directly to the stream.
        /// </summary>
        /// <param name="b1">The first byte of the encoded tag</param>
        /// <param name="b2">The second byte of the encoded tag</param>
        /// <param name="b3">The third byte of the encoded tag</param>
        /// <param name="b4">The fourth byte of the encoded tag</param>
        /// <param name="b5">The fifth byte of the encoded tag</param>
        public void WriteRawTag(byte b1, byte b2, byte b3, byte b4, byte b5)
        {
            var span = writer.GetSpan(5);
            span[4] = b5;
            span[3] = b4;
            span[2] = b3;
            span[1] = b2;
            span[0] = b1;
            writer.Advance(5);
        }
        #endregion

        #region Underlying writing primitives
        /// <summary>
        /// Writes a 32 bit value as a varint. The fast route is taken when
        /// there's enough buffer space left to whizz through without checking
        /// for each byte; otherwise, we resort to calling WriteRawByte each time.
        /// </summary>
        internal void WriteRawVarint32(uint value)
        {
            // Optimize for the common case of a single byte value
            if (value < 128)
            {
                var buffer = writer.GetSpan(1);
                buffer[0] = (byte)value;
                writer.Advance(1);
            }
            else
            {
                var buffer = writer.GetSpan(4);
                var position = 0;

                while (value > 127)
                {
                    buffer[position++] = (byte)((value & 0x7F) | 0x80);
                    value >>= 7;
                }

                buffer[position++] = (byte)value;

                writer.Advance(position);
            }
        }

        internal void WriteRawVarint64(ulong value)
        {
            // Optimize for the common case of a single byte value
            if (value < 128)
            {
                var buffer = writer.GetSpan(1);
                buffer[0] = (byte)value;
                writer.Advance(1);
            }
            else
            {
                var buffer = writer.GetSpan(8);
                var position = 0;

                while (value > 127)
                {
                    buffer[position++] = (byte)((value & 0x7F) | 0x80);
                    value >>= 7;
                }

                buffer[position++] = (byte)value;

                writer.Advance(position);
            }
        }

        internal void WriteRawLittleEndian32(uint value)
        {
            var buffer = writer.GetSpan(4);

            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);

            writer.Advance(4);
        }

        internal void WriteRawLittleEndian64(ulong value)
        {
            var buffer = writer.GetSpan(8);

            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);

            writer.Advance(8);
        }

        internal void WriteRawByte(byte value)
        {
            var buffer = writer.GetSpan(1);
            buffer[0] = value;
            writer.Advance(1);
        }

        /// <summary>
        /// Writes out an array of bytes.
        /// </summary>
        internal void WriteRawBytes(byte[] value)
        {
            WriteRawBytes(value, 0, value.Length);
        }

        /// <summary>
        /// Writes out part of an array of bytes.
        /// </summary>
        internal void WriteRawBytes(byte[] value, int offset, int length)
        {
            writer.Write(value.AsSpan(offset, length));
        }

        #endregion

        /// <summary>
        /// Encode a 32-bit value with ZigZag encoding.
        /// </summary>
        /// <remarks>
        /// ZigZag encodes signed integers into values that can be efficiently
        /// encoded with varint.  (Otherwise, negative values must be 
        /// sign-extended to 64 bits to be varint encoded, thus always taking
        /// 10 bytes on the wire.)
        /// </remarks>
        internal static uint EncodeZigZag32(int n)
        {
            // Note:  the right-shift must be arithmetic
            return (uint) ((n << 1) ^ (n >> 31));
        }

        /// <summary>
        /// Encode a 64-bit value with ZigZag encoding.
        /// </summary>
        /// <remarks>
        /// ZigZag encodes signed integers into values that can be efficiently
        /// encoded with varint.  (Otherwise, negative values must be 
        /// sign-extended to 64 bits to be varint encoded, thus always taking
        /// 10 bytes on the wire.)
        /// </remarks>
        internal static ulong EncodeZigZag64(long n)
        {
            return (ulong) ((n << 1) ^ (n >> 63));
        }

        public void Flush()
        {
            writer.Commit();
        }
    }
}
#endif
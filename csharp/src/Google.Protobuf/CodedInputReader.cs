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
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Google.Protobuf
{
    // TODO: class name TBD (CodedInputReader, CodedInputContext, CodedInputByRefReader, WireFormatReader)
    public ref struct CodedInputReader
    {
        private const int recursionLimit = 100;

        private SequenceReader<byte> reader;
        private uint lastTag;
        private int recursionDepth;
        private long currentLimit;

        public CodedInputReader(ReadOnlySequence<byte> input)
        {
            reader = new SequenceReader<byte>(input);
            lastTag = 0;
            recursionDepth = 0;
            currentLimit = long.MaxValue;
            DiscardUnknownFields = false;
        }

        public ReadOnlySequence<byte> Sequence => reader.Sequence;

        public long Position => reader.Consumed;

        public bool IsAtEnd => reader.End;

        /// <summary>
        /// Returns the last tag read, or 0 if no tags have been read or we've read beyond
        /// the end of the stream.
        /// </summary>
        internal uint LastTag { get { return lastTag; } }

        /// <summary>
        /// Internal-only property; when set to true, unknown fields will be discarded while parsing.
        /// </summary>
        internal bool DiscardUnknownFields { get; set; }

#region Reading of tags etc

        /// <summary>
        /// Peeks at the next field tag. This is like calling <see cref="ReadTag"/>, but the
        /// tag is not consumed. (So a subsequent call to <see cref="ReadTag"/> will return the
        /// same value.)
        /// </summary>
        public uint PeekTag()
        {
            uint previousTag = lastTag;
            long consumed = reader.Consumed;

            uint tag = ReadTag();

            long rewindCount = reader.Consumed - consumed;
            if (rewindCount > 0)
            {
                reader.Rewind(rewindCount);
                lastTag = previousTag;
            }

            return tag;
        }

        private void ThrowEndOfStreamUnless(bool condition)
        {
            if (!condition)
            {
                throw InvalidProtocolBufferException.TruncatedMessage();
            }
        }

        /// <summary>
        /// Reads a field tag, returning the tag of 0 for "end of stream".
        /// </summary>
        /// <remarks>
        /// If this method returns 0, it doesn't necessarily mean the end of all
        /// the data in this CodedInputStream; it may be the end of the logical stream
        /// for an embedded message, for example.
        /// </remarks>
        /// <returns>The next field tag, or 0 for end of stream. (0 is never a valid tag.)</returns>
        public uint ReadTag()
        {
            uint tag;
            byte value;

            bool hasValue = reader.TryRead(out value);
            if (!hasValue)
            {
                ThrowEndOfStreamUnless(IsAtEnd);

                // End of stream
                lastTag = 0;
                return 0;
            }

            if (value < 128)
            {
                tag = value;
            }
            else
            {
                int result = value & 0x7f;
                ThrowEndOfStreamUnless(reader.TryRead(out value));
                if (value < 128)
                {
                    result |= value << 7;
                    tag = (uint)result;
                }
                else
                {
                    result = (value & 0x7f) << 7;
                    ThrowEndOfStreamUnless(reader.TryRead(out value));
                    if (value < 128)
                    {
                        result |= value << 14;
                        tag = (uint)result;
                    }
                    else
                    {
                        result = (value & 0x7f) << 14;
                        ThrowEndOfStreamUnless(reader.TryRead(out value));
                        result |= value << 21;
                        tag = (uint)result;
                    }
                }
            }

            if (WireFormat.GetTagFieldNumber(tag) == 0)
            {
                // If we actually read a tag with a field of 0, that's not a valid tag.
                throw InvalidProtocolBufferException.InvalidTag();
            }

            lastTag = tag;

            return tag;
        }

        /// <summary>
        /// Skips the data for the field with the tag we've just read.
        /// This should be called directly after <see cref="ReadTag"/>, when
        /// the caller wishes to skip an unknown field.
        /// </summary>
        /// <remarks>
        /// This method throws <see cref="InvalidProtocolBufferException"/> if the last-read tag was an end-group tag.
        /// If a caller wishes to skip a group, they should skip the whole group, by calling this method after reading the
        /// start-group tag. This behavior allows callers to call this method on any field they don't understand, correctly
        /// resulting in an error if an end-group tag has not been paired with an earlier start-group tag.
        /// </remarks>
        /// <exception cref="InvalidProtocolBufferException">The last tag was an end-group tag</exception>
        /// <exception cref="InvalidOperationException">The last read operation read to the end of the logical stream</exception>
        public void SkipLastField()
        {
            if (lastTag == 0)
            {
                throw new InvalidOperationException("SkipLastField cannot be called at the end of a stream");
            }
            switch (WireFormat.GetTagWireType(lastTag))
            {
                case WireFormat.WireType.StartGroup:
                    SkipGroup(lastTag);
                    break;
                case WireFormat.WireType.EndGroup:
                    throw new InvalidProtocolBufferException(
                        "SkipLastField called on an end-group tag, indicating that the corresponding start-group was missing");
                case WireFormat.WireType.Fixed32:
                    ReadFixed32();
                    break;
                case WireFormat.WireType.Fixed64:
                    ReadFixed64();
                    break;
                case WireFormat.WireType.LengthDelimited:
                    var length = ReadLength();
                    ThrowEndOfStreamUnless(reader.Remaining >= length);
                    reader.Advance(length);
                    break;
                case WireFormat.WireType.Varint:
                    ReadRawVarint32();
                    break;
            }
        }

        /// <summary>
        /// Skip a group.
        /// </summary>
        internal void SkipGroup(uint startGroupTag)
        {
            // Note: Currently we expect this to be the way that groups are read. We could put the recursion
            // depth changes into the ReadTag method instead, potentially...
            recursionDepth++;
            if (recursionDepth >= recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            uint tag;
            while (true)
            {
                tag = ReadTag();
                if (tag == 0)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                // Can't call SkipLastField for this case- that would throw.
                if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.EndGroup)
                {
                    break;
                }
                // This recursion will allow us to handle nested groups.
                SkipLastField();
            }
            int startField = WireFormat.GetTagFieldNumber(startGroupTag);
            int endField = WireFormat.GetTagFieldNumber(tag);
            if (startField != endField)
            {
                throw new InvalidProtocolBufferException(
                    $"Mismatched end-group tag. Started with field {startField}; ended with field {endField}");
            }
            recursionDepth--;
        }

        /// <summary>
        /// Reads a double field from the stream.
        /// </summary>
        public double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble((long)ReadRawLittleEndian64());
        }

        /// <summary>
        /// Reads a float field from the stream.
        /// </summary>
        public unsafe float ReadFloat()
        {
            // Cannot create a span directly since it gets passed to instance methods on a ref struct.
            byte* buffer = stackalloc byte[sizeof(float)];
            Span<byte> tempSpan = new Span<byte>(buffer, sizeof(float));

            ThrowEndOfStreamUnless(reader.TryCopyTo(tempSpan));
            reader.Advance(sizeof(float));

            return Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(tempSpan));
        }

        /// <summary>
        /// Reads a uint64 field from the stream.
        /// </summary>
        public ulong ReadUInt64()
        {
            return ReadRawVarint64();
        }

        /// <summary>
        /// Reads an int64 field from the stream.
        /// </summary>
        public long ReadInt64()
        {
            return (long)ReadRawVarint64();
        }

        /// <summary>
        /// Reads an int32 field from the stream.
        /// </summary>
        public int ReadInt32()
        {
            return (int)ReadRawVarint32();
        }

        /// <summary>
        /// Reads a fixed64 field from the stream.
        /// </summary>
        public ulong ReadFixed64()
        {
            return ReadRawLittleEndian64();
        }

        /// <summary>
        /// Reads a fixed32 field from the stream.
        /// </summary>
        public uint ReadFixed32()
        {
            return ReadRawLittleEndian32();
        }

        /// <summary>
        /// Reads a bool field from the stream.
        /// </summary>
        public bool ReadBool()
        {
            return ReadRawVarint32() != 0;
        }

        /// <summary>
        /// Reads a string field from the stream.
        /// </summary>
        public unsafe string ReadString()
        {
            int length = ReadLength();

            if (length == 0)
            {
                return string.Empty;
            }

            if (length < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            ReadOnlySpan<byte> unreadSpan = reader.UnreadSpan;
            if (unreadSpan.Length >= length)
            {
                // Fast path: all bytes to decode appear in the same span.
                ReadOnlySpan<byte> data = unreadSpan.Slice(0, length);

                string value;
                fixed (byte* sourceBytes = &MemoryMarshal.GetReference(data))
                {
                    value = CodedOutputStream.Utf8Encoding.GetString(sourceBytes, length);
                }

                reader.Advance(length);
                return value;
            }
            else
            {
                return ReadStringSlow(length);
            }
        }

        /// <summary>
        /// Reads a string assuming that it is spread across multiple spans in the <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <param name="byteLength">The length of the string to be decoded, in bytes.</param>
        /// <returns>The decoded string.</returns>
        private string ReadStringSlow(int byteLength)
        {
            ThrowEndOfStreamUnless(reader.Remaining >= byteLength);

            // We need to decode bytes incrementally across multiple spans.
            int maxCharLength = CodedOutputStream.Utf8Encoding.GetMaxCharCount(byteLength);
            char[] charArray = ArrayPool<char>.Shared.Rent(maxCharLength);
            var decoder = CodedOutputStream.Utf8Encoding.GetDecoder();

            int remainingByteLength = byteLength;
            int initializedChars = 0;
            while (remainingByteLength > 0)
            {
                int bytesRead = Math.Min(remainingByteLength, this.reader.UnreadSpan.Length);
                remainingByteLength -= bytesRead;
                bool flush = remainingByteLength == 0;

                unsafe
                {
                    fixed (byte* pUnreadSpan = reader.UnreadSpan)
                    fixed (char* pCharArray = &charArray[initializedChars])
                    {
                        initializedChars += decoder.GetChars(pUnreadSpan, bytesRead, pCharArray, charArray.Length - initializedChars, flush);
                    }
                }
            }

            string value = new string(charArray, 0, initializedChars);
            ArrayPool<char>.Shared.Return(charArray);
            return value;
        }

        /// <summary>
        /// Reads an embedded message field value from the stream.
        /// </summary>   
        public void ReadMessage(IBufferMessage builder)
        {
            int length = ReadLength();
            if (recursionDepth >= recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }

            ++recursionDepth;
            builder.MergeFrom(ref this);
            CheckReadEndOfStreamTag();

            --recursionDepth;
        }

        /// <summary>
        /// Reads an embedded group field from the stream.
        /// </summary>
        public void ReadGroup(IBufferMessage builder)
        {
            if (recursionDepth >= recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            ++recursionDepth;
            builder.MergeFrom(ref this);
            --recursionDepth;
        }

        /// <summary>
        /// Reads a bytes field value from the stream.
        /// </summary>   
        public ByteString ReadBytes()
        {
            int length = ReadLength();

            ThrowEndOfStreamUnless(reader.Remaining >= length);

            if (length < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            var data = reader.Sequence.Slice(reader.Position, length);

            reader.Advance(length);

            return ByteString.AttachBytes(data.ToArray());
        }

        /// <summary>
        /// Reads a uint32 field value from the stream.
        /// </summary>   
        public uint ReadUInt32()
        {
            return ReadRawVarint32();
        }

        /// <summary>
        /// Reads an enum field value from the stream.
        /// </summary>   
        public int ReadEnum()
        {
            // Currently just a pass-through, but it's nice to separate it logically from WriteInt32.
            return (int)ReadRawVarint32();
        }

        /// <summary>
        /// Reads an sfixed32 field value from the stream.
        /// </summary>   
        public int ReadSFixed32()
        {
            return (int)ReadRawLittleEndian32();
        }

        /// <summary>
        /// Reads an sfixed64 field value from the stream.
        /// </summary>   
        public long ReadSFixed64()
        {
            return (long)ReadRawLittleEndian64();
        }

        /// <summary>
        /// Reads an sint32 field value from the stream.
        /// </summary>   
        public int ReadSInt32()
        {
            return DecodeZigZag32(ReadRawVarint32());
        }

        /// <summary>
        /// Reads an sint64 field value from the stream.
        /// </summary>   
        public long ReadSInt64()
        {
            return DecodeZigZag64(ReadRawVarint64());
        }

        /// <summary>
        /// Reads a length for length-delimited data.
        /// </summary>
        /// <remarks>
        /// This is internally just reading a varint, but this method exists
        /// to make the calling code clearer.
        /// </remarks>
        public int ReadLength()
        {
            return (int)ReadRawVarint32();
        }

        /// <summary>
        /// Peeks at the next tag in the stream. If it matches <paramref name="tag"/>,
        /// the tag is consumed and the method returns <c>true</c>; otherwise, the
        /// stream is left in the original position and the method returns <c>false</c>.
        /// </summary>
        public bool MaybeConsumeTag(uint tag)
        {
            return PeekTag() == tag;
        }

#endregion

#region Underlying reading primitives
        
        /// <summary>
        /// Reads a raw Varint from the stream.  If larger than 32 bits, discard the upper bits.
        /// This method is optimised for the case where we've got lots of data in the buffer.
        /// That means we can check the size just once, then just read directly from the buffer
        /// without constant rechecking of the buffer length.
        /// </summary>
        internal uint ReadRawVarint32()
        {
            byte value;

            ThrowEndOfStreamUnless(reader.TryRead(out value));
            int tmp = value;
            if (tmp < 128)
            {
                return (uint)tmp;
            }
            int result = tmp & 0x7f;
            ThrowEndOfStreamUnless(reader.TryRead(out value));
            tmp = value;
            if (tmp < 128)
            {
                result |= tmp << 7;
            }
            else
            {
                result |= (tmp & 0x7f) << 7;
                ThrowEndOfStreamUnless(reader.TryRead(out value));
                tmp = value;
                if (tmp < 128)
                {
                    result |= tmp << 14;
                }
                else
                {
                    result |= (tmp & 0x7f) << 14;
                    ThrowEndOfStreamUnless(reader.TryRead(out value));
                    tmp = value;
                    if (tmp < 128)
                    {
                        result |= tmp << 21;
                    }
                    else
                    {
                        result |= (tmp & 0x7f) << 21;
                        ThrowEndOfStreamUnless(reader.TryRead(out value));
                        tmp = value;
                        result |= tmp << 28;
                        if (tmp >= 128)
                        {
                            // Discard upper 32 bits.
                            // Note that this has to use ReadRawByte() as we only ensure we've
                            // got at least 5 bytes at the start of the method. This lets us
                            // use the fast path in more cases, and we rarely hit this section of code.
                            for (int i = 0; i < 5; i++)
                            {
                                ThrowEndOfStreamUnless(reader.TryRead(out value));
                                tmp = value;
                                if (tmp < 128)
                                {
                                    return (uint)result;
                                }
                            }
                            throw InvalidProtocolBufferException.MalformedVarint();
                        }
                    }
                }
            }
            return (uint)result;
        }

        /// <summary>
        /// Reads a varint from the input one byte at a time, so that it does not
        /// read any bytes after the end of the varint. If you simply wrapped the
        /// stream in a CodedInputStream and used ReadRawVarint32(Stream)
        /// then you would probably end up reading past the end of the varint since
        /// CodedInputStream buffers its input.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        internal static uint ReadRawVarint32(Stream input)
        {
            int result = 0;
            int offset = 0;
            for (; offset < 32; offset += 7)
            {
                int b = input.ReadByte();
                if (b == -1)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                result |= (b & 0x7f) << offset;
                if ((b & 0x80) == 0)
                {
                    return (uint)result;
                }
            }
            // Keep reading up to 64 bits.
            for (; offset < 64; offset += 7)
            {
                int b = input.ReadByte();
                if (b == -1)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                if ((b & 0x80) == 0)
                {
                    return (uint)result;
                }
            }
            throw InvalidProtocolBufferException.MalformedVarint();
        }

        /// <summary>
        /// Reads a raw varint from the stream.
        /// </summary>
        internal ulong ReadRawVarint64()
        {
            int shift = 0;
            ulong result = 0;
            while (shift < 64)
            {
                ThrowEndOfStreamUnless(reader.TryRead(out byte b));
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
                shift += 7;
            }
            throw InvalidProtocolBufferException.MalformedVarint();
        }

        /// <summary>
        /// Reads a 32-bit little-endian integer from the stream.
        /// </summary>
        internal unsafe uint ReadRawLittleEndian32()
        {
            byte* buffer = stackalloc byte[4];
            Span<byte> tempSpan = new Span<byte>(buffer, 4);

            ThrowEndOfStreamUnless(reader.TryCopyTo(tempSpan));
            reader.Advance(4);

            return BinaryPrimitives.ReadUInt32LittleEndian(tempSpan);
        }

        /// <summary>
        /// Reads a 64-bit little-endian integer from the stream.
        /// </summary>
        internal unsafe ulong ReadRawLittleEndian64()
        {
            byte* buffer = stackalloc byte[8];
            Span<byte> tempSpan = new Span<byte>(buffer, 8);

            ThrowEndOfStreamUnless(reader.TryCopyTo(tempSpan));
            reader.Advance(8);

            return BinaryPrimitives.ReadUInt64LittleEndian(tempSpan);
        }

        /// <summary>
        /// Decode a 32-bit value with ZigZag encoding.
        /// </summary>
        /// <remarks>
        /// ZigZag encodes signed integers into values that can be efficiently
        /// encoded with varint.  (Otherwise, negative values must be 
        /// sign-extended to 64 bits to be varint encoded, thus always taking
        /// 10 bytes on the wire.)
        /// </remarks>
        internal static int DecodeZigZag32(uint n)
        {
            return (int)(n >> 1) ^ -(int)(n & 1);
        }

        /// <summary>
        /// Decode a 32-bit value with ZigZag encoding.
        /// </summary>
        /// <remarks>
        /// ZigZag encodes signed integers into values that can be efficiently
        /// encoded with varint.  (Otherwise, negative values must be 
        /// sign-extended to 64 bits to be varint encoded, thus always taking
        /// 10 bytes on the wire.)
        /// </remarks>
        internal static long DecodeZigZag64(ulong n)
        {
            return (long)(n >> 1) ^ -(long)(n & 1);
        }
#endregion

        /// <summary>
        /// Sets currentLimit to (current position) + byteLimit. This is called
        /// when descending into a length-delimited embedded message. The previous
        /// limit is returned.
        /// </summary>
        /// <returns>The old limit.</returns>
        internal long PushLimit(long byteLimit)
        {
            if (byteLimit < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }
            
            byteLimit += reader.Consumed;
            long oldLimit = currentLimit;
            if (byteLimit > oldLimit)
            {
                throw InvalidProtocolBufferException.TruncatedMessage();
            }
            currentLimit = byteLimit;

            return oldLimit;
        }

        /// <summary>
        /// Discards the current limit, returning the previous limit.
        /// </summary>
        internal void PopLimit(long oldLimit)
        {
            currentLimit = oldLimit;
        }

        /// <summary>
        /// Returns whether or not all the data before the limit has been read.
        /// </summary>
        /// <returns></returns>
        internal bool ReachedLimit
        {
            get
            {
                if (currentLimit == long.MaxValue)
                {
                    return false;
                }
                return reader.Consumed >= currentLimit;
            }
        }

        /// <summary>
        /// Verifies that the last call to ReadTag() returned tag 0 - in other words,
        /// we've reached the end of the stream when we expected to.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">The 
        /// tag read was not the one specified</exception>
        internal void CheckReadEndOfStreamTag()
        {
            if (lastTag != 0)
            {
                throw InvalidProtocolBufferException.MoreDataAvailable();
            }
        }
    }
}
#endif
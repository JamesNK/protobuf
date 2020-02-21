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

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Google.Protobuf.Collections;

namespace Google.Protobuf
{
    /// <summary>
    /// Fast parsing primitives
    /// </summary>
    [SecuritySafeCritical]
    public static class ParsingPrimitives
    {
        // TODO: we might need a withLimit version

        // return new bufferPos
        public static int ParseRawVarint64(int bufferPos, ref Span<byte> buffer, out ulong result)
        {
            // we assume there's enough data left in the buffer so that we don't have to check at all

            // the first part of the method should be inlined, the rest should invoke another non-inlined method
            result = buffer[bufferPos++];
            if (result < 128)
            {
                return bufferPos;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = buffer[bufferPos++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return bufferPos;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }

        //TODO: other version that uses SequenceReader...
        public static ulong ParseRawVarint64_WithReader(ref SequenceReader<byte> reader)
        {
            var current = reader.UnreadSpan;

            int bufferPos = 0;
            ulong result = current[bufferPos++];
            if (result < 128)
            {
                reader.Advance(bufferPos);
                return result;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = current[bufferPos++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    reader.Advance(bufferPos);
                    return result;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }

        public static ulong ParseRawVarint64_ParseContextWithPosition(ref ParseContextWithPosition context)
        {
            var current = context.Buffer;
            var position = context.Position;

            ulong result = current[position++];
            if (result < 128)
            {
                context.Position = position;
                return result;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = current[position++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    context.Position = position;
                    return result;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }

        public static ulong ParseRawVarint64_ArrayParseContextWithPosition(ArrayParseContextWithPosition context)
        {
            var current = context.Buffer;
            var position = context.Position;

            ulong result = current[position++];
            if (result < 128)
            {
                context.Position = position;
                return result;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = current[position++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    context.Position = position;
                    return result;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }

        public static int ParseRawVarint64_FromByteArray(int bufferPos, byte[] buffer, out ulong result)
        {
            // we assume there's enough data left in the buffer so that we don't have to check at all

            // the first part of the method should be inlined, the rest should invoke another non-inlined method
            result = buffer[bufferPos++];
            if (result < 128)
            {
                return bufferPos;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = buffer[bufferPos++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return bufferPos;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }   
    }

    public ref struct ParseContextWithPosition
    {
        public Span<byte> Buffer;
        public int Position;
    }

    public class ArrayParseContextWithPosition
    {
        public byte[] Buffer;
        public int Position;
    }
}
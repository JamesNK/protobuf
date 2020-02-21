#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2019 Google Inc.  All rights reserved.
// https://github.com/protocolbuffers/protobuf
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

using BenchmarkDotNet.Attributes;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using pb = global::Google.Protobuf;
using pbc = global::Google.Protobuf.Collections;
using pbr = global::Google.Protobuf.Reflection;
using scg = global::System.Collections.Generic;

namespace Google.Protobuf.Benchmarks
{
    /// <summary>
    /// Benchmark that tests serialization/deserialization of wrapper fields.
    /// </summary>
    [MemoryDiagnoser]
    public class ParsingPrimitivesBenchmark
    {
        const int IterationCount = 10000;
        byte[] manyPrimitiveFieldsByteArray;
        ReadOnlySequence<byte> manyPrimitiveFieldsReadOnlySequence;
        ArrayParseContextWithPosition context;

        [GlobalSetup]
        public void GlobalSetup()
        {
            MemoryStream memoryStream = new MemoryStream();
            CodedOutputStream outputStream = new CodedOutputStream(memoryStream);
            int extraPadding = 100;
            for (int i = 0; i < IterationCount + extraPadding; i++)
            {
                outputStream.WriteInt64(i);
            }
            outputStream.Flush();

            manyPrimitiveFieldsByteArray = memoryStream.ToArray();
            manyPrimitiveFieldsReadOnlySequence = new ReadOnlySequence<byte>(manyPrimitiveFieldsByteArray);

            context = new ArrayParseContextWithPosition();
        }

        [Benchmark]
        public ulong ParseFromSpan_PositionIsLocalVariable()
        {
            Span<byte> bufferSpan = new Span<byte>(manyPrimitiveFieldsByteArray);
            ulong sum = 0;
            int bufferPos = 0;
            for (int i = 0; i < IterationCount; i++)
            {
                bufferPos = ParsingPrimitives.ParseRawVarint64(bufferPos, ref bufferSpan, out ulong result);
                sum += result;
            }
            return sum;
        }

        [Benchmark]
        public ulong ParseFromSpan_PositionKeptBySequenceReader()
        {
            SequenceReader<byte> reader = new SequenceReader<byte>(manyPrimitiveFieldsReadOnlySequence);
            ulong sum = 0;
            for (int i = 0; i < IterationCount; i++)
            {
                ulong result = ParsingPrimitives.ParseRawVarint64_WithReader(ref reader);
                sum += result;
            }
            return sum;
        }

        [Benchmark]
        public ulong ParseFromSpan_ParseContextWithPosition()
        {
            ParseContextWithPosition context = new ParseContextWithPosition();
            context.Buffer = new Span<byte>(manyPrimitiveFieldsByteArray);
            context.Position = 0;

            ulong sum = 0;
            for (int i = 0; i < IterationCount; i++)
            {
                ulong result = ParsingPrimitives.ParseRawVarint64_ParseContextWithPosition(ref context);
                sum += result;
            }
            return sum;
        }

        [Benchmark]
        public ulong ParseFromSpan_ArrayParseContextWithPosition()
        {
            context.Buffer = manyPrimitiveFieldsByteArray;
            context.Position = 0;

            ulong sum = 0;
            for (int i = 0; i < IterationCount; i++)
            {
                ulong result = ParsingPrimitives.ParseRawVarint64_ArrayParseContextWithPosition(context);
                sum += result;
            }
            return sum;
        }

        [Benchmark]
        public ulong ParseFromByteArray_PositionIsLocalVariable()
        {
            byte[] bufferByteArray = manyPrimitiveFieldsByteArray;
            ulong sum = 0;
            int bufferPos = 0;
            for (int i = 0; i < IterationCount; i++)
            {
                bufferPos = ParsingPrimitives.ParseRawVarint64_FromByteArray(bufferPos, bufferByteArray, out ulong result);
                sum += result;
            }
            return sum;
        }
    }
}

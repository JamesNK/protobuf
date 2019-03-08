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
using Benchmarks.Proto3;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Google.Protobuf.Benchmarks
{
    [MemoryDiagnoser]
    public class JamesBenchmarks
    {
        private GoogleMessage1 _message;
        private byte[] _messageData;
        private int _messageSize;
        private BufferWriter _bufferWriter;
        private ReadOnlySequence<byte> _readOnlySequence;

        [GlobalSetup]
        public void GlobalSetup()
        {
            MemoryStream ms = new MemoryStream();
            CodedOutputStream output = new CodedOutputStream(ms);

            GoogleMessage1 googleMessage1 = new GoogleMessage1();
            googleMessage1.Field1 = "Text" + new string('!', 200);
            googleMessage1.Field2 = 2;
            googleMessage1.Field15 = new GoogleMessage1SubMessage();
            googleMessage1.Field15.Field1 = 1;

            googleMessage1.WriteTo(output);
            output.Flush();

            _message = googleMessage1;
            _messageData = ms.ToArray();
            _messageSize = googleMessage1.CalculateSize();

            _bufferWriter = new BufferWriter(new byte[_messageSize]);
            _readOnlySequence = new ReadOnlySequence<byte>(_messageData);
        }

        [Benchmark]
        public void WriteToByteArray()
        {
            CodedOutputStream output = new CodedOutputStream(new byte[_messageSize]);

            _message.WriteTo(output);
        }

        [Benchmark]
        public void ParseFromByteArray()
        {
            var messageData = new byte[_messageData.Length];
            Array.Copy(_messageData, messageData, _messageData.Length);

            CodedInputStream input = new CodedInputStream(messageData);

            GoogleMessage1 message = new GoogleMessage1();
            message.MergeFrom(input);
        }

        [Benchmark]
        public void WriteToBufferWriter()
        {
            CodedOutputWriter output = new CodedOutputWriter(_bufferWriter);

            _message.WriteTo(ref output);

            _bufferWriter.Reset();
        }

        [Benchmark]
        public void ParseFromReadOnlySequence()
        {
            CodedInputReader input = new CodedInputReader(_readOnlySequence);

            GoogleMessage1 message = new GoogleMessage1();
            message.MergeFrom(ref input);
        }
    }

    internal class BufferWriter : IBufferWriter<byte>
    {
        private readonly byte[] _buffer;
        private int _position;

        public BufferWriter(byte[] buffer)
        {
            _buffer = buffer;
        }

        public void Advance(int count)
        {
            _position += count;
        }

        public void Reset()
        {
            _position = 0;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            return _buffer.AsMemory(_position);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return _buffer.AsSpan(_position);
        }
    }
}

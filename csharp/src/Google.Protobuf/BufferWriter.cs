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

#if NETSTANDARD2_0
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Google.Protobuf
{
    /// <summary>
    /// A fast access struct that wraps <see cref="IBufferWriter{T}"/>.
    /// </summary>
    internal ref struct BufferWriter
    {
        /// <summary>
        /// The underlying <see cref="IBufferWriter{T}"/>.
        /// </summary>
        private IBufferWriter<byte> _output;

        /// <summary>
        /// The result of the last call to <see cref="IBufferWriter{T}.GetSpan(int)"/>, less any bytes already "consumed" with <see cref="Advance(int)"/>.
        /// Backing field for the <see cref="Span"/> property.
        /// </summary>
        private Span<byte> _span;

        /// <summary>
        /// The number of uncommitted bytes (all the calls to <see cref="Advance(int)"/> since the last call to <see cref="Commit"/>).
        /// </summary>
        private int _buffered;

        /// <summary>
        /// The total number of bytes written with this writer.
        /// Backing field for the <see cref="BytesCommitted"/> property.
        /// </summary>
        private long _bytesCommitted;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferWriter"/> struct.
        /// </summary>
        /// <param name="output">The <see cref="IBufferWriter{T}"/> to be wrapped.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferWriter(IBufferWriter<byte> output)
        {
            _buffered = 0;
            _bytesCommitted = 0;
            _output = output;

            _span = _output.GetSpan();
        }

        /// <summary>
        /// Gets the result of the last call to <see cref="IBufferWriter{T}.GetSpan(int)"/>.
        /// </summary>
        public Span<byte> Span => _span;

        /// <summary>
        /// Gets the total number of bytes written with this writer.
        /// </summary>
        public long BytesCommitted => _bytesCommitted;

        /// <summary>
        /// Gets the <see cref="IBufferWriter{T}"/> underlying this instance.
        /// </summary>
        internal IBufferWriter<byte> UnderlyingWriter => _output;

        public Span<byte> GetSpan(int sizeHint)
        {
            Ensure(sizeHint);
            return this.Span;
        }

        /// <summary>
        /// Calls <see cref="IBufferWriter{T}.Advance(int)"/> on the underlying writer
        /// with the number of uncommitted bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Commit()
        {
            var buffered = _buffered;
            if (buffered > 0)
            {
                _bytesCommitted += buffered;
                _buffered = 0;
                _output.Advance(buffered);
                _span = default;
            }
        }

        /// <summary>
        /// Used to indicate that part of the buffer has been written to.
        /// </summary>
        /// <param name="count">The number of bytes written to.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            _buffered += count;
            _span = _span.Slice(count);
        }

        /// <summary>
        /// Copies the caller's buffer into this writer and calls <see cref="Advance(int)"/> with the length of the source buffer.
        /// </summary>
        /// <param name="source">The buffer to copy in.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ReadOnlySpan<byte> source)
        {
            if (_span.Length >= source.Length)
            {
                source.CopyTo(_span);
                Advance(source.Length);
            }
            else
            {
                WriteMultiBuffer(source);
            }
        }

        /// <summary>
        /// Acquires a new buffer if necessary to ensure that some given number of bytes can be written to a single buffer.
        /// </summary>
        /// <param name="count">The number of bytes that must be allocated in a single buffer.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Ensure(int count = 1)
        {
            if (_span.Length < count)
            {
                EnsureMore(count);
            }
        }

        /// <summary>
        /// Gets a fresh span to write to, with an optional minimum size.
        /// </summary>
        /// <param name="count">The minimum size for the next requested buffer.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureMore(int count = 0)
        {
            if (_buffered > 0)
            {
                Commit();
            }

            _span = _output.GetSpan(count);
        }

        /// <summary>
        /// Copies the caller's buffer into this writer, potentially across multiple buffers from the underlying writer.
        /// </summary>
        /// <param name="source">The buffer to copy into this writer.</param>
        private void WriteMultiBuffer(ReadOnlySpan<byte> source)
        {
            while (source.Length > 0)
            {
                if (_span.Length == 0)
                {
                    EnsureMore();
                }

                var writable = Math.Min(source.Length, _span.Length);
                source.Slice(0, writable).CopyTo(_span);
                source = source.Slice(writable);
                Advance(writable);
            }
        }
    }
}
#endif
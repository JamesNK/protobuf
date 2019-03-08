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

#if NETCOREAPP2_1
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Google.Protobuf
{
    internal class ArrayBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private ResizableArray<byte> _buffer;
        private readonly int _maximumSizeHint;

        public ArrayBufferWriter(int capacity, int? maximumSizeHint = null)
        {
            _buffer = new ResizableArray<byte>(ArrayPool<byte>.Shared.Rent(capacity));
            _maximumSizeHint = maximumSizeHint ?? int.MaxValue;
        }

        public int CommitedByteCount => _buffer.Count;

        public void Clear()
        {
            _buffer.Count = 0;
        }

        public ArraySegment<byte> Free => _buffer.Free;

        public ArraySegment<byte> Formatted => _buffer.Full;

        public Memory<byte> GetMemory(int minimumLength = 0)
        {
            minimumLength = Math.Min(minimumLength, _maximumSizeHint);

            if (minimumLength < 1)
            {
                minimumLength = 1;
            }

            if (minimumLength > _buffer.FreeCount)
            {
                int doubleCount = _buffer.FreeCount * 2;
                int newSize = minimumLength > doubleCount ? minimumLength : doubleCount;
                byte[] newArray = ArrayPool<byte>.Shared.Rent(newSize + _buffer.Count);
                byte[] oldArray = _buffer.Resize(newArray);
                ArrayPool<byte>.Shared.Return(oldArray);
            }

            return _buffer.FreeMemory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan(int minimumLength = 0)
        {
            minimumLength = Math.Min(minimumLength, _maximumSizeHint);

            if (minimumLength < 1)
            {
                minimumLength = 1;
            }

            if (minimumLength > _buffer.FreeCount)
            {
                int doubleCount = _buffer.FreeCount * 2;
                int newSize = minimumLength > doubleCount ? minimumLength : doubleCount;
                byte[] newArray = ArrayPool<byte>.Shared.Rent(newSize + _buffer.Count);
                byte[] oldArray = _buffer.Resize(newArray);
                ArrayPool<byte>.Shared.Return(oldArray);
            }

            return _buffer.FreeSpan;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int bytes)
        {
            _buffer.Count += bytes;
            if (_buffer.Count > _buffer.Capacity)
            {
                throw new InvalidOperationException("More bytes commited than returned from FreeBuffer");
            }
        }

        public void Dispose()
        {
            byte[] array = _buffer.Array;
            _buffer.Array = null;
            ArrayPool<byte>.Shared.Return(array);
        }

        private struct ResizableArray<T>
        {
            public ResizableArray(T[] array, int count = 0)
            {
                Array = array;
                Count = count;
            }

            public T[] Array { get; set; }

            public int Count { get; set; }

            public int Capacity => Array.Length;

            public T[] Resize(T[] newArray)
            {
                T[] oldArray = Array;
                Array.AsSpan(0, Count).CopyTo(newArray);  // CopyTo will throw if newArray.Length < _count
                Array = newArray;
                return oldArray;
            }

            public ArraySegment<T> Full => new ArraySegment<T>(Array, 0, Count);

            public ArraySegment<T> Free => new ArraySegment<T>(Array, Count, Array.Length - Count);

            public Span<T> FreeSpan => new Span<T>(Array, Count, Array.Length - Count);

            public Memory<T> FreeMemory => new Memory<T>(Array, Count, Array.Length - Count);

            public int FreeCount => Array.Length - Count;
        }
    }
}
#endif
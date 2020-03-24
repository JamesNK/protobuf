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

#if GOOGLE_PROTOBUF_SUPPORT_SYSTEM_MEMORY
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;

namespace Google.Protobuf
{
    /// <summary>
    /// SequenceReader is originally from corefx, and has been contributed to Protobuf
    /// https://github.com/dotnet/runtime/blob/071da4c41aa808c949a773b92dca6f88de9d11f3/src/libraries/System.Memory/src/System/Buffers/SequenceReader.cs
    /// </summary>
    [SecuritySafeCritical]
    internal ref partial struct LimitedSequenceReader
    {
        private SequencePosition _currentPosition;
        private SequencePosition _nextPosition;
        private bool _moreData;
        private readonly int _length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LimitedSequenceReader(ReadOnlySequence<byte> sequence)
        {
            CurrentSpanIndex = 0;
            Consumed = 0;
            Sequence = sequence;
            _currentPosition = sequence.Start;
            _length = (int)Sequence.Length;
            CurrentLimit = _length;

            if (_length > 0)
            {
                var first = sequence.First.Span;
                _nextPosition = sequence.GetPosition(first.Length);
                _currentSpan = first;
                CurrentLimitedSpan = first;
                _moreData = first.Length > 0;

                if (!_moreData && !sequence.IsSingleSegment)
                {
                    _moreData = true;
                    GetNextSpan();
                }
            }
            else
            {
                _moreData = false;
                _nextPosition = _currentPosition;
                _currentSpan = ReadOnlySpan<byte>.Empty;
                CurrentLimitedSpan = ReadOnlySpan<byte>.Empty;
            }
        }

        /// <summary>
        /// True when there is no more data in the <see cref="Sequence"/>.
        /// </summary>
        public bool End => !_moreData;

        /// <summary>
        /// The underlying <see cref="ReadOnlySequence{T}"/> for the reader.
        /// </summary>
        public ReadOnlySequence<byte> Sequence { get; }

        public SequencePosition Position => Sequence.GetPosition(CurrentSpanIndex, _currentPosition);

        private ReadOnlySpan<byte> _currentSpan;

        public ReadOnlySpan<byte> CurrentLimitedSpan { get; private set; }

        public int CurrentSpanIndex { get; private set; }

        public ReadOnlySpan<byte> UnreadSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentSpan.Slice(CurrentSpanIndex);
        }

        public int Consumed { get; private set; }

        public int Remaining => _length - Consumed;

        public int CurrentLimit { get; private set; }

        public void SetLimit(int newLimit)
        {
            CurrentLimit = newLimit;
            CurrentLimitedSpan = _currentSpan.Slice(0, Math.Min(CurrentLimit, _currentSpan.Length));
        }

        /// <summary>
        /// Move the reader back the specified number of items.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rewind(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            Consumed -= count;

            if (CurrentSpanIndex >= count)
            {
                CurrentSpanIndex -= count;
                _moreData = true;
            }
            else
            {
                // Current segment doesn't have enough data, scan backward through segments
                RetreatToPreviousSpan(Consumed);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RetreatToPreviousSpan(int consumed)
        {
            ResetReader();
            Advance(consumed);
        }

        private void ResetReader()
        {
            CurrentSpanIndex = 0;
            Consumed = 0;
            _currentPosition = Sequence.Start;
            _nextPosition = _currentPosition;

            if (Sequence.TryGet(ref _nextPosition, out ReadOnlyMemory<byte> memory, advance: true))
            {
                _moreData = true;

                if (memory.Length == 0)
                {
                    _currentSpan = default;
                    CurrentLimitedSpan = default;
                    // No data in the first span, move to one with data
                    GetNextSpan();
                }
                else
                {
                    _currentSpan = memory.Span;
                    CurrentLimitedSpan = _currentSpan.Slice(0, Math.Min(CurrentLimit, _currentSpan.Length));
                }
            }
            else
            {
                // No data in any spans and at end of sequence
                _moreData = false;
                _currentSpan = default;
                CurrentLimitedSpan = default;
            }
        }

        /// <summary>
        /// Get the next segment with available data, if any.
        /// </summary>
        private void GetNextSpan()
        {
            if (!Sequence.IsSingleSegment)
            {
                SequencePosition previousNextPosition = _nextPosition;
                while (Sequence.TryGet(ref _nextPosition, out ReadOnlyMemory<byte> memory, advance: true))
                {
                    _currentPosition = previousNextPosition;
                    if (memory.Length > 0)
                    {
                        _currentSpan = memory.Span;
                        CurrentLimitedSpan = _currentSpan.Slice(0, Math.Min(CurrentLimit, _currentSpan.Length));
                        CurrentSpanIndex = 0;
                        return;
                    }
                    else
                    {
                        _currentSpan = default;
                        CurrentLimitedSpan = default;
                        CurrentSpanIndex = 0;
                        previousNextPosition = _nextPosition;
                    }
                }
            }
            _moreData = false;
        }

        /// <summary>
        /// Move the reader ahead the specified number of items.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            if (_currentSpan.Length - CurrentSpanIndex > count)
            {
                CurrentSpanIndex += count;
                Consumed += count;
            }
            else
            {
                // Can't satisfy from the current span
                AdvanceToNextSpan(count);
            }
        }

        /// <summary>
        /// Unchecked helper to avoid unnecessary checks where you know count is valid.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceCurrentSpan(int count)
        {
            Debug.Assert(count >= 0);

            Consumed += count;
            CurrentSpanIndex += count;
            if (CurrentSpanIndex >= _currentSpan.Length)
            {
                GetNextSpan();
            }
        }

        /// <summary>
        /// Only call this helper if you know that you are advancing in the current span
        /// with valid count and there is no need to fetch the next one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceWithinSpan(int count)
        {
            Debug.Assert(count >= 0);

            Consumed += count;
            CurrentSpanIndex += count;

            Debug.Assert(CurrentSpanIndex < _currentSpan.Length);
        }

        private void AdvanceToNextSpan(int count)
        {
            Consumed += count;
            while (_moreData)
            {
                int remaining = _currentSpan.Length - CurrentSpanIndex;

                if (remaining > count)
                {
                    CurrentSpanIndex += count;
                    count = 0;
                    break;
                }

                // As there may not be any further segments we need to
                // push the current index to the end of the span.
                CurrentSpanIndex += remaining;
                count -= remaining;
                Debug.Assert(count >= 0);

                GetNextSpan();

                if (count == 0)
                {
                    break;
                }
            }

            if (count != 0)
            {
                // Not enough data left- adjust for where we actually ended and throw
                Consumed -= count;
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<byte> destination)
        {
            ReadOnlySpan<byte> firstSpan = UnreadSpan;
            if (firstSpan.Length >= destination.Length)
            {
                firstSpan.Slice(0, destination.Length).CopyTo(destination);
                return true;
            }

            return TryCopyMultisegment(destination);
        }

        internal bool TryCopyMultisegment(Span<byte> destination)
        {
            if (Remaining < destination.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> firstSpan = UnreadSpan;
            Debug.Assert(firstSpan.Length < destination.Length);
            firstSpan.CopyTo(destination);
            int copied = firstSpan.Length;

            SequencePosition next = _nextPosition;
            while (Sequence.TryGet(ref next, out ReadOnlyMemory<byte> nextSegment, true))
            {
                if (nextSegment.Length > 0)
                {
                    ReadOnlySpan<byte> nextSpan = nextSegment.Span;
                    int toCopy = Math.Min(nextSpan.Length, destination.Length - copied);
                    nextSpan.Slice(0, toCopy).CopyTo(destination.Slice(copied));
                    copied += toCopy;
                    if (copied >= destination.Length)
                    {
                        break;
                    }
                }
            }

            return true;
        }
    }
}
#endif
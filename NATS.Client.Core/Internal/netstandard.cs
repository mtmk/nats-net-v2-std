#if NETSTANDARD2_0

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit {}
}

namespace NATS.Client.Core.Internal
{
    using System.Buffers;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Diagnostics;

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal readonly struct VoidResult
    {
    }
    
    internal sealed class TaskCompletionSource : TaskCompletionSource<VoidResult>
    {
        public TaskCompletionSource(TaskCreationOptions creationOptions) : base(creationOptions)
        {
        }

        public bool TrySetResult() => TrySetResult(default);
        
        public void SetResult() => SetResult(default);

        public new Task Task => base.Task;
    }

    internal static class EncodingExtensions
    {
        internal static int GetBytes(this Encoding encoding, string chars, Span<byte> bytes)
        {
            var buffer = encoding.GetBytes(chars);
            buffer.AsSpan().CopyTo(bytes);
            return buffer.Length;
        }
        
        internal static void GetBytes(this Encoding encoding, string chars, IBufferWriter<byte> bw)
        {
            var buffer = encoding.GetBytes(chars);
            bw.Write(buffer);
        }
        
        internal static string GetString(this Encoding encoding, in ReadOnlySequence<byte> buffer)
        {
            return encoding.GetString(buffer.ToArray());
        }
        
        internal static string GetString(this Encoding encoding, in ReadOnlySpan<byte> buffer)
        {
            return encoding.GetString(buffer.ToArray());
        }
    }

    internal static class TaskExtensions
    {
        internal static bool IsNotCompletedSuccessfully(this Task? task)
        {
            return task != null && (!task.IsCompleted || task.IsCanceled || task.IsFaulted);
        }
        
        internal static Task WaitAsync(this Task task, CancellationToken cancellationToken)
            => WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);
        
        internal static Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            var timeoutTask = Task.Delay(timeout, cancellationToken);

            return Task.WhenAny(task, timeoutTask).ContinueWith((completedTask) =>
            {
                if (completedTask.Result == timeoutTask)
                {
                    throw new TimeoutException("The operation has timed out.");
                }

                return task;
            }, cancellationToken).Unwrap();
        }
    }

    internal static class SequenceReaderExtensions
    {
        internal static bool TryReadTo(this ref SequenceReader<byte> reader, out ReadOnlySpan<byte> span, ReadOnlySpan<byte> delimiters)
        {
            return reader.TryReadToAny(out span, delimiters);
        }
    }

    internal static class SpanExtensions
    {
        internal static bool Contains(this in Span<byte> span, byte value)
        {
            foreach (var b in span)
            {
                if (b == value)
                    return true;
            }

            return false;
        }
    }
    
    internal static class ReadOnlySequenceExtensions
    {
        // Adapted from .NET 6.0 implementation
        internal static long GetOffset<T>(this in ReadOnlySequence<T> sequence, SequencePosition position)
        {
            object? positionSequenceObject = position.GetObject();
            bool positionIsNull = positionSequenceObject == null;

            object? startObject = sequence.Start.GetObject();
            object? endObject = sequence.End.GetObject();

            uint positionIndex = (uint)position.GetInteger();

            // if sequence object is null we suppose start segment
            if (positionIsNull)
            {
                positionSequenceObject = sequence.Start.GetObject();
                positionIndex = (uint)sequence.Start.GetInteger();
            }

            // Single-Segment Sequence
            if (startObject == endObject)
            {
                return positionIndex;
            }
            else
            {
                // Verify position validity, this is not covered by BoundsCheck for Multi-Segment Sequence
                // BoundsCheck for Multi-Segment Sequence check only validity inside current sequence but not for SequencePosition validity.
                // For single segment position bound check is implicit.
                Debug.Assert(positionSequenceObject != null);

                if (((ReadOnlySequenceSegment<T>)positionSequenceObject).Memory.Length - positionIndex < 0)
                    throw new ArgumentOutOfRangeException();

                // Multi-Segment Sequence
                ReadOnlySequenceSegment<T>? currentSegment = (ReadOnlySequenceSegment<T>?)startObject;
                while (currentSegment != null && currentSegment != positionSequenceObject)
                {
                    currentSegment = currentSegment.Next!;
                }

                // Hit the end of the segments but didn't find the segment
                if (currentSegment is null)
                {
                    throw new ArgumentOutOfRangeException();
                }

                Debug.Assert(currentSegment!.RunningIndex + positionIndex >= 0);

                return currentSegment!.RunningIndex + positionIndex;
            }
        }
    }
    
    public class PeriodicTimer : IDisposable
    {
        private readonly Timer _timer;
        private TaskCompletionSource<bool> _tcs;
        private readonly TimeSpan _period;
        private bool _disposed;

        public PeriodicTimer(TimeSpan period)
        {
            _period = period;
            _timer = new Timer(Callback, null, period, Timeout.InfiniteTimeSpan);
            _tcs = new TaskCompletionSource<bool>();
        }

        private void Callback(object state)
        {
            TaskCompletionSource<bool> tcs = Interlocked.Exchange(ref _tcs, new TaskCompletionSource<bool>());
            tcs.TrySetResult(true);
        }

        public Task<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PeriodicTimer));

            _timer.Change(_period, Timeout.InfiniteTimeSpan);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<bool>(cancellationToken);

            cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));

            return _tcs.Task;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            _tcs.TrySetResult(false); // Signal no more ticks will occur
        }
    }
    
    internal static class KeyValuePairExtensions
    {
        internal static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kv, out TKey key, out TValue value)
        {
            key = kv.Key;
            value = kv.Value;
        }
    }
}

#endif

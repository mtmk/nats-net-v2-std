#if !NET6_0_OR_GREATER

using System.Buffers;
using System.Text;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit {}
}

namespace NATS.Client.TestUtilities
{
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using System.Threading.Channels;

    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal readonly struct VoidResult
    {
    }
    
    internal static class TaskExtensions
    {
        internal static Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                task.Wait((int)timeout.TotalMilliseconds, cancellationToken);
                return task.Result;
            }, cancellationToken);
        }
    }

    internal static class ChannelReaderExtensions
    {
        public static async IAsyncEnumerable<T> ReadAllAsync<T>(this ChannelReader<T> reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out T? item))
                {
                    yield return item;
                }
            }
        }
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
}

#endif

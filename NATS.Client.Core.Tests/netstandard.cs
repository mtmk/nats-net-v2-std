#if !NET6_0_OR_GREATER

namespace NATS.Client.TestUtilities
{
    using System.Runtime.CompilerServices;
    using System.Threading.Channels;

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

}

#endif

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NATS.Client.Core.Internal;

public static class Logger
{
    private static object _lock = new();
    private static Action<string> _log = _ => { };

    public static Action<string> Log
    {
        get
        {
            lock (_lock) return _log;
        }
        set
        {
            lock (_lock) _log = value;
        }
    }
}
internal class InboxSub : NatsSubBase
{
    private readonly InboxSubBuilder _inbox;
    private readonly NatsConnection _connection;

    public InboxSub(
        InboxSubBuilder inbox,
        string subject,
        NatsSubOpts? opts,
        NatsConnection connection,
        ISubscriptionManager manager)
    : base(connection, manager, subject, queueGroup: default, opts)
    {
        _inbox = inbox;
        _connection = connection;
    }

    // Avoid base class error handling since inboxed subscribers will be responsible for that.
    public override ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer) =>
        _inbox.ReceivedAsync(subject, replyTo, headersBuffer, payloadBuffer, _connection);

    // Not used. Dummy implementation to keep base happy.
    protected override ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
        => default;

    protected override void TryComplete()
    {
    }
}

internal class InboxSubBuilder : ISubscriptionManager
{
    private readonly ILogger<InboxSubBuilder> _logger;
#if NET6_0_OR_GREATER
    private readonly ConcurrentDictionary<string, ConditionalWeakTable<NatsSubBase, object>> _bySubject = new();
#else
    private readonly ConcurrentDictionary<string, List<WeakReference<NatsSubBase>>> _bySubject = new();
#endif

    public InboxSubBuilder(ILogger<InboxSubBuilder> logger) => _logger = logger;

    public InboxSub Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager)
    {
        return new InboxSub(this, subject, opts, connection, manager);
    }

    public ValueTask RegisterAsync(NatsSubBase sub)
    {
#if NET6_0_OR_GREATER
        _bySubject.AddOrUpdate(
                sub.Subject,
                static (_, s) => new ConditionalWeakTable<NatsSubBase, object> { { s, new object() } },
                static (_, subTable, s) =>
                {
                    lock (subTable)
                    {
                        if (!subTable.Any())
                        {
                            // if current subTable is empty, it may be in process of being removed
                            // return a new object
                            return new ConditionalWeakTable<NatsSubBase, object> { { s, new object() } };
                        }

                        // the updateValueFactory delegate can be called multiple times
                        // use AddOrUpdate to avoid exceptions if this happens
                        subTable.AddOrUpdate(s, new object());
                        return subTable;
                    }
                },
                sub);
#else
        Logger.Log($">>> [Inbox] [RegisterAsync] {sub.Subject}");
        _bySubject.AddOrUpdate(
            sub.Subject,
            _ =>
            {
                Logger.Log($">>> [Inbox] [RegisterAsync] [1] {sub.Subject}");
                return new List<WeakReference<NatsSubBase>> { new WeakReference<NatsSubBase>(sub) };
            },
            (_, subTable) =>
            {
                Logger.Log($">>> [Inbox] [RegisterAsync] [2] {sub.Subject}");
                lock (subTable)
                {
                    if (subTable.Count == 0)
                    {
                        subTable.Add(new WeakReference<NatsSubBase>(sub));
                        return subTable;
                    }

                    var wr = subTable.FirstOrDefault(w =>
                    {
                        if (w.TryGetTarget(out var t))
                        {
                            if (t == sub)
                            {
                                return true;
                            }
                        }

                        return false;
                    });
                    
                    if (wr != null)
                    {
                        subTable.Remove(wr);
                    }
                    
                    subTable.Add(new WeakReference<NatsSubBase>(sub));
                    return subTable;
                }
            });
#endif
        return sub.ReadyAsync();
    }

    public async ValueTask ReceivedAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer, NatsConnection connection)
    {
        Logger.Log($">>> [Inbox] [ReceivedAsync] {subject}");

        if (!_bySubject.TryGetValue(subject, out var subTable))
        {
            Logger.Log($">>> [Inbox] [ReceivedAsync] [1] {subject}");
            _logger.LogWarning(NatsLogEvents.InboxSubscription, "Unregistered message inbox received for {Subject}", subject);
            return;
        }

#if NET6_0_OR_GREATER
        foreach (var (sub, _) in subTable)
        {
            await sub.ReceiveAsync(subject, replyTo, headersBuffer, payloadBuffer).ConfigureAwait(false);
        }
#else
        Logger.Log($">>> [Inbox] [ReceivedAsync] [2] {subject}");
        foreach (var weakReference in subTable)
        {
            if (weakReference.TryGetTarget(out var sub))
            {
                Logger.Log($">>> [Inbox] [ReceivedAsync] [3] {subject}");
                await sub.ReceiveAsync(subject, replyTo, headersBuffer, payloadBuffer).ConfigureAwait(false);
            }
        }
#endif
    }

    public ValueTask RemoveAsync(NatsSubBase sub)
    {
        if (!_bySubject.TryGetValue(sub.Subject, out var subTable))
        {
            _logger.LogWarning(NatsLogEvents.InboxSubscription, "Unregistered message inbox received for {Subject}", sub.Subject);
            return default;
        }

        lock (subTable)
        {
#if NET6_0_OR_GREATER
            if (!subTable.Remove(sub))
                _logger.LogWarning(NatsLogEvents.InboxSubscription, "Unregistered message inbox received for {Subject}", sub.Subject);

            if (!subTable.Any())
            {
                // try to remove this specific instance of the subTable
                // if an update is in process and sees an empty subTable, it will set a new instance
                _bySubject.TryRemove(KeyValuePair.Create(sub.Subject, subTable));
            }
#else
            if (subTable.Count == 0)
            {
                _bySubject.TryRemove(sub.Subject, out _);
            }
#endif
        }

        return default;
    }
}

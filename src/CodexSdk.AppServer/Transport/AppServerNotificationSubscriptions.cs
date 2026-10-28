using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace OpenAI.CodexSdk.AppServer.Transport;

internal sealed class AppServerNotificationSubscriptions
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Subscriber> _subscribers = [];
    private long _nextSubscriberId;
    private bool _completed;
    private Exception? _completionError;

    public void Publish(AppServerNotification notification)
    {
        lock (_gate)
        {
            if (_completed)
            {
                if (_completionError is not null)
                    throw _completionError;
                throw new ObjectDisposedException(nameof(AppServerNotificationSubscriptions));
            }

            foreach (var subscriber in _subscribers.Values)
            {
                if (!subscriber.Channel.Writer.TryWrite(notification))
                    throw new InvalidOperationException("A notification subscription closed before publication completed.");
            }
        }
    }

    public IAsyncEnumerable<AppServerNotification> Subscribe(CancellationToken cancellationToken = default)
    {
        var subscriber = new Subscriber();
        long subscriberId;

        lock (_gate)
        {
            if (_completed)
            {
                subscriber.Complete(_completionError);
                subscriberId = 0;
            }
            else
            {
                subscriberId = ++_nextSubscriberId;
                _subscribers.Add(subscriberId, subscriber);
            }
        }

        if (subscriberId != 0 && cancellationToken.CanBeCanceled)
        {
            subscriber.SetCancellationRegistration(cancellationToken.Register(static state =>
            {
                var (subscriptions, id) = ((AppServerNotificationSubscriptions Subscriptions, long Id))state!;
                subscriptions.RemoveSubscriber(id);
            }, (this, subscriberId)));
        }

        return ReadAllAsync(subscriberId, subscriber, cancellationToken);
    }

    private async IAsyncEnumerable<AppServerNotification> ReadAllAsync(
        long subscriberId,
        Subscriber subscriber,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var notification in subscriber.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return notification;
        }
        finally
        {
            RemoveSubscriber(subscriberId);
        }
    }

    public void Complete(Exception? error = null)
    {
        lock (_gate)
        {
            if (_completed)
                return;

            _completed = true;
            _completionError = error;
            foreach (var subscriber in _subscribers.Values)
                subscriber.Complete(error);
            _subscribers.Clear();
        }
    }

    private void RemoveSubscriber(long subscriberId)
    {
        if (subscriberId == 0)
            return;

        lock (_gate)
        {
            if (_subscribers.Remove(subscriberId, out var subscriber))
                subscriber.Complete();
        }
    }

    private sealed class Subscriber
    {
        private readonly object _gate = new();
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _completed;

        public Subscriber()
        {
            Channel = System.Threading.Channels.Channel.CreateUnbounded<AppServerNotification>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public Channel<AppServerNotification> Channel { get; }

        public void SetCancellationRegistration(CancellationTokenRegistration registration)
        {
            lock (_gate)
            {
                if (!_completed)
                {
                    _cancellationRegistration = registration;
                    return;
                }
            }

            registration.Unregister();
        }

        public void Complete(Exception? error = null)
        {
            CancellationTokenRegistration registration;
            lock (_gate)
            {
                if (_completed)
                    return;

                _completed = true;
                registration = _cancellationRegistration;
                _cancellationRegistration = default;
            }

            Channel.Writer.TryComplete(error);
            registration.Unregister();
        }
    }
}

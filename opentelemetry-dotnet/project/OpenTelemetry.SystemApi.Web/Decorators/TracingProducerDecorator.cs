using System.Diagnostics;
using System.Text;
using Bogus;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.System.Web.Infrastructure;
using OpenTelemetry.Trace;

namespace OpenTelemetry.System.Web.Decorators;

public class TracingProducerDecorator<TKey, TValue>: IProducer<TKey, TValue>
{
    private static readonly Faker Faker = new Faker("ru");
    private readonly IProducer<TKey, TValue> _producer;
    private readonly IOptions<ApplicationOptions> _options;
    private readonly ILogger<TracingProducerDecorator<TKey, TValue>> _logger;

    public TracingProducerDecorator(IProducer<TKey, TValue> producer, IOptions<ApplicationOptions> options, ILogger<TracingProducerDecorator<TKey, TValue>> logger)
    {
        _producer = producer;
        _options = options;
        _logger = logger;
    }

    public void Dispose()
    {
        _producer.Dispose();
    }

    public int AddBrokers(string brokers)
    {
        return _producer.AddBrokers(brokers);
    }

    public void SetSaslCredentials(string username, string password)
    {
        _producer.SetSaslCredentials(username, password);
    }

    public Handle Handle => _producer.Handle;

    public string Name => _producer.Name;

    private const string ProducingActivity = "Kafka.Producer.Produce";

    private Activity? StartActiveSpan(Message<TKey, TValue> message)
    {
        var activity = Tracing.WebActivitySource.StartActivity(ProducingActivity, ActivityKind.Producer);
        if (activity is not null)
        {
            if (_options.Value.SendRandomBaggage)
            {
                var pairs = Enumerable.Range(0, 3)
                                      .Select(_ =>
                                           new KeyValuePair<string, string>(Faker.Random.Word(), Faker.Random.Word()))
                                      .ToArray();
                _logger.LogInformation("Выставляю Baggage для запроса: {Baggage}", pairs);
                foreach (var (key, value) in pairs)
                {
                    Baggage.SetBaggage(key, value);
                }
            }
            
            var propagationContext = new PropagationContext(activity.Context, Baggage.Current);
            Propagators.DefaultTextMapPropagator.Inject(propagationContext, message.Headers ??= new Headers(),
                (headers, key, value) => headers.Add(key, Encoding.UTF8.GetBytes(value)));
        }
        return activity;
    }
    
    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = new CancellationToken())
    {
        using var activity = StartActiveSpan(message);
        try
        {
            var result = await _producer.ProduceAsync(topic, message, cancellationToken);
            activity?.SetTag("kafka.topic", result.Topic);
            activity?.SetTag("kafka.partition", result.Partition.Value);
            activity?.SetTag("kafka.offset", result.Offset.Value);
            return result;
        }
        catch (Exception e) when (activity is not null)
        {
            activity.RecordException(e);
            activity.SetStatus(Status.Error);
            throw;
        }
    }

    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(TopicPartition topicPartition,
                                                                 Message<TKey, TValue> message,
                                                                 CancellationToken cancellationToken = new CancellationToken())
    {
        using var span = StartActiveSpan(message);
        try
        {
            var result = await _producer.ProduceAsync(topicPartition, message, cancellationToken);
            span?.SetTag("kafka.topic", result.Topic);
            span?.SetTag("kafka.partition", result.Partition.Value);
            span?.SetTag("kafka.offset", result.Offset.Value);
            return result;
        }
        catch (Exception e)
        {
            span.RecordException(e);
            span.SetStatus(Status.Error);
            throw;
        }
    }

    public void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>> deliveryHandler = null!)
    {
        // ReSharper disable AccessToDisposedClosure
        var span = StartActiveSpan(message);
        try
        {
            _producer.Produce(topic, message, (r) =>
            {
                try
                {
                    if (span is not null)
                    {
                        if (r.Error.IsError)
                        {
                            span.SetStatus(ActivityStatusCode.Error, $"Ошибка кафки: {r.Error.Reason}");
                        }
                        else
                        {
                            span.SetTag("kafka.topic", r.Topic);
                            span.SetTag("kafka.partition", r.Partition.Value);
                            span.SetTag("kafka.offset", r.Offset.Value);
                        }
                        span.Dispose();
                    }
                }
                catch (ObjectDisposedException)
                { }
                deliveryHandler(r);
            });
        }
        catch (Exception e)
        {
            if (span is not null)
            {
                try
                {
                    span.RecordException(e);
                    span.SetStatus(Status.Error);
                    span.Dispose();
                }
                catch (ObjectDisposedException)
                { }
            }
            throw;
        }
    }

    public void Produce(TopicPartition topicPartition, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>> deliveryHandler = null)
    {
        // ReSharper disable AccessToDisposedClosure
        var span = StartActiveSpan(message);
        try
        {
            _producer.Produce(topicPartition, message, (r) =>
            {
                try
                {
                    if (span is not null)
                    {
                        if (r.Error.IsError)
                        {
                            span.SetStatus(ActivityStatusCode.Error, $"Ошибка кафки: {r.Error.Reason}");
                        }
                        else
                        {
                            span.SetTag("kafka.topic", r.Topic);
                            span.SetTag("kafka.partition", r.Partition.Value);
                            span.SetTag("kafka.offset", r.Offset.Value);
                        }
                        span.Dispose();
                    }
                }
                catch (ObjectDisposedException)
                { }
                deliveryHandler(r);
            });
        }
        catch (Exception e)
        {
            if (span is not null)
            {
                span.RecordException(e);
                span.SetStatus(Status.Error);
                span.Dispose();
            }
            throw;
        }
    }

    public int Poll(TimeSpan timeout)
    {
        return _producer.Poll(timeout);
    }

    public int Flush(TimeSpan timeout)
    {
        return _producer.Flush(timeout);
    }

    public void Flush(CancellationToken cancellationToken = new CancellationToken())
    {
        _producer.Flush(cancellationToken);
    }

    public void InitTransactions(TimeSpan timeout)
    {
        _producer.InitTransactions(timeout);
    }

    public void BeginTransaction()
    {
        _producer.BeginTransaction();
    }

    public void CommitTransaction(TimeSpan timeout)
    {
        _producer.CommitTransaction(timeout);
    }

    public void CommitTransaction()
    {
        _producer.CommitTransaction();
    }

    public void AbortTransaction(TimeSpan timeout)
    {
        _producer.AbortTransaction(timeout);
    }

    public void AbortTransaction()
    {
        _producer.AbortTransaction();
    }

    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout)
    {
        _producer.SendOffsetsToTransaction(offsets, groupMetadata, timeout);
    }
}
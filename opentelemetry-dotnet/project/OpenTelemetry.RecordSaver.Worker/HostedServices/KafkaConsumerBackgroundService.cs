using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.RecordSaver.Worker.Database;
using OpenTelemetry.RecordSaver.Worker.Infrastructure;
using OpenTelemetry.RecordSaver.Worker.Options;
using OpenTelemetry.Trace;

namespace OpenTelemetry.RecordSaver.Worker.HostedServices;

public class KafkaConsumerBackgroundService : BackgroundService
{
    private readonly ILogger<KafkaConsumerBackgroundService> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IOptions<ApplicationOptions> _options;

    public KafkaConsumerBackgroundService(ILogger<KafkaConsumerBackgroundService> logger, 
                                          IDbContextFactory<ApplicationDbContext> dbContextFactory, 
                                          IOptions<ApplicationOptions> options)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogInformation("Создаю консьюмера кафки");
        
        using var consumer = new ConsumerBuilder<Null, string>(new ConsumerConfig()
            {
                BootstrapServers = _options.Value.BootstrapServers,
                GroupId = "Consumer"
            })
           .Build();
        _logger.LogInformation("Подписываюсь на очередь");
        consumer.Subscribe(_options.Value.KafkaQueue);
        _logger.LogInformation("Начинаю работу");
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            if (result is null)
            {
                _logger.LogInformation("Из консьюмера вернулся null. Закачиваю работу");
                break;
            }
            
            using var span = StartActivity(result);
            try
            {
                await ProcessMessageAsync(result, stoppingToken);
            }
            catch (Exception e)
            {
                span.RecordException(e);
                throw;
            }
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<Null, string> result, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Создаю DbContext");
        await using var context = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
        _logger.LogInformation("Сохраняю запись: {Record}", result.Message.Value);
            
        await context.AddAsync(new Record() {Value = result.Message.Value}, stoppingToken);
        await context.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Сохранение закончено");
    }

    private static IEnumerable<string> GetValuesFromHeadersSafe(Headers headers, string key) =>
        headers.Where(x => x.Key == key)
               .Select(b =>
                {
                    try
                    {
                        return Encoding.UTF8.GetString(b.GetValueBytes());
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                })
               .Where(x => x is not null)!;

    private Activity? StartActivity(ConsumeResult<Null, string> result)
    {
        var propagationContext = Propagators.DefaultTextMapPropagator.Extract(default, result.Message.Headers,
            GetValuesFromHeadersSafe);

        var useLink = _options.Value.UseLink;
        ActivityLink[]? links = null;
        ActivityContext parentContext = default;
        
        if (useLink)
        {
            links = new ActivityLink[]
            {
                new(propagationContext.ActivityContext)
            };
        }
        else
        {
            parentContext = propagationContext.ActivityContext;
        }

        var activity = Tracing.ConsumerActivitySource.StartActivity(
            Tracing.KafkaMessageProcessing, 
            kind: ActivityKind.Consumer,
            parentContext: parentContext,
            links: links);

        var baggage = propagationContext.Baggage;
        Baggage.Current = baggage;
        
        if (_options.Value.LogBaggageOnContextExtraction)
        {
            _logger.LogInformation("Полученный Baggage: {Baggage}", baggage.GetBaggage());
        }

        // https://github.com/open-telemetry/semantic-conventions/blob/main/semantic_conventions/trace/general.yaml
        activity?.SetTag("thread.id", Environment.CurrentManagedThreadId);
        activity?.SetTag("thread.name", Thread.CurrentThread.Name);
        activity?.SetTag("enduser.id", Thread.CurrentPrincipal?.Identity?.Name);
        SetLineNumber(activity);
        
        return activity;

        void SetLineNumber(Activity? a, [CallerLineNumber] int lineNumber = 0)
        {
            a?.SetTag("code.lineno", lineNumber);
        }
    }
}
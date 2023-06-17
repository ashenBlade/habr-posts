using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.RecordSaver.Worker.Database;
using OpenTelemetry.Trace;

namespace OpenTelemetry.RecordSaver.Worker;

public class KafkaConsumer : BackgroundService
{
    private readonly ILogger<KafkaConsumer> _logger;
    private readonly Tracer _tracer;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

    public KafkaConsumer(ILogger<KafkaConsumer> logger, Tracer tracer, IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _logger = logger;
        _tracer = tracer;
        _dbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogInformation("Создаю консьюмера кафки");
        
        using var consumer = new ConsumerBuilder<Null, string>(new ConsumerConfig()
            {
                BootstrapServers = "kafka:9092",
                GroupId = "Consumer"
            })
           .Build();
        _logger.LogInformation("Подписываюсь на очередь");
        consumer.Subscribe("weather");
        _logger.LogInformation("Создаю DbContext");
        await using var context = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
        _logger.LogInformation("Начинаю работу");
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            if (result is null)
            {
                _logger.LogInformation("Из консьюмера вернулся null. Закачиваю работу");
                break;
            }

            var propagationContext = Propagators.DefaultTextMapPropagator.Extract(default, result.Message.Headers,
                (headers, key) => headers.Where(x => x.Key == key)
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
                                         .Where(x => x is not null));

            
            using var span = Tracing.ConsumerActivitySource.StartActivity(
                Tracing.KafkaMessageProcessing, 
                kind: ActivityKind.Consumer,
                parentContext: propagationContext.ActivityContext,
                links: new ActivityLink[]{new(propagationContext.ActivityContext)});

            Baggage.Current = propagationContext.Baggage;
            
            _logger.LogInformation("Сохраняю запись: {Record}", result.Message.Value);
            
            var x = await context.AddAsync(new Record() {Value = result.Message.Value}, stoppingToken);
            await context.SaveChangesAsync(stoppingToken);
            x.State = EntityState.Detached;
            
            _logger.LogInformation("Сохранение закончено");
        }
    }
}
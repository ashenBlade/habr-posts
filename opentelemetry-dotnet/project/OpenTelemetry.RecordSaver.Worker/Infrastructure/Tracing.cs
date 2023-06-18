using System.Diagnostics;
using System.Reflection;

namespace OpenTelemetry.RecordSaver.Worker.Infrastructure;

public static class Tracing
{
    private static readonly AssemblyName CurrentAssembly = typeof(Tracing).Assembly.GetName();
    private static string Version => CurrentAssembly.Version!.ToString();
    private static string AssemblyName => CurrentAssembly.Name!;
    public static readonly ActivitySource ConsumerActivitySource = new(AssemblyName, Version);
    
    public const string KafkaMessageProcessing = "Обработка сообщения из кафки";
}
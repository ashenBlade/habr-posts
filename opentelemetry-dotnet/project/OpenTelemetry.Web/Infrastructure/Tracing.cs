using System.Diagnostics;
using System.Reflection;

namespace OpenTelemetry.Web.Infrastructure;

public static class Tracing
{
    private static readonly AssemblyName CurrentAssembly = typeof(Tracing).Assembly.GetName();
    private static string Version => CurrentAssembly.Version!.ToString();
    private static string AssemblyName => CurrentAssembly.Name!;
    
    public static readonly ActivitySource WebActivitySource = new(AssemblyName, Version);
    public const string StateRequest = "Получение состояния системы";
}
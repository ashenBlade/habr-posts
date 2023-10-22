using Examples;

var bytes = "123456789"u8.ToArray();
var bitResult = CrcComputer.ComputePerBit(bytes);
var tableSimpleResult = CrcComputer.ComputeTableSimple(bytes);
var tableOptimizedResult = CrcComputer.ComputeTableOptimized(bytes);
Console.WriteLine($"Стратегия\t\tРезультат");
Console.WriteLine($"Побитовая\t\t{bitResult}");
Console.WriteLine($"Табличная простая\t{tableSimpleResult}");
Console.WriteLine($"Табличная оптимиз\t{tableOptimizedResult}");

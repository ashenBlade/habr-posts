using Examples;

var bitResult = CrcComputer.ComputePerBit(new byte[] {0b11010001});
Console.WriteLine(Convert.ToString(bitResult, 2));
// ValidateComputedPolynomial();
//
// var bytes = "123456789"u8.ToArray();
// var bitResult = CrcComputer.ComputePerBit(bytes);
// var tableSimpleResult = CrcComputer.ComputeTableSimple(bytes);
// var tableOptimizedResult = CrcComputer.ComputeTableOptimized(bytes);
// Console.WriteLine($"Стратегия\t\tРезультат");
// Console.WriteLine($"Побитовая\t\t{bitResult}");
// Console.WriteLine($"Табличная, простая\t{tableSimpleResult}");
// Console.WriteLine($"Табличная, оптимиз\t{tableOptimizedResult}");

void ValidateComputedPolynomial()
{
    var computedTableInitialValue = CrcComputer.ComputeNewTableValue(CrcComputer.InitialSimpleRegister);
    if (computedTableInitialValue != CrcComputer.InitialOptimizedRegister)
    {
        throw new Exception($"Рассчитанное начальное значение регистра не равно константному\nРассчитанное:\t{computedTableInitialValue}\nКонстанта:\t{CrcComputer.InitialOptimizedRegister}");
    }
}
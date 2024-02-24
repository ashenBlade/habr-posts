using BenchmarkDotNet.Running;
using FileWrite.Benchmarks;

BenchmarkRunner.Run<BufferedVsUnbufferedWriteBenchmarks>();

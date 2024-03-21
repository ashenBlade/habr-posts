using BenchmarkDotNet.Running;
using FileWrite.Benchmarks;

BenchmarkRunner.Run<AlignedWriteBenchmarks>();
BenchmarkRunner.Run<UnalignedWriteBenchmarks>();

using BenchmarkDotNet.Attributes;

namespace FileWrite.Benchmarks;

public class FileWriteBenchmarks
{
    public const string FirstFileName = "first-file";
    public const string SecondFileName = "second-file";
    
    private const int IterationsCount = 1024 * 16;
    
    private FileStream _directFileStream = null!;
    private FileStream _bufferedFileStream = null!;
    private byte[] _chunk = Array.Empty<byte>();

    [GlobalSetup]
    public void GlobalSetup()
    {
        const int sectorSize = 512;
        const int bufferSize = 4096;
        
        _directFileStream = new FileStream(FirstFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 0, FileOptions.WriteThrough);
        _bufferedFileStream = new FileStream(SecondFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: bufferSize, FileOptions.WriteThrough);
        
        _chunk = new byte[sectorSize];
        var random = new Random(42);
        random.NextBytes(_chunk);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _directFileStream.Close();
        _bufferedFileStream.Close();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _directFileStream.SetLength(0);
        _bufferedFileStream.SetLength(0);
    }

    private void WriteTestBase(Stream stream)
    {
        for (int i = 0; i < IterationsCount; i++)
        {
            stream.Write(_chunk);
        }
        stream.Flush();
    }

    [Benchmark]
    public void DirectFileWrite() => WriteTestBase(_directFileStream);

    [Benchmark]
    public void BufferedFileWrite() => WriteTestBase(_bufferedFileStream);
}
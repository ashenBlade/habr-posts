using BenchmarkDotNet.Attributes;

namespace FileWrite.Benchmarks;

public class BufferedVsUnbufferedWriteBenchmarks
{
    public const string FileName = "sample.txt";
    private const int IterationsCount = 1024 * 16;
    
    private FileStream _fileStream = null!;
    private BufferedStream _bufferedStream = null!;
    private byte[] _chunk = Array.Empty<byte>();

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fileStream = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 0, FileOptions.WriteThrough);
        _bufferedStream = new BufferedStream(_fileStream, 2 * 4 * 1024);
        _chunk = new byte[1024 * 4];
        var random = new Random(42);
        random.NextBytes(_chunk);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fileStream.Close();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // Очищаем файл
        _fileStream.SetLength(0);
    }

    [Benchmark]
    public void DirectFileWrite()
    {
        for (int i = 0; i < IterationsCount; i++)
        {
            _fileStream.Write(_chunk);
        }
        _fileStream.Flush();
    }

    [Benchmark]
    public void BufferedFileWrite()
    {
        for (int i = 0; i < IterationsCount; i++)
        {
            _bufferedStream.Write(_chunk);
        }
        
        _bufferedStream.Flush();
    }
}
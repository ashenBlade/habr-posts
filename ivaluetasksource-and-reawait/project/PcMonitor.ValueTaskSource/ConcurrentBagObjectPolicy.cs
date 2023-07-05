using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace PcMonitor.ValueTaskSource;

public class ConcurrentBagObjectPolicy<T>: IPooledObjectPolicy<T>, IDisposable where T : notnull, IDisposable
{
    private readonly Func<T> _factory;
    private readonly ConcurrentBag<T> _bag = new();

    public ConcurrentBagObjectPolicy(Func<T> factory)
    {
        _factory = factory;
    }
    
    public T Create()
    {
        if (_bag.TryTake(out var item))
        {
            return item;
        }

        return _factory();
    }

    public bool Return(T obj)
    {
        _bag.Add(obj);
        return true;
    }

    public void Dispose()
    {
        foreach (var item in _bag)
        {
            item.Dispose();
        }
        _bag.Clear();
    }
}
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
namespace Damper.Infrastructure.Repositories;

public class CachedCustomerRepository : ICustomerRepository
{
    private readonly ICustomerRepository _durableRepo;
    private readonly IMemoryCache _memoryCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CachedCustomerRepository(ICustomerRepository innerRepository, IMemoryCache memoryCache)
    {
        _durableRepo = innerRepository;
        _memoryCache = memoryCache;
    }

    public async Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct)
    {
        string cacheKey = CacheKey(customerId);
        CustomerConfig? cachedConfig;

        // Look for the config in cache first - if it's there, get out of here!
        if (_memoryCache.TryGetValue(cacheKey, out cachedConfig))
        {
            return cachedConfig;
        }

        // Cache miss: Go fetch from the durable repository using single flight pattern
        var sem = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            // Try the cache again in case another thread put it in there - if it hits, get out of here!
            if (_memoryCache.TryGetValue(cacheKey, out cachedConfig))
            {
                return cachedConfig;
            }
            
            var realConfig = await _durableRepo.GetByIdAsync(customerId, ct)
                                               .ConfigureAwait(continueOnCapturedContext: false);
            if (realConfig != null)
            {
                // Add to cache with an absolute expiration window
                _memoryCache.Set(cacheKey, realConfig, CacheDuration);
            }
            return realConfig;
        }
        finally
        {
            sem.Release();
            // Try to remove the semaphore if no one else is waiting to avoid unbounded growth.
            // SemaphoreSlim.CurrentCount == 1 means it's not being waited upon (we created with 1, waited -> 0 -> released -> 1).
            // This is a heuristic — races are possible but acceptable for cleanup.
            if (sem.CurrentCount == 1)
            {
                _locks.TryRemove(cacheKey, out _);
                sem.Dispose();
            }
        }
    }

    public void Invalidate(string customerId)
    {
        _memoryCache.Remove(CacheKey(customerId));
    }

    public void Update(string customerId, CustomerConfig config)
    {
        _memoryCache.Set(CacheKey(customerId), config, CacheDuration);
    }

    private static string CacheKey(string customerId) => $"customer-{customerId}";
}
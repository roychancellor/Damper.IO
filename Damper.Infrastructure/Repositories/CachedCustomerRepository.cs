using Microsoft.Extensions.Caching.Memory;

namespace Damper.Infrastructure.Repositories;

public class CachedCustomerRepository : ICustomerRepository
{
    private readonly ICustomerRepository _durableRepo;
    private readonly IMemoryCache _memoryCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public CachedCustomerRepository(ICustomerRepository innerRepository, IMemoryCache memoryCache)
    {
        _durableRepo = innerRepository;
        _memoryCache = memoryCache;
    }

    public async Task<CustomerConfig?> GetByIdAsync(string customerId)
    {
        string cacheKey = $"customer-{customerId}";

        // Look for the config in cache first
        if (_memoryCache.TryGetValue(cacheKey, out CustomerConfig? cachedConfig))
        {
            return cachedConfig;
        }

        // Cache miss: Go fetch from the durable repository
        var realConfig = await _durableRepo.GetByIdAsync(customerId);

        if (realConfig != null)
        {
            // Save to cache with an absolute expiration window
            _memoryCache.Set(cacheKey, realConfig, CacheDuration);
        }

        return realConfig;
    }
}
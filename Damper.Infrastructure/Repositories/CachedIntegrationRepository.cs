using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
namespace Damper.Infrastructure.Repositories;

public class CachedIntegrationRepository : IIntegrationRepository
{
    private readonly IIntegrationRepository _durableRepo;
    private readonly IMemoryCache _memoryCache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private IOptionsMonitor<AppSettings> _optMon;

    public CachedIntegrationRepository(IIntegrationRepository innerRepository, IMemoryCache memoryCache, IOptionsMonitor<AppSettings> optMon)
    {
        _durableRepo = innerRepository;
        _memoryCache = memoryCache;
        _optMon = optMon;
    }

    public async Task<Integration?> GetByIdAsync(long integrationId, CancellationToken ct)
    {
        string cacheKey = CacheKey(integrationId);

        // Look for the integration in cache first - if it's there, get out of here!
        if (_memoryCache.TryGetValue(cacheKey, out Integration? cachedInteg))
        {
            return cachedInteg;
        }

        // Cache miss: Go fetch from the durable repository using single flight pattern
        var sem = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(CancellationToken.None);
        try
        {
            // Try the cache again in case another thread put it in there - if it hits, get out of here!
            if (_memoryCache.TryGetValue(cacheKey, out cachedInteg))
            {
                return cachedInteg;
            }
            
            var realConfig = await _durableRepo.GetByIdAsync(integrationId, ct)
                                               .ConfigureAwait(continueOnCapturedContext: false);
            if (realConfig != null)
            {
                // Add to cache with an absolute expiration window
                _memoryCache.Set(cacheKey, realConfig, GetCacheTimeToLive());
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

    private TimeSpan GetCacheTimeToLive()
    {
        return TimeSpan.FromMinutes(_optMon.CurrentValue.RepositorySettings.CacheTimeToLiveMinutes);
    }

    public void Invalidate(long integrationId)
    {
        _memoryCache.Remove(CacheKey(integrationId));
    }

    public void Update(long integrationId, Integration config)
    {
        _memoryCache.Set(CacheKey(integrationId), config, GetCacheTimeToLive());
    }

    private static string CacheKey(long integrationId) => $"integration-{integrationId}";
    private static string CacheKey(string apiKey) => $"apikey-{apiKey}";

    // TODO: Implement the repository methods
    public async Task<Integration?> GetByApiKeyAsync(ApiKey apiKey, CancellationToken ct = default)
    {
        string cacheKey = CacheKey(apiKey.Reveal());

        // Look for the integration in cache first - if it's there, get out of here!
        if (_memoryCache.TryGetValue(cacheKey, out Integration? cachedInteg))
        {
            return cachedInteg;
        }

        // Cache miss: Go fetch from the durable repository using single flight pattern
        var sem = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(CancellationToken.None);
        try
        {
            // Try the cache again in case another thread put it in there - if it hits, get out of here!
            if (_memoryCache.TryGetValue(cacheKey, out cachedInteg))
            {
                return cachedInteg;
            }
            
            var realConfig = await _durableRepo.GetByApiKeyAsync(apiKey, ct)
                                               .ConfigureAwait(continueOnCapturedContext: false);
            if (realConfig != null)
            {
                // Add to cache with an absolute expiration window
                _memoryCache.Set(cacheKey, realConfig, GetCacheTimeToLive());
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

    public Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task SaveAsync(Integration integration, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
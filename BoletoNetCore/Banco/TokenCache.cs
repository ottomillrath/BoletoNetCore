using System.Collections.Concurrent;
using System;
using System.Linq;

namespace BoletoNetCore
{
    /// <summary>
    /// Classe responsável por salvar tokens para uso posterior
    /// Implementada para suportar o token por webhook do banco Cecred
    /// </summary>
    public class TokenCache : IDisposable
    {
        private static ConcurrentDictionary<long, TokenInfo> _tokenCache;

        public TokenCache()
        {
            if (_tokenCache == null) _tokenCache = new ConcurrentDictionary<long, TokenInfo>();
        }

        private class TokenInfo
        {
            public string Token { get; set; }
            public DateTime Expiration { get; set; }
        }

        public void AddOrUpdateToken(long id, string token, DateTime expiration)
        {
            var tokenInfo = new TokenInfo
            {
                Token = token,
                Expiration = expiration
            };

            _tokenCache.AddOrUpdate(id, tokenInfo, (key, existingVal) => tokenInfo);
        }

        public string GetToken(long id)
        {
            if (_tokenCache.TryGetValue(id, out TokenInfo tokenInfo))
                if (tokenInfo.Expiration > DateTime.UtcNow)
                    return tokenInfo.Token;
                else
                    RemoveToken(id);

            return null;
        }

        public void RemoveToken(long id)
        {
            _tokenCache.TryRemove(id, out _);
        }

        public void CleanExpiredTokens()
        {
            var expiredTokens = _tokenCache.Where(kvp => kvp.Value.Expiration <= DateTime.UtcNow).Select(kvp => kvp.Key).ToList();

            foreach (var id in expiredTokens)
                RemoveToken(id);
        }

        public void Dispose()
        {
            CleanExpiredTokens();
        }
    }
}

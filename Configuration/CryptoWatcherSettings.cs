using System.Collections.Generic;

namespace CryptoWatcher.Configuration
{
    public class CryptoWatcherSettings
    {
        public CryptoWatcherMode Mode { get; init; }
        
        public Dictionary<string, string[]> Markets { get; init; }

    }
}
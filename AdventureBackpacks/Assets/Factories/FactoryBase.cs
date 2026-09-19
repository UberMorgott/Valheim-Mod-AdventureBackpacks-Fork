using AdventureBackpacks.Configuration;

namespace AdventureBackpacks.Assets.Factories;

public abstract class FactoryBase
{
    private ModLogger _logger;
    private ConfigRegistry _config;

    internal ModLogger Log => _logger;
    internal ConfigRegistry Config => _config;

    private protected FactoryBase(ModLogger logger, ConfigRegistry configs)
    {
        _logger = logger;
        _config = configs;
    }
}

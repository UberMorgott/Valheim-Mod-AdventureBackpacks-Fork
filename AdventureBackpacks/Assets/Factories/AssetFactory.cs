using AdventureBackpacks.Configuration;

namespace AdventureBackpacks.Assets.Factories;

internal abstract class AssetFactory : FactoryBase
{
    protected AssetFactory(ModLogger logger, ConfigRegistry configs) : base(logger, configs)
    {
    }

    internal abstract void CreateAssets();
}

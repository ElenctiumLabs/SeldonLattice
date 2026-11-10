using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Pqc;
using SeldonLattice.Infrastructure.Services;
using SeldonLattice.Infrastructure.Storage;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// The Merkle store and its supporting services: ML-DSA-65 post-quantum signing of every
/// stored node, the sigil generator, and the startup-time health probe.
/// </summary>
public static class MerkleStoreExtensions
{
    public static void AddSeldonLatticeMerkleStore(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add FASTER Merkle Store.
        // Default the data dir once here: both the pqc-keys dir and the store dir
        // below combine against it, and running the CLI from a directory without
        // appsettings.json leaves this null (line 31 used to crash on it).
        var dataDir = configuration["DataDir"] ?? "data";
        var historyLimit = configuration.GetValue("Defaults:HistoryLimit", 100);

        // Post-quantum signing (ML-DSA-65): sign every stored node, verify on read.
        var pqcKeysDir = Path.Combine(dataDir, "pqc-keys");
        services.AddSingleton<IPqcSigner>(sp =>
            new MLDsaSigner(pqcKeysDir, sp.GetRequiredService<ILogger<MLDsaSigner>>()));

        services.AddSingleton<IMerkleStore>(sp =>
        {
            var raw = new FasterMerkleStore(Path.Combine(dataDir, "merkle"), historyLimit);
            return new SignedMerkleStore(
                raw,
                sp.GetRequiredService<IPqcSigner>(),
                sp.GetRequiredService<ILogger<SignedMerkleStore>>());
        });

        // Add Sigil Generator
        services.Configure<SigilOptions>(configuration.GetSection("Sigil"));
        services.AddSingleton<ISigilGenerator, SigilGenerator>();

        // Add App Info for health checks (tracks startup time via IHostApplicationLifetime)
        services.AddSingleton<AppInfo>(sp =>
        {
            var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
            var info = new AppInfo(DateTime.UtcNow);
            lifetime.ApplicationStarted.Register(() => info.SetStarted(DateTime.UtcNow));
            return info;
        });
    }
}

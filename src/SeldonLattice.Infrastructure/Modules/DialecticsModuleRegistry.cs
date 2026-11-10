using System.Collections.Concurrent;
using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Infrastructure.Modules;

/// <summary>
/// Implementation of the module registry for managing dialectics modules.
/// Supports both pre-configured modules and custom modules created at runtime.
/// </summary>
public class DialecticsModuleRegistry : IDialecticsModuleRegistry
{
    private readonly ConcurrentDictionary<string, IDialecticsModule> _modules = new();
    private readonly ConcurrentDictionary<string, Func<IServiceProvider, IDialecticsModule>> _scopedResolvers = new();
    private readonly ConcurrentDictionary<string, ModuleConfiguration> _definitions = new();

    public void RegisterModule(string key, IDialecticsModule module)
    {
        RegisterModule(key, module, scopedResolver: null);
    }

    public void RegisterModule(string key, IDialecticsModule module, Func<IServiceProvider, IDialecticsModule>? scopedResolver)
    {
        _modules[key] = module;
        if (scopedResolver is not null)
            _scopedResolvers[key] = scopedResolver;
    }

    public IDialecticsModule GetModule(string key)
    {
        if (!_modules.TryGetValue(key, out var module))
            throw new KeyNotFoundException($"Module '{key}' not found. Available: {string.Join(", ", _modules.Keys)}");
        return module;
    }

    public IDialecticsModule GetModule(string key, IServiceProvider services)
    {
        if (_scopedResolvers.TryGetValue(key, out var resolver))
            return resolver(services);
        return GetModule(key);
    }

    public IEnumerable<string> GetAvailableModules() => _modules.Keys;

    public void RegisterModuleDefinition(string key, ModuleConfiguration configuration)
    {
        _definitions[key] = configuration;
    }

    public ModuleConfiguration? GetModuleDefinition(string key) =>
        _definitions.GetValueOrDefault(key);

    public IEnumerable<ModuleConfiguration> GetAllModuleDefinitions() =>
        _definitions.Values;
}

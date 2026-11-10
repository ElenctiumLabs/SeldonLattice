using SeldonLattice.Domain.Configuration;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Registry for managing multiple dialectics modules.
/// Allows the system to support philosophy, research, and runtime-created custom modules
/// without hardcoding their implementations.
/// </summary>
public interface IDialecticsModuleRegistry
{
    /// <summary>
    /// Registers a new module.
    /// </summary>
    void RegisterModule(string key, IDialecticsModule module);

    /// <summary>
    /// Registers a module together with a resolver that produces a fresh, request-scoped
    /// instance of the module from a supplied service provider. Used by startup-registered
    /// modules so that scoped services held by the module (agents, mediators, ascent
    /// services and their per-request histories) are never shared across concurrent
    /// HTTP requests.
    /// </summary>
    void RegisterModule(string key, IDialecticsModule module, Func<IServiceProvider, IDialecticsModule>? scopedResolver);

    /// <summary>
    /// Retrieves a registered module by its key.
    /// <para>
    /// Prefer <see cref="GetModule(string, IServiceProvider)"/> in request handlers so each
    /// request gets its own scoped module instance. This parameterless overload returns the
    /// startup-registered instance and is intended for metadata/introspection only.
    /// </para>
    /// Throws KeyNotFoundException if the module is not registered.
    /// </summary>
    IDialecticsModule GetModule(string key);

    /// <summary>
    /// Retrieves a registered module by its key, resolving a fresh request-scoped instance
    /// from the supplied service provider when a scoped resolver was registered.
    /// Throws KeyNotFoundException if the module is not registered.
    /// </summary>
    IDialecticsModule GetModule(string key, IServiceProvider services);

    /// <summary>
    /// Gets all registered module keys.
    /// </summary>
    IEnumerable<string> GetAvailableModules();

    /// <summary>
    /// Registers a custom module configuration for later retrieval.
    /// </summary>
    void RegisterModuleDefinition(string key, ModuleConfiguration configuration);

    /// <summary>
    /// Gets a custom module definition by key, or null if not found.
    /// </summary>
    ModuleConfiguration? GetModuleDefinition(string key);

    /// <summary>
    /// Gets all registered custom module definitions.
    /// </summary>
    IEnumerable<ModuleConfiguration> GetAllModuleDefinitions();
}
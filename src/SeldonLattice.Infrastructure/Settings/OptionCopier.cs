using System.Reflection;

namespace SeldonLattice.Infrastructure.Settings;

/// <summary>
/// Copies the public settable properties of one options object into another.
///
/// The engine options are registered as singletons and the agents cache the instance
/// (<c>private readonly DialecticsOptions _options = options.Value;</c>). Replacing the
/// object would leave those agents on stale values, so a settings save updates the live
/// instance in place. Reflection also handles the <c>init</c>-only properties on the
/// options records; the CLR treats <c>init</c> as a normal setter with a modreq.
/// </summary>
internal static class OptionCopier
{
    public static void Copy<T>(T source, T target) where T : class
    {
        if (ReferenceEquals(source, target)) return;

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetIndexParameters().Length != 0) continue;

            property.SetValue(target, property.GetValue(source));
        }
    }
}

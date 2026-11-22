using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features;

/// <summary>
/// Resolves a service only when its value is first used, so commands can take
/// configuration-dependent dependencies without loading the configuration up front.
/// </summary>
internal sealed class LazyService<T>(IServiceProvider provider)
    : Lazy<T>(provider.GetRequiredService<T>)
    where T : notnull;

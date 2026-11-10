using SeldonLattice.API.Middleware;

namespace SeldonLattice.API.Extensions;

public static class ObservabilityMiddlewareExtensions
{
    public static void UseObservability(this IApplicationBuilder builder)
    {
        builder.UseMiddleware<ObservabilityMiddleware>();
    }
}
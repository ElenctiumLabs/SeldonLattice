namespace SeldonLattice.API.Endpoints;

/// <summary>
/// Single composition point for every API route group. Each group has its own
/// <c>MapXxxEndpoints</c> extension (see this folder); this method just names them all so the
/// host only has one line of routing to read.
/// </summary>
public static class EndpointMappingExtensions
{
    public static void MapSeldonLatticeEndpoints(this WebApplication app)
    {
        app.MapControllers();
    }
}

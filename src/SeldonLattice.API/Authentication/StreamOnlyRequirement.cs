using Microsoft.AspNetCore.Authorization;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Requirement for the "StreamOnly" policy: the request is authorized either by an
/// authenticated API key that carries the <c>stream</c> role, OR by a valid short-lived
/// stream token supplied via the query string (for EventSource, which cannot attach
/// headers). Stream tokens are only ever accepted on the SSE stream endpoints.
/// </summary>
public sealed class StreamOnlyRequirement : IAuthorizationRequirement
{
}

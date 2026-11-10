namespace SeldonLattice.API.Models;
public record CreateSessionRequest(string RootQuestion, string? Framework, string? OverrideReason = null);

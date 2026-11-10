namespace SeldonLattice.API;

public record IngestSourceRequest(string SourceId, string Query, int MaxResults = 50); // Default overridden via call-site; config value used in IngestionOrchestrator
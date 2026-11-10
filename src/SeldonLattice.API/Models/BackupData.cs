namespace SeldonLattice.API.Models;

public class BackupData
{
    public DateTime ExportedAt { get; set; }
    public int NodeCount { get; set; }
    public List<BackupNode> Nodes { get; init; } = [];
}
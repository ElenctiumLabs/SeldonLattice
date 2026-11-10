using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Verification;

namespace SeldonLattice.API.Controllers;

public partial class SessionController
{
    /// <summary>
    /// Exports a session as transcript (Markdown, default), graph (JSON), or report (plain text).
    /// </summary>
    [HttpGet("{sessionId}/export")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> ExportSession(
        string sessionId,
        [FromQuery] string format = "transcript")
    {
        try
        {
            var session = await GetOwnedSessionAsync(sessionId);
            if (session == null)
                return Results.NotFound($"Session '{sessionId}' not found.");

            var nodesTask = GetSessionNodesAsync(sessionId);
            var reportTask = verifier.VerifySessionAsync(sessionId);
            var exportNodesTask = format.ToLowerInvariant() == "json"
                ? exportVerifier.BuildExportNodesAsync(sessionId)
                : Task.FromResult<IReadOnlyList<ExportNode>>([]);
            await Task.WhenAll(nodesTask, reportTask, exportNodesTask);

            var nodes = nodesTask.Result;
            var report = reportTask.Result;
            var exportNodes = exportNodesTask.Result;
            var turns = nodes
                .Select((n, i) => MapToHistoryTurn(n, i + 1))
                .ToList();

            return format.ToLowerInvariant() switch
            {
                "json" => Results.Ok(GenerateJsonExport(session, turns, report, exportNodes)),

                "report" => Results.File(
                    Encoding.UTF8.GetBytes(GenerateTextReport(session, turns)),
                    "text/plain",
                    $"seldon-lattice-session-{sessionId}.txt"),

                _ => Results.File(
                    Encoding.UTF8.GetBytes(GenerateMarkdownExport(session, turns)),
                    "text/markdown",
                    $"seldon-lattice-session-{sessionId}.md"),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export session {SessionId}", sessionId);
            return Results.Problem("An error occurred while exporting the session.", statusCode: 500);
        }
    }

    /// <summary>
    /// Generates an HTML/PDF report for a session with sigils and quality metrics.
    /// </summary>
    [HttpPost("{sessionId}/export/report")]
    [Authorize(Policy = "WriterOnly")]
    public async Task<IResult> ExportSessionReportHtml(string sessionId)
    {
        try
        {
            var session = await GetOwnedSessionAsync(sessionId);
            if (session == null)
                return Results.NotFound($"Session '{sessionId}' not found.");

            var nodesTask = GetSessionNodesAsync(sessionId);
            var reportTask = verifier.VerifySessionAsync(sessionId);
            await Task.WhenAll(nodesTask, reportTask);

            var nodes = nodesTask.Result;
            var report = reportTask.Result;
            var turns = nodes
                .Select((n, i) => MapToHistoryTurn(n, i + 1))
                .ToList();

            // Only scored turns contribute: question and synthesis nodes carry null
            // because the quality gates never ran on them. Averaging in a substituted
            // zero would drag the report's mean toward a value nobody measured.
            var qualityScores = turns
                .Select(t => t.QualityScore)
                .Where(s => s.HasValue)
                .Select(s => (float)s!.Value)
                .ToList();
            var avgScore = qualityScores.Count > 0 ? qualityScores.Average() : 0;
            var maxScore = qualityScores.Count > 0 ? qualityScores.Max() : 0;
            var minScore = qualityScores.Count > 0 ? qualityScores.Min() : 0;

            var html = GenerateHtmlReport(session, turns, avgScore, maxScore, minScore, report);

            return Results.Content(html, "text/html");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate report for session {SessionId}", sessionId);
            return Results.Problem("An error occurred while generating the report.", statusCode: 500);
        }
    }

    private static string GenerateMarkdownExport(Session session, List<HistoryTurn> turns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Seldon Lattice Session Report");
        sb.AppendLine();
        sb.AppendLine($"**Session ID:** `{session.SessionId}`");
        sb.AppendLine($"**Framework:** {session.Framework}");
        sb.AppendLine($"**Created:** {session.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"**Last Updated:** {session.LastUpdatedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"**Root Question:** {session.RootQuestion}");
        sb.AppendLine($"**Total Turns:** {turns.Count}");
        sb.AppendLine($"**Max Level:** {session.MaxLevel}");
        sb.AppendLine();

        foreach (var turn in turns)
        {
            sb.AppendLine($"## Turn {turn.TurnNumber} - {turn.Speaker} ({turn.Role})");
            sb.AppendLine($"**Timestamp:** {turn.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");
            sb.AppendLine($"**Quality Score:** {turn.QualityScore:F2}");
            if (turn.IsSustained) sb.AppendLine("**Sustained theme:** Yes");
            if (turn.CeremonyScore is not null) sb.AppendLine($"**Ceremony:** {turn.CeremonyScore:F2}");
            if (turn.SimilarityToPrevious is not null) sb.AppendLine($"**Similarity to previous:** {turn.SimilarityToPrevious:F3}");
            if (turn.AscentScore != 0) sb.AppendLine($"**Ascent score:** {turn.AscentScore:F3}");
            if (turn.JoinMediation is not null) sb.AppendLine($"**Join mediation:** {turn.JoinMediation:F3}");
            if (turn.JoinNovelty is not null) sb.AppendLine($"**Join novelty:** {turn.JoinNovelty:F3}");
            if (turn.IrreducibleTension != 0) sb.AppendLine($"**Irreducible tension:** {turn.IrreducibleTension:F3}");
            if (!string.IsNullOrWhiteSpace(turn.RemainingDissensus)) sb.AppendLine($"**Remaining dissensus:** {turn.RemainingDissensus}");
            if (turn.RepeatsPremise is not null) sb.AppendLine($"**Repeats premise:** {turn.RepeatsPremise:P1}");
            if (turn.UnresolvedTensions is { Length: > 0 }) sb.AppendLine("**Unresolved tensions:** " + string.Join("; ", turn.UnresolvedTensions));
            if (turn.TensionIntensity is not null) sb.AppendLine($"**Tension intensity:** {turn.TensionIntensity:F2}");
            if (turn.NewArgumentCount is not null) sb.AppendLine($"**New arguments:** {turn.NewArgumentCount}");
            if (turn.WordCount is not null) sb.AppendLine($"**Word count:** {turn.WordCount}");
            if (turn.SelfSimilarity is not null) sb.AppendLine($"**Self similarity:** {turn.SelfSimilarity:F3}");
            if (turn.ReasoningTrace is not null) sb.AppendLine($"**Reasoning trace:** {turn.ReasoningTrace}");
            sb.AppendLine();
            sb.AppendLine(turn.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static object GenerateJsonExport(Session session, List<HistoryTurn> turns, SessionVerificationReport? report = null, IReadOnlyList<ExportNode>? nodes = null)
    {
        var verify = report == null ? null : new
        {
            verified = report.Passed,
            nodesChecked = report.NodesChecked,
            signaturesVerified = report.SignaturesVerified,
            pointerIssues = report.PointerIssues,
            signatureIssues = report.SignatureIssues,
            issues = report.Issues,
        };

        return new
        {
            exportVersion = "1.1",
            exportedAt = DateTime.UtcNow,
            sessionId = session.SessionId,
            session = new
            {
                session.SessionId,
                session.Framework,
                session.RootQuestion,
                session.CreatedAt,
                session.LastUpdatedAt,
                session.NodeCount,
                session.MaxLevel
            },
            verify,
            nodes = nodes?.Select(n => new
            {
                n.Kind,
                n.Cid,
                n.Level,
                n.SpeakerId,
                n.Timestamp,
                n.BlobBase64,
                n.Links,
                n.Signature,
            }),
            turns = turns.Select(t => new
            {
                t.TurnNumber,
                t.Speaker,
                t.Role,
                t.Content,
                t.Timestamp,
                t.QualityScore,
                t.Level,
                metrics = new
                {
                    t.CeremonyScore, t.SimilarityToPrevious, t.AscentScore, t.JoinMediation,
                    t.JoinNovelty, t.IrreducibleTension, t.NewArgumentCount, t.WordCount,
                    t.HasFarewell, t.HasAgreement, t.HasDisagreement, t.SelfSimilarity
                },
                markers = new
                {
                    t.IsSustained, t.RemainingDissensus, t.RepeatsPremise,
                    t.UnresolvedTensions, t.TensionIntensity
                },
                t.ReasoningTrace
            })
        };
    }

    private static string GenerateTextReport(Session session, List<HistoryTurn> turns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Seldon Lattice Session Report ===");
        sb.AppendLine();
        sb.AppendLine($"Session ID:   {session.SessionId}");
        sb.AppendLine($"Framework:    {session.Framework}");
        sb.AppendLine($"Created:      {session.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Last Updated: {session.LastUpdatedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Root Question: {session.RootQuestion}");
        sb.AppendLine($"Total Turns:  {turns.Count}");
        sb.AppendLine($"Max Level:    {session.MaxLevel}");
        sb.AppendLine();
        sb.AppendLine("=== Debate Transcript ===");
        sb.AppendLine();

        foreach (var turn in turns)
        {
            sb.AppendLine($"[Turn {turn.TurnNumber}] {turn.Speaker} ({turn.Role}) - Level {turn.Level}");
            sb.AppendLine($"  Score: {turn.QualityScore:F2}");
            if (turn.IsSustained) sb.AppendLine("  Sustained theme: Yes");
            if (turn.CeremonyScore is not null) sb.AppendLine($"  Ceremony: {turn.CeremonyScore:F2}");
            if (turn.SimilarityToPrevious is not null) sb.AppendLine($"  Similarity: {turn.SimilarityToPrevious:F3}");
            if (turn.AscentScore != 0) sb.AppendLine($"  Ascent score: {turn.AscentScore:F3}");
            if (turn.JoinMediation is not null) sb.AppendLine($"  Join mediation: {turn.JoinMediation:F3}");
            if (turn.JoinNovelty is not null) sb.AppendLine($"  Join novelty: {turn.JoinNovelty:F3}");
            if (turn.IrreducibleTension != 0) sb.AppendLine($"  Irreducible tension: {turn.IrreducibleTension:F3}");
            if (turn.RemainingDissensus is not null) sb.AppendLine($"  Remaining dissensus: {turn.RemainingDissensus}");
            if (turn.RepeatsPremise is not null) sb.AppendLine($"  Repeats premise: {turn.RepeatsPremise:P1}");
            if (turn.UnresolvedTensions is { Length: > 0 }) sb.AppendLine("  Unresolved tensions: " + string.Join("; ", turn.UnresolvedTensions));
            if (turn.TensionIntensity is not null) sb.AppendLine($"  Tension intensity: {turn.TensionIntensity:F2}");
            if (turn.NewArgumentCount is not null) sb.AppendLine($"  New arguments: {turn.NewArgumentCount}");
            if (turn.WordCount is not null) sb.AppendLine($"  Word count: {turn.WordCount}");
            sb.AppendLine($"  Content: {turn.Content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a printable HTML report for a session.
    /// Includes sigil visualization data and quality metrics.
    /// </summary>
    private static string GenerateHtmlReport(
        Session session,
        List<HistoryTurn> turns,
        float avgScore,
        float maxScore,
        float minScore,
        SessionVerificationReport? verify = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        sb.AppendLine("  <title>Seldon Lattice Report - " + session.SessionId + "</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: 'Courier New', monospace; background: #0f1218; color: #c4b59d; margin: 0; padding: 2rem; }");
        sb.AppendLine("    h1 { color: #e0c382; border-bottom: 1px solid #2a3340; padding-bottom: 0.5rem; }");
        sb.AppendLine("    h2 { color: #e0c382; margin-top: 2rem; }");
        sb.AppendLine("    .meta { color: #8a9a7b; font-size: 0.875rem; line-height: 1.8; }");
        sb.AppendLine("    .turn { border-left: 2px solid #b8924a; margin: 1.5rem 0; padding-left: 1rem; }");
        sb.AppendLine("    .turn-header { color: #e0c382; font-weight: bold; font-size: 0.9rem; }");
        sb.AppendLine("    .turn-content { margin-top: 0.5rem; white-space: pre-wrap; }");
        sb.AppendLine("    .metrics { display: flex; gap: 2rem; margin-top: 2rem; }");
        sb.AppendLine("    .metric { text-align: center; }");
        sb.AppendLine("    .metric-value { font-size: 1.5rem; color: #e0c382; }");
        sb.AppendLine("    .metric-label { font-size: 0.8rem; color: #8a9a7b; }");
        sb.AppendLine("    .verify-pass { color: #7c9d6e; font-weight: bold; }");
        sb.AppendLine("    .verify-fail { color: #c05f5f; font-weight: bold; }");
        sb.AppendLine("    .verify-table { border-collapse: collapse; margin-top: 0.5rem; width: 100%; font-size: 0.875rem; }");
        sb.AppendLine("    .verify-table td, .verify-table th { border: 1px solid #2a3340; padding: 0.4rem 0.6rem; text-align: left; }");
        sb.AppendLine("    .verify-issue { color: #e0c382; font-size: 0.85rem; margin: 0.25rem 0; }");
        sb.AppendLine("    @media print { body { background: white; color: black; } .turn { border-color: #b8924a; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <h1>Seldon Lattice Session Report</h1>");
        sb.AppendLine("  <div class=\"meta\">");
        sb.AppendLine("    <p><strong>Session ID:</strong> " + session.SessionId + "</p>");
        sb.AppendLine("    <p><strong>Framework:</strong> " + session.Framework + "</p>");
        sb.AppendLine("    <p><strong>Created:</strong> " + session.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss UTC") + "</p>");
        sb.AppendLine("    <p><strong>Root Question:</strong> " + session.RootQuestion + "</p>");
        sb.AppendLine("    <p><strong>Total Turns:</strong> " + turns.Count + " | <strong>Max Level:</strong> " + session.MaxLevel + "</p>");
        sb.AppendLine("  </div>");
        sb.AppendLine();
        sb.AppendLine("  <h2>Quality Metrics</h2>");
        sb.AppendLine("  <div class=\"metrics\">");
        sb.AppendLine("    <div class=\"metric\"><div class=\"metric-value\">" + avgScore.ToString("F2") + "</div><div class=\"metric-label\">Avg Score</div></div>");
        sb.AppendLine("    <div class=\"metric\"><div class=\"metric-value\">" + maxScore.ToString("F2") + "</div><div class=\"metric-label\">Max Score</div></div>");
        sb.AppendLine("    <div class=\"metric\"><div class=\"metric-value\">" + minScore.ToString("F2") + "</div><div class=\"metric-label\">Min Score</div></div>");
        sb.AppendLine("  </div>");
        sb.AppendLine();
        sb.AppendLine("  <h2>Integrity Verification</h2>");
        if (verify is not null)
        {
            var statusClass = verify.Passed ? "verify-pass" : "verify-fail";
            sb.AppendLine("    <p class=\"" + statusClass + "\">" + (verify.Passed ? "VERIFIED" : "NOT VERIFIED") + "</p>");
            sb.AppendLine("    <table class=\"verify-table\">");
            sb.AppendLine("      <tr><th>Nodes Checked</th><th>Signatures Verified</th><th>Pointer Issues</th><th>Signature Issues</th></tr>");
            sb.AppendLine("      <tr><td>" + verify.NodesChecked + "</td><td>" + verify.SignaturesVerified + "</td><td>"
                + verify.PointerIssues + "</td><td>" + verify.SignatureIssues + "</td></tr>");
            sb.AppendLine("    </table>");
            if (verify.Issues.Count > 0)
            {
                sb.AppendLine("    <h3 style=\"font-size:0.9rem;margin-bottom:0.25rem;\">Findings</h3>");
                foreach (var issue in verify.Issues)
                    sb.AppendLine("    <p class=\"verify-issue\">[" + issue.Severity + "] " + WebUtility.HtmlEncode(issue.NodeType)
                        + " " + WebUtility.HtmlEncode(issue.NodeCid) + ": " + WebUtility.HtmlEncode(issue.Message) + "</p>");
            }
        }
        else
        {
            sb.AppendLine("    <p class=\"verify-fail\">NOT VERIFIED</p>");
        }
        sb.AppendLine();
        sb.AppendLine("  <h2>Debate Transcript</h2>");
        foreach (var turn in turns)
        {
            sb.AppendLine("  <div class=\"turn\">");
            sb.AppendLine("    <div class=\"turn-header\">Turn " + turn.TurnNumber + " - " + turn.Speaker + " (" + turn.Role + ") - Level " + turn.Level + "</div>");
            if (turn.IsSustained) sb.AppendLine("    <div class=\"turn-metrics\"><strong>Sustained theme</strong></div>");
            var details = new List<string>();
            if (turn.QualityScore is not null) details.Add($"Quality: {turn.QualityScore:F2}");
            if (turn.CeremonyScore is not null) details.Add($"Ceremony: {turn.CeremonyScore:F2}");
            if (turn.SimilarityToPrevious is not null) details.Add($"Similarity: {turn.SimilarityToPrevious:F3}");
            if (turn.AscentScore != 0) details.Add($"Ascent: {turn.AscentScore:F3}");
            if (turn.JoinMediation is not null) details.Add($"Join mediation: {turn.JoinMediation:F3}");
            if (turn.JoinNovelty is not null) details.Add($"Join novelty: {turn.JoinNovelty:F3}");
            if (turn.IrreducibleTension != 0) details.Add($"Irreducible tension: {turn.IrreducibleTension:F3}");
            if (turn.RemainingDissensus is not null) details.Add("Remaining dissensus: " + turn.RemainingDissensus);
            if (turn.RepeatsPremise is not null) details.Add($"Repeats premise: {turn.RepeatsPremise:P1}");
            if (turn.UnresolvedTensions is { Length: > 0 }) details.Add("Unresolved tensions: " + string.Join("; ", turn.UnresolvedTensions));
            if (turn.TensionIntensity is not null) details.Add($"Tension intensity: {turn.TensionIntensity:F2}");
            if (turn.NewArgumentCount is not null) details.Add($"New arguments: {turn.NewArgumentCount}");
            if (turn.WordCount is not null) details.Add($"Word count: {turn.WordCount}");
            if (details.Count > 0) sb.AppendLine("    <div class=\"turn-metrics\">" + WebUtility.HtmlEncode(string.Join(" | ", details)) + "</div>");
            sb.AppendLine("    <div class=\"turn-content\">" + WebUtility.HtmlEncode(turn.Content) + "</div>");
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }
}

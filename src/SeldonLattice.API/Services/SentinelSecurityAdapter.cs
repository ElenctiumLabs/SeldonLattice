using Microsoft.Extensions.Options;
using Sentinel.Sdk.Security;
using SeldonLattice.Application.Security;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Services;

/// <summary>
/// Adapts Sentinel's policy evaluation as the authoritative prompt injection detector.
/// Falls back to the local PromptInjectionSecurityGuard if Sentinel is unreachable
/// or if the feature is disabled. A circuit breaker short-circuits to the local guard
/// after repeated Sentinel failures so debate turns never wait on a dead dependency.
/// </summary>
public sealed class SentinelSecurityAdapter(
    SecurityClient securityClient,
    PromptInjectionSecurityGuard localGuard,
    IOptions<SentinelConfiguration> config,
    ILogger<SentinelSecurityAdapter> logger)
    : ISentinelSecurityScanner
{
    private readonly SentinelConfiguration _config = config.Value;
    private readonly int _failureThreshold = Math.Max(1, config.Value.Security.FailureThreshold);
    private readonly TimeSpan _openDuration = TimeSpan.FromSeconds(Math.Max(1, config.Value.Security.OpenDurationSeconds));

    private int _consecutiveFailures;
    private DateTime _openUntilUtc = DateTime.MinValue;
    private readonly Lock _breakerLock = new();

    /// <summary>
    /// Scans text for prompt injection. Uses Sentinel as authoritative source;
    /// falls back to local guard if Sentinel is unavailable.
    /// </summary>
    public async Task<SentinelSecurityScanResult> ScanTextAsync(
        string? text,
        string? sessionId = null,
        string? speakerId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SentinelSecurityScanResult.Clean;

        if (!_config.Enabled || !_config.Security.Enabled || !AllowThroughBreaker())
            return MapLocalResult(localGuard.ScanText(text));

        try
        {
            var evaluation = await securityClient.EvaluatePolicyAsync(new PolicyEvaluation
            {
                AgentId = _config.Agent.ExternalId,
                OperationType = "dialectical-reasoning",
                Input = new Dictionary<string, object>
                {
                    ["content"] = text,
                    ["speaker"] = speakerId ?? "unknown"
                },
                Context = new Dictionary<string, object>
                {
                    ["sessionId"] = sessionId ?? "unknown",
                    ["source"] = "seldon-lattice"
                }
            }, cancellationToken);

            RecordSuccess();
            return evaluation.IsAllowed
                ? SentinelSecurityScanResult.Clean
                : BlockedResult(evaluation.Violations);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ascend cancelled - do not fall back to a scan whose result nobody consumes.
            throw;
        }
        catch (Exception ex)
        {
            RecordFailure();
            logger.LogWarning(ex, "Sentinel security evaluation failed; falling back to local guard");

            return _config.Security.FallbackToLocal ? MapLocalResult(localGuard.ScanText(text)) : SentinelSecurityScanResult.Clean;
        }
    }

    private SentinelSecurityScanResult BlockedResult(IReadOnlyList<PolicyViolation> violations)
    {
        logger.LogWarning("Sentinel security evaluation blocked text: {ViolationCount} violation(s)", violations.Count);

        return new SentinelSecurityScanResult
        {
            IsThreatDetected = true,
            ThreatType = "Sentinel_Policy_Violation",
            Details = string.Join("; ", violations.Select(v => v.Message)),
            MatchedPatterns = [.. violations.Select(v => v.PolicyName)]
        };
    }

    private bool AllowThroughBreaker()
    {
        lock (_breakerLock)
        {
            return DateTime.UtcNow >= _openUntilUtc;
        }
    }

    private void RecordSuccess()
    {
        lock (_breakerLock)
        {
            _consecutiveFailures = 0;
        }
    }

    private void RecordFailure()
    {
        lock (_breakerLock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures == _failureThreshold)
            {
                _openUntilUtc = DateTime.UtcNow + _openDuration;
                logger.LogWarning(
                    "Sentinel circuit breaker OPEN for {Seconds}s after {Threshold} consecutive failure(s)",
                    _openDuration.TotalSeconds, _failureThreshold);
            }
            else if (_consecutiveFailures > _failureThreshold && DateTime.UtcNow >= _openUntilUtc)
            {
                // Still failing past a probe window - re-open.
                _openUntilUtc = DateTime.UtcNow + _openDuration;
            }
        }
    }

    private static SentinelSecurityScanResult MapLocalResult(SecurityScanResult local) => new()
    {
        IsThreatDetected = local.IsThreatDetected,
        ThreatType = local.ThreatType,
        Details = local.Details,
        MatchedPatterns = local.MatchedPatterns
    };
}

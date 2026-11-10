import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../lib/api';
import type { SessionVerificationResult } from '../lib/api';
import { getConfig } from '../lib/config';
import { isMerkleCid } from '../lib/transcript';

interface ProvenanceStripProps {
  /** Merkle CID of the selected node, when the node has been anchored. */
  cid?: string | null;
  /** Parent hashes recorded on the node (its lineage in the Merkle DAG). */
  parentHashes?: string[] | null;
  /** Session the node belongs to; enables whole-session verification. */
  sessionId?: string | null;
}

const LABEL_STYLE = 'block text-[12px] font-mono uppercase tracking-widest mb-1';

/**
 * Read-only provenance view for the Inspector.
 *
 * Displays the Merkle content address of the selected node, its parent chain,
 * and a VERIFY action that asks the backend to re-derive every integrity check
 * for the session from stored bytes (content addressing, reference resolution,
 * and ML-DSA signatures).
 *
 * The component never fabricates a CID: nodes that are still optimistic (not yet
 * written to the Merkle store) render as pending instead.
 */
export const ProvenanceStrip: React.FC<ProvenanceStripProps> = ({
  cid,
  parentHashes,
  sessionId,
}) => {
  const [copied, setCopied] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [result, setResult] = useState<SessionVerificationResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const merkleCid = isMerkleCid(cid) ? cid : undefined;

  const lineage = useMemo(() => {
    const seen = new Set<string>();
    const parents: string[] = [];
    for (const raw of parentHashes ?? []) {
      const parent = raw?.trim();
      if (!parent || parent === merkleCid || seen.has(parent)) continue;
      seen.add(parent);
      parents.push(parent);
    }
    return parents;
  }, [parentHashes, merkleCid]);

  // A verification result belongs to one session; switching sessions (or
  // nodes within a new session) must not leave a stale pass/fail banner.
  useEffect(() => {
    setResult(null);
    setError(null);
  }, [sessionId]);

  const handleCopy = useCallback(async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      const ta = document.createElement('textarea');
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }
    setCopied(text);
    setTimeout(() => setCopied((current) => (current === text ? null : current)), getConfig().copiedIndicatorMs);
  }, []);

  const handleVerify = useCallback(async () => {
    if (!sessionId || verifying) return;
    setVerifying(true);
    setError(null);
    setResult(null);
    try {
      setResult(await api.verifySession(sessionId));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Verification request failed');
    } finally {
      setVerifying(false);
    }
  }, [sessionId, verifying]);

  const copyButton = (text: string) => (
    <button
      type="button"
      onClick={() => handleCopy(text)}
      className="shrink-0 px-2 py-1.5 rounded text-[12px] font-mono uppercase tracking-wider border cursor-pointer transition-all"
      style={{
        backgroundColor: copied === text ? 'var(--pr-role-judge)' : 'var(--pr-surface)',
        color: copied === text ? 'var(--pr-bg)' : 'var(--pr-text-muted)',
        borderColor: copied === text ? 'var(--pr-role-judge)' : 'var(--pr-border)',
      }}
      aria-label="Copy to clipboard"
    >
      {copied === text ? 'OK' : 'Copy'}
    </button>
  );

  return (
    <div className="space-y-3 font-mono text-[11px]">
      {/* Node CID */}
      <div>
        <label className={LABEL_STYLE} style={{ color: 'var(--pr-text-muted)' }}>
          Node CID (Merkle Content Address)
        </label>
        {merkleCid ? (
          <div className="flex items-start gap-2">
            <code
              className="flex-1 min-w-0 px-2 py-1.5 rounded border break-all select-all text-[12px]"
              style={{
                backgroundColor: 'var(--pr-bg)',
                borderColor: 'var(--pr-border)',
                color: 'var(--pr-text)',
              }}
            >
              {merkleCid}
            </code>
            {copyButton(merkleCid)}
          </div>
        ) : (
          <div
            className="px-2 py-1.5 rounded border italic text-[12px]"
            style={{
              backgroundColor: 'var(--pr-bg)',
              borderColor: 'var(--pr-border)',
              color: 'var(--pr-text-dim)',
            }}
          >
            Pending - not yet written to the Merkle store
          </div>
        )}
      </div>

      {/* Parent chain */}
      <div>
        <label className={LABEL_STYLE} style={{ color: 'var(--pr-text-muted)' }}>
          Lineage Ancestry (Parent Hashes)
        </label>
        {lineage.length > 0 ? (
          <div className="space-y-1">
            {lineage.map((parent) => (
              <div key={parent} className="flex items-start gap-2">
                <code
                  className="flex-1 min-w-0 px-2 py-1 rounded border break-all text-[12px]"
                  style={{
                    backgroundColor: 'var(--pr-bg)',
                    borderColor: 'var(--pr-border)',
                    color: 'var(--pr-text-dim)',
                  }}
                >
                  &uarr; {parent}
                </code>
                {copyButton(parent)}
              </div>
            ))}
          </div>
        ) : (
          <div
            className="px-2 py-1.5 rounded border italic text-[12px]"
            style={{
              backgroundColor: 'var(--pr-bg)',
              borderColor: 'var(--pr-border)',
              color: 'var(--pr-text-dim)',
            }}
          >
            {merkleCid ? 'Root origin (no parent links)' : 'No lineage until the node is anchored'}
          </div>
        )}
      </div>

      {/* Verification */}
      <div className="pt-2 border-t" style={{ borderColor: 'var(--pr-border)' }}>
        <div className="flex items-center justify-between gap-2">
          <span className="uppercase tracking-wider text-[12px]" style={{ color: 'var(--pr-text-muted)' }}>
            ML-DSA &amp; Merkle Integrity
          </span>
          <button
            type="button"
            onClick={handleVerify}
            disabled={verifying || !sessionId}
            className="px-2.5 py-1 rounded font-bold uppercase tracking-wider border transition-all text-[12px]"
            style={{
              backgroundColor: verifying ? 'var(--pr-surface-raised)' : 'var(--pr-surface)',
              color: !sessionId ? 'var(--pr-text-dim)' : 'var(--pr-role-proposer)',
              borderColor: 'var(--pr-border)',
              cursor: verifying || !sessionId ? 'not-allowed' : 'pointer',
            }}
            title={sessionId ? 'Verify the whole session against stored bytes' : 'No active session to verify'}
          >
            {verifying ? 'VERIFYING...' : 'VERIFY'}
          </button>
        </div>

        <div aria-live="polite">
          {result && (
            <div
              className="mt-2 p-2 rounded border text-[12px]"
              style={{
                backgroundColor: result.verified ? 'rgba(82, 201, 122, 0.12)' : 'rgba(231, 76, 60, 0.12)',
                borderColor: result.verified ? 'var(--pr-role-judge)' : '#e74c3c',
                color: result.verified ? 'var(--pr-role-judge)' : '#e74c3c',
              }}
            >
              <div className="font-bold flex items-center justify-between gap-2">
                <span>{result.verified ? '✓ MERKLE & SIGNATURES VALID' : '✗ INTEGRITY MISMATCH'}</span>
                <span className="text-[12px] opacity-80 whitespace-nowrap">
                  {result.signaturesVerified} signature{result.signaturesVerified === 1 ? '' : 's'}
                </span>
              </div>
              <div className="mt-1 text-[12px]" style={{ color: 'var(--pr-text)' }}>
                Checked {result.nodesChecked} node{result.nodesChecked === 1 ? '' : 's'} across the session.
                {result.pointerIssues ? ` ${result.pointerIssues} pointer issue${result.pointerIssues === 1 ? '' : 's'}.` : ''}
                {result.signatureIssues ? ` ${result.signatureIssues} signature issue${result.signatureIssues === 1 ? '' : 's'}.` : ''}
                {result.contentIssues ? ` ${result.contentIssues} content issue${result.contentIssues === 1 ? '' : 's'}.` : ''}
              </div>
              {result.issues && result.issues.length > 0 && (
                <ul className="mt-1.5 space-y-1">
                  {result.issues.map((issue, index) => (
                    <li
                      key={`${issue.nodeCid}-${issue.code}-${index}`}
                      className="text-[10px] leading-snug"
                      style={{ color: issue.severity === 'Error' ? '#e74c3c' : 'var(--pr-text-muted)' }}
                    >
                      [{issue.severity}] {issue.code}: {issue.message}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {error && (
            <div
              className="mt-2 p-2 rounded border text-[12px]"
              style={{ backgroundColor: 'rgba(231, 76, 60, 0.12)', borderColor: '#e74c3c', color: '#e74c3c' }}
            >
              Verification error: {error}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

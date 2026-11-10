import type { RawApiNode } from '../../layout';
import type { LiveTurn } from '../../types/liveTurn';
import { SYNTHESIS_TYPES, THESIS_SPEAKERS, speakerLabel } from '../../config/agentRegistry';
import { displayContent } from './displayContent';
import { filterLatestPath, findRootQuestion } from './graphPath';
import { collectParentHashes, isMerkleCid } from './provenance';

/** Converts raw API nodes into LiveTurn objects for the transcript UI. Deduplicates nodes by CID, classifies dialectic roles, and extracts speaker IDs from node types. */
export function liveTurnsFromApiNodes(
  apiData: RawApiNode[],
  previousTurns?: LiveTurn[],
): LiveTurn[] {
  // Deduplicate runs by picking the latest valid active path
  const filteredData = filterLatestPath(apiData);

  // Build a lookup of metrics/phase from previously stored turns (preserved across refresh).
  // The SSE stream synthesizes LiveTurns with temporary ids (`turn-*`, `synth-*`, ...),
  // while turns reconstructed from the Merkle store use `node-<cid>` ids - so id-based
  // lookup alone never matches across a reconstruction. As a fallback we index prior turns
  // by (speaker, level) and consume them in order for each reconstructed node, which
  // preserves quality metrics through both refresh and the post-synthesis fetchNodes calls.
  const metricsById = new Map<string, LiveTurn['metrics']>();
  const phaseById = new Map<string, string | undefined>();
  const priorBySpeakerLevel = new Map<string, LiveTurn[]>();
  if (previousTurns) {
    for (const t of previousTurns) {
      if (t.metrics) metricsById.set(t.id, t.metrics);
      if (t.phase) phaseById.set(t.id, t.phase);
      const key = `${t.speaker.toLowerCase()}|${t.level ?? 0}`;
      const arr = priorBySpeakerLevel.get(key) ?? [];
      arr.push(t);
      priorBySpeakerLevel.set(key, arr);
    }
  }
  const takePrior = (key: string): LiveTurn | undefined => {
    const arr = priorBySpeakerLevel.get(key);
    if (!arr || arr.length === 0) return undefined;
    const turn = arr.shift();
    if (arr.length === 0) priorBySpeakerLevel.delete(key);
    return turn;
  };

  const turns: LiveTurn[] = filteredData
    .filter((n) => {
      const nt = (n.nodeType ?? '').toLowerCase();
      return nt !== 'session_metadata' && n.content;
    })
    .map((n): LiveTurn => {
      const nt = (n.nodeType ?? '').toLowerCase();
      const speakerId: string = (n as any).speakerId || n.nodeType?.replace(/_reply$/, '') || '';
      const isQuestion = nt === 'question';
      const isSynthesis = SYNTHESIS_TYPES.has(nt) || nt.includes('synthesis') || nt.includes('socrat');
      const isInterjection = nt === 'interjection' || nt.endsWith('_interjection');
      const isSystem = nt === 'system';
      const isThesis = isQuestion || THESIS_SPEAKERS.has(speakerId.toLowerCase());
      const role: LiveTurn['dialecticRole'] = isQuestion
        ? 'thesis'
        : isSynthesis
          ? 'synthesis'
          : isInterjection
            ? 'mediation'
            : isSystem
              ? 'system'
              : isThesis ? 'thesis' : 'antithesis';
      // Synthesis nodes are the Moderator role; interjections are the Mediator role. The
      // stored SpeakerId is the agent ("mediator") for both, so the label must come from
      // the node type/role rather than the raw id.
      const displaySpeaker = isQuestion
        ? 'Question'
        : speakerLabel(speakerId, nt, role);
      const id = `node-${n.cid}`;
      const idMatched = metricsById.has(id) || phaseById.has(id);
      const prior = idMatched ? undefined : takePrior(`${displaySpeaker.toLowerCase()}|${n.level ?? 0}`);

      // Quality metrics now live ON the node (persisted by the ascent service), so a
      // cold load reads them directly rather than depending on a streamed copy still
      // sitting in localStorage. Values absent from the node fall back to the prior
      // turn - which covers nodes written before the metrics were persisted, and the
      // stream-only fields on a turn that is still in flight.
      //
      // `??` is deliberate, not `||`: a measured 0.0 (a maximally repetitive reply) is a
      // real observation and must survive, whereas null means "never scored".
      const inheritedMetrics = metricsById.get(id) ?? prior?.metrics;
      const nodeMetrics: LiveTurn['metrics'] = {};
      const carry = <K extends keyof NonNullable<LiveTurn['metrics']>>(
        key: K,
        value: NonNullable<LiveTurn['metrics']>[K] | null | undefined,
      ) => {
        if (value !== null && value !== undefined) nodeMetrics[key] = value;
      };

      carry('qualityScore', n.qualityScore);
      carry('ceremonyScore', n.ceremonyScore);
      carry('similarityScore', n.similarityToPrevious);
      carry('newArgumentCount', n.newArgumentCount);
      carry('wordCount', n.wordCount);
      carry('hasFarewell', n.hasFarewell);
      carry('hasAgreement', n.hasAgreement);
      carry('hasDisagreement', n.hasDisagreement);
      carry('selfSimilarity', n.selfSimilarity);

      // Ascent score and irreducible tension are written by the ascent scorer, which
      // only runs for a synthesis. On every other node they sit at their zero
      // initialisers (0f / null), so reading them unconditionally would render a
      // "0.0%" bar for turns that were never scored - presenting unmeasured as
      // measured. Gate on the synthesis node, where a 0 is a genuine score (the
      // scorer ran and awarded nothing) rather than an absent one.
      if (isSynthesis) {
        if (n.isSustained === true) carry('isSustained', true);
        carry('ascentScore', n.ascentScore);
        carry('joinMediation', n.joinMediation);
        carry('joinNovelty', n.joinNovelty);
        carry('irreducibleTension', n.irreducibleTension);
      }

      const metrics =
        inheritedMetrics || Object.keys(nodeMetrics).length > 0
          ? { ...inheritedMetrics, ...nodeMetrics }
          : undefined;

      return {
        id,
        speaker: displaySpeaker,
        content: displayContent(n.content),
        dialecticRole: role,
        level: n.level ?? 0,
        timestamp: n.timestamp ? (Date.parse(n.timestamp) || 0) : 0,
        // Real provenance survives the reconstruction, so the Inspector can show the
        // content address and lineage of any persisted node instead of a generated id.
        cid: isMerkleCid(n.cid) ? n.cid : undefined,
        parentHashes: collectParentHashes(n),
        metrics,
        phase: phaseById.get(id) ?? prior?.phase,
        isSustained: n.isSustained,
        // Negative-dialectic markers, read from the node. Without these a page reload
        // reconstructed turns without the markers, so the Inspector lost them even
        // though the stored node had carried them all along.
        remainingDissensus: n.remainingDissensus ?? prior?.remainingDissensus ?? null,
        repeatsPremise: n.repeatsPremise ?? prior?.repeatsPremise ?? null,
        unresolvedTensions: n.unresolvedTensions ?? prior?.unresolvedTensions ?? null,
        tensionIntensity: n.tensionIntensity ?? prior?.tensionIntensity ?? null,
      };
    });

  // Group API turns by level
  const apiByLevel = new Map<number, LiveTurn[]>();
  for (const t of turns) {
    const lvl = t.level ?? 0;
    if (!apiByLevel.has(lvl)) apiByLevel.set(lvl, []);
    apiByLevel.get(lvl)!.push(t);
  }

  // Group previous turns by level
  const prevByLevel = new Map<number, LiveTurn[]>();
  if (previousTurns && previousTurns.length > 0) {
    for (const t of previousTurns) {
      const lvl = t.level ?? 0;
      if (!prevByLevel.has(lvl)) prevByLevel.set(lvl, []);
      prevByLevel.get(lvl)!.push(t);
    }
  }

  // Collect all unique levels across API nodes and previous turns
  const allLevels = Array.from(
    new Set([...apiByLevel.keys(), ...prevByLevel.keys()])
  ).sort((a, b) => a - b);

  const result: LiveTurn[] = [];

  for (const lvl of allLevels) {
    const apiTurnsForLvl = apiByLevel.get(lvl) ?? [];
    const prevTurnsForLvl = prevByLevel.get(lvl) ?? [];

    const isLvlCompleted = apiTurnsForLvl.some((t) => t.dialecticRole === 'synthesis');

    if (isLvlCompleted && apiTurnsForLvl.length > 0) {
      // Completed round: the Merkle nodes are canonical and, now that the fetch pages
      // to completion, complete - so they alone define the round. previousTurns are
      // deliberately not merged in: SSE turns carry `turn-*` ids and reconstructed
      // ones `node-<cid>`, and the synthesis speaker label differs between the two
      // sources (`Synthesis` on the stream, the moderator's name on the node), so any
      // merge by id or by speaker duplicates the round on reload. A round that is
      // still in progress takes the branch below, which does preserve streamed turns.
      const turnMap = new Map<string, LiveTurn>();
      for (const t of apiTurnsForLvl) {
        turnMap.set(t.id, t);
      }
      result.push(...turnMap.values());
    } else if (prevTurnsForLvl.length > 0) {
      // Active / in-progress round: preserve live streaming turns
      const mergedLvl: LiveTurn[] = [...prevTurnsForLvl];

      const questionNode = apiTurnsForLvl.find(
        (t) => t.dialecticRole === 'thesis' && t.speaker.toLowerCase() === 'question'
      );
      if (questionNode && !mergedLvl.some((t) => t.speaker.toLowerCase() === 'question' || t.speaker.toLowerCase() === 'system')) {
        mergedLvl.unshift(questionNode);
      }

      const synthNode = apiTurnsForLvl.find((t) => t.dialecticRole === 'synthesis');
      if (synthNode && !mergedLvl.some((t) => t.dialecticRole === 'synthesis')) {
        mergedLvl.push(synthNode);
      }

      const turnMap = new Map<string, LiveTurn>();
      for (const t of mergedLvl) {
        turnMap.set(t.id, t);
      }
      result.push(...turnMap.values());
    } else {
      // Cold load from Merkle nodes
      const turnMap = new Map<string, LiveTurn>();
      for (const t of apiTurnsForLvl) {
        turnMap.set(t.id, t);
      }
      result.push(...turnMap.values());
    }
  }

  // Derive the fractal-echo marker from stored data. The echo event is not persisted, but
  // the boundary is: a sustained synthesis followed by a question that asks the root
  // again. Marking the existing question turn keeps the transcript honest - it is the
  // real next-level question, not a synthetic duplicate.
  const rootQuestion = findRootQuestion(filteredData);
  if (rootQuestion) {
    const root = rootQuestion.trim().toLowerCase();
    const sustainedLevels = new Set(
      result
        .filter((t) => t.isSustained === true && t.dialecticRole === 'synthesis')
        .map((t) => t.level ?? 0)
    );
    for (const turn of result) {
      if (turn.speaker.toLowerCase() !== 'question') continue;
      if (!sustainedLevels.has((turn.level ?? 0) - 1)) continue;
      if (turn.content.trim().toLowerCase() === root) turn.isEcho = true;
    }
  }

  return result.sort((a, b) => (a.level ?? 0) - (b.level ?? 0));
}

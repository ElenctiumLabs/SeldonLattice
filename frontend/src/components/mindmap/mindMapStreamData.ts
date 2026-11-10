/** Normalized fields consumed by MindMap's live stream event handler. */
export interface MindMapStreamData {
  type: string;
  speakerId: any;
  content: string;
  exitReason: any;
  metaphor: any;
  isSustained: any;
  nextTheme: any;
  connectiveSentence: any;
  synthesisThesis: any;
  remainingDissensus: any;
  repeatsPremise: any;
  unresolvedTensions: any;
  tensionIntensity: any;
  triggeringSignal: any;
  turn: any;
  dialecticRole: any;
  qualityScore: any;
  ceremonyScore: any;
  similarityToPrevious: any;
  phase: any;
  ascentScore: any;
  joinMediation: any;
  joinNovelty: any;
  irreducibleTension: any;
  reasoningTrace: any;
}

/** Collapse the backend's PascalCase and legacy camelCase event fields once. */
export function normalizeMindMapStreamData(raw: Record<string, any>): MindMapStreamData {
  const read = (pascalCase: string, camelCase: string) => raw[pascalCase] ?? raw[camelCase];

  return {
    type: read('Type', 'type') ?? '',
    speakerId: read('SpeakerId', 'speakerId'),
    content: raw.Content ?? raw.content ?? raw.Metaphor ?? raw.metaphor ?? '',
    exitReason: read('ExitReason', 'exitReason'),
    metaphor: read('Metaphor', 'metaphor'),
    isSustained: read('IsSustained', 'isSustained'),
    nextTheme: read('NextTheme', 'nextTheme'),
    connectiveSentence: read('ConnectiveSentence', 'connectiveSentence'),
    synthesisThesis: read('SynthesisThesis', 'synthesisThesis'),
    remainingDissensus: read('RemainingDissensus', 'remainingDissensus'),
    repeatsPremise: read('RepeatsPremise', 'repeatsPremise'),
    unresolvedTensions: read('UnresolvedTensions', 'unresolvedTensions'),
    tensionIntensity: read('TensionIntensity', 'tensionIntensity'),
    triggeringSignal: read('TriggeringSignal', 'triggeringSignal'),
    turn: read('Turn', 'turn'),
    dialecticRole: read('DialecticRole', 'dialecticRole'),
    qualityScore: read('QualityScore', 'qualityScore'),
    ceremonyScore: read('CeremonyScore', 'ceremonyScore'),
    similarityToPrevious: read('SimilarityToPrevious', 'similarityToPrevious'),
    phase: read('Phase', 'phase'),
    ascentScore: read('AscentScore', 'ascentScore'),
    joinMediation: read('JoinMediation', 'joinMediation'),
    joinNovelty: read('JoinNovelty', 'joinNovelty'),
    irreducibleTension: read('IrreducibleTension', 'irreducibleTension'),
    reasoningTrace: read('ReasoningTrace', 'reasoningTrace'),
  };
}

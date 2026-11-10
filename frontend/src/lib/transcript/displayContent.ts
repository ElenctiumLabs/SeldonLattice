/** Extracts human-readable text from raw agent output. Handles JSON with mediator insight fields (mediator_insight, tensions, resolution, embodied_insight, relational_insight, cultural_factors), plain text, and mixed formats. Falls back to regex extraction and raw JSON stripping if needed. */
export function displayContent(raw: string): string {
  if (!raw) return '';
  let text = raw.trim();

  // Strip markdown code fences
  if (text.startsWith('```')) {
    text = text.replace(/^```(?:json)?\s*/i, '', ).replace(/\s*```$/, '').trim();
  }

  // Strip leading colon: some models emit ": text" after [RESPONSE] marker
  text = text.replace(/^[\t ]*:[\t ]*/, '').trim();

  // Helper: strip leading colon and whitespace from extracted content
  const stripLeadingColon = (s: string): string => s.replace(/^[\t ]*:[\t ]*/, '').trim();

  // Helper: extract mediator fields from a parsed JSON object
  const extractMediatorFields = (parsed: Record<string, unknown>): string | null => {
    const insight = (parsed.mediator_insight ?? parsed.embodied_insight ?? parsed.relational_insight) as string | undefined;
    if (typeof insight !== 'string' || insight.trim().length === 0) return null;
    const parts: string[] = [stripLeadingColon(insight)];
    const tensions = (parsed.tensions ?? parsed.cultural_factors) as string[] | undefined;
    if (Array.isArray(tensions) && tensions.length > 0) {
      parts.push('\n\nTensions:\n' + tensions.map((t: string) => `- ${stripLeadingColon(t)}`).join('\n'));
    }
    const resolution = (parsed.resolution ?? parsed.organismic_resolution ?? parsed.authentic_resolution) as string | undefined;
    if (typeof resolution === 'string' && resolution.trim().length > 0) {
      parts.push('\n\nResolution: ' + stripLeadingColon(resolution));
    }
    return parts.join('');
  };

  // Helper: try to find and parse a JSON object embedded in text (handles preamble text)
  const tryParseJsonFromText = (input: string): Record<string, unknown> | null => {
    const start = input.indexOf('{');
    const end = input.lastIndexOf('}');
    if (start < 0 || end <= start) return null;
    try {
      return JSON.parse(input.substring(start, end + 1));
    } catch {
      return null;
    }
  };

  // 1. Direct JSON parse (text starts with {)
  if (text.startsWith('{')) {
    try {
      const parsed = JSON.parse(text);
      if (parsed && typeof parsed === 'object') {
        // Try standard response field first
        const res = parsed.response ?? parsed.Response ?? parsed.content ?? parsed.Content ?? parsed.text;
        if (typeof res === 'string' && res.trim().length > 0) {
          return stripLeadingColon(res);
        }
        // Try mediator insight fields
        const mediatorResult = extractMediatorFields(parsed);
        if (mediatorResult) return mediatorResult;
        // If only reasoning and no displayable content, return empty
        if (parsed.reasoning && !res) {
          return '';
        }
      }
    } catch {
      // JSON parse failed, fall through to other strategies
    }
  }

  // 2. Try to find JSON embedded in text (handles "Here is my analysis:\n{...}")
  const embedded = tryParseJsonFromText(text);
  if (embedded) {
    const res = embedded.response ?? embedded.Response;
    if (typeof res === 'string' && res.trim().length > 0) {
      return stripLeadingColon(res);
    }
    const mediatorResult = extractMediatorFields(embedded);
    if (mediatorResult) return mediatorResult;
  }

  // 3. Regex fallback: extract "response" field
  const match = text.match(/"response"\s*:\s*"([\s\S]*?)"(?=\s*,\s*"reasoning"|\s*,\s*"[^"]+"\s*:|\s*\}\s*$)/i) ||
                text.match(/"response"\s*:\s*"([\s\S]*?)"/i);

  if (match && match[1]) {
    let extracted = match[1];
    extracted = extracted
      .replace(/\\n/g, '\n')
      .replace(/\\"/g, '"')
      .replace(/\\\\/g, '\\')
      .replace(/\\t/g, '\t')
      .trim();
    if (extracted.length > 0) {
      return extracted;
    }
  }

  // 4. Regex fallback: extract mediator insight fields
  const insightMatch = text.match(/"mediator_insight"\s*:\s*"([\s\S]*?)"(?=\s*[,\}])/i) ||
                       text.match(/"embodied_insight"\s*:\s*"([\s\S]*?)"(?=\s*[,\}])/i) ||
                       text.match(/"relational_insight"\s*:\s*"([\s\S]*?)"(?=\s*[,\}])/i);

  if (insightMatch && insightMatch[1]) {
    let extracted = insightMatch[1]
      .replace(/\\n/g, '\n')
      .replace(/\\"/g, '"')
      .replace(/\\\\/g, '\\')
      .replace(/\\t/g, '\t')
      .trim();
    if (extracted.length > 0) {
      const tensionsMatch = text.match(/"tensions"\s*:\s*\[([\s\S]*?)\]/i) ||
                            text.match(/"cultural_factors"\s*:\s*\[([\s\S]*?)\]/i);
      if (tensionsMatch && tensionsMatch[1]) {
        const items = tensionsMatch[1].match(/"([^"]+)"/g);
        if (items && items.length > 0) {
          extracted += '\n\nTensions:\n' + items.map((s: string) => `- ${s.replace(/"/g, '').trim()}`).join('\n');
        }
      }
      const resMatch = text.match(/"resolution"\s*:\s*"([\s\S]*?)"(?=\s*[,\}])/i);
      if (resMatch && resMatch[1]) {
        extracted += '\n\nResolution: ' + resMatch[1].replace(/\\n/g, '\n').replace(/\\"/g, '"').replace(/\\\\/g, '\\').trim();
      }
      return extracted;
    }
  }

  // 5. Strip JSON reasoning blocks (when response is outside JSON)
  if (text.startsWith('{')) {
    let depth = 0;
    let jsonEnd = -1;
    let inString = false;
    let escape = false;
    for (let i = 0; i < text.length; i++) {
      const ch = text[i];
      if (escape)         { escape = false; continue; }
      if (ch === '\\' && inString) { escape = true;  continue; }
      if (ch === '"')     { inString = !inString; continue; }
      if (inString)       { continue; }
      if (ch === '{')     depth++;
      else if (ch === '}') { depth--; if (depth === 0) { jsonEnd = i; break; } }
    }

    if (jsonEnd >= 0) {
      const jsonPart = text.substring(0, jsonEnd + 1);
      const restPart = text.substring(jsonEnd + 1).trim();
      const hasReasoning = jsonPart.includes('"reasoning"');
      const hasResponse  = jsonPart.includes('"response"');

      if (hasReasoning && !hasResponse) {
        if (restPart.length > 0) {
          return restPart.replace(/^["'\s]+|["'\s]+$/g, '').trim();
        }
        return '';
      }
    }
  }

  if (text.includes('"reasoning"')) {
    const stripped = text.replace(/\{[\s\S]*?"reasoning"[\s\S]*?\}/gi, '').trim();
    return stripped.length > 0
      ? stripped.replace(/^["'\s]+|["'\s]+$/g, '').trim()
      : '';
  }

  // 6. Last resort: try to extract mediator fields from raw text
  const mediatorFields = /"(?:mediator_insight|embodied_insight|relational_insight|tensions|cultural_factors|resolution|organismic_resolution|authentic_resolution)"\s*:/i;
  if (mediatorFields.test(text)) {
    try {
      const parsed = JSON.parse(text);
      if (parsed && typeof parsed === 'object') {
        const mediatorResult = extractMediatorFields(parsed);
        if (mediatorResult) return mediatorResult;
      }
    } catch {
      const cleaned = text.replace(/\{[\s\S]*\}/g, '').trim();
      if (cleaned.length > 0) return cleaned;
    }
  }

  return text;
}

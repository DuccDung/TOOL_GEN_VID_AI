export type SpeechDiffKind = 'match' | 'missing' | 'unexpected';

export type SpeechDiffSegment = {
  text: string;
  kind: SpeechDiffKind;
};

export type SpeechTranscriptDiff = {
  expected: SpeechDiffSegment[];
  transcript: SpeechDiffSegment[];
  matches: boolean;
};

type WordToken = {
  display: string;
  comparison: string;
};

export function buildSpeechTranscriptDiff(expected: string, transcript: string): SpeechTranscriptDiff {
  const expectedTokens = tokenize(expected);
  const transcriptTokens = tokenize(transcript);
  const lengths = Array.from(
    { length: expectedTokens.length + 1 },
    () => Array<number>(transcriptTokens.length + 1).fill(0)
  );

  for (let expectedIndex = expectedTokens.length - 1; expectedIndex >= 0; expectedIndex -= 1) {
    for (let transcriptIndex = transcriptTokens.length - 1; transcriptIndex >= 0; transcriptIndex -= 1) {
      lengths[expectedIndex][transcriptIndex] =
        expectedTokens[expectedIndex].comparison === transcriptTokens[transcriptIndex].comparison
          ? lengths[expectedIndex + 1][transcriptIndex + 1] + 1
          : Math.max(lengths[expectedIndex + 1][transcriptIndex], lengths[expectedIndex][transcriptIndex + 1]);
    }
  }

  const expectedSegments: SpeechDiffSegment[] = [];
  const transcriptSegments: SpeechDiffSegment[] = [];
  let expectedIndex = 0;
  let transcriptIndex = 0;
  while (expectedIndex < expectedTokens.length || transcriptIndex < transcriptTokens.length) {
    if (
      expectedIndex < expectedTokens.length &&
      transcriptIndex < transcriptTokens.length &&
      expectedTokens[expectedIndex].comparison === transcriptTokens[transcriptIndex].comparison
    ) {
      expectedSegments.push({ text: expectedTokens[expectedIndex].display, kind: 'match' });
      transcriptSegments.push({ text: transcriptTokens[transcriptIndex].display, kind: 'match' });
      expectedIndex += 1;
      transcriptIndex += 1;
      continue;
    }

    const skipExpected = expectedIndex < expectedTokens.length
      ? lengths[expectedIndex + 1][transcriptIndex]
      : -1;
    const skipTranscript = transcriptIndex < transcriptTokens.length
      ? lengths[expectedIndex][transcriptIndex + 1]
      : -1;
    if (expectedIndex < expectedTokens.length && skipExpected >= skipTranscript) {
      expectedSegments.push({ text: expectedTokens[expectedIndex].display, kind: 'missing' });
      expectedIndex += 1;
    } else if (transcriptIndex < transcriptTokens.length) {
      transcriptSegments.push({ text: transcriptTokens[transcriptIndex].display, kind: 'unexpected' });
      transcriptIndex += 1;
    }
  }

  return {
    expected: expectedSegments,
    transcript: transcriptSegments,
    matches: expectedSegments.every((segment) => segment.kind === 'match') &&
      transcriptSegments.every((segment) => segment.kind === 'match')
  };
}

function tokenize(value: string): WordToken[] {
  return value
    .normalize('NFC')
    .trim()
    .split(/\s+/u)
    .filter(Boolean)
    .map((display) => ({
      display,
      comparison: display
        .normalize('NFC')
        .toLocaleLowerCase('vi-VN')
        .replace(/[^\p{L}\p{M}\p{N}]/gu, '')
    }))
    .filter((token) => token.comparison.length > 0);
}

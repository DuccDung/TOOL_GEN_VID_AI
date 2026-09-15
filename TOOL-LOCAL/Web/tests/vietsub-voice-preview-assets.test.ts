import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { vietsubVoicePreviewSamples } from '../src/features/vietsub/vietsubVoicePreviewSamples';

type PreviewManifest = {
  schemaVersion: number;
  samples: { voiceId: string; file: string; sha256: string; durationSeconds: number; sampleRate: number }[];
};

describe('Vietsub voice preview assets', () => {
  it('bundles one distinct, hash-verified PCM sample for every local voice', () => {
    const manifest = JSON.parse(readFileSync(
      new URL('../public/voice-previews/manifest.json', import.meta.url), 'utf8'
    )) as PreviewManifest;
    expect(manifest.schemaVersion).toBe(1);
    expect(manifest.samples).toHaveLength(15);
    expect(Object.keys(vietsubVoicePreviewSamples)).toHaveLength(15);

    const hashes = new Set<string>();
    for (const sample of manifest.samples) {
      const route = vietsubVoicePreviewSamples[sample.voiceId];
      expect(route).toBe(`/voice-previews/${sample.file}`);
      expect(route).toMatch(/^\/voice-previews\/[a-z0-9_-]+\.wav$/);
      const bytes = readFileSync(new URL(`../public${route}`, import.meta.url));
      expect(bytes.length).toBeGreaterThan(40_000);
      expect(bytes.length).toBeLessThan(512_000);
      expect(bytes.toString('ascii', 0, 4)).toBe('RIFF');
      expect(bytes.toString('ascii', 8, 12)).toBe('WAVE');
      expect(bytes.toString('ascii', 12, 16)).toBe('fmt ');
      expect(bytes.readUInt16LE(22)).toBe(1); // mono
      expect(bytes.readUInt16LE(34)).toBe(16); // PCM 16-bit
      const rate = bytes.readUInt32LE(24);
      expect(rate).toBe(sample.sampleRate);
      expect(Math.abs((bytes.length - 44) / (rate * 2) - sample.durationSeconds)).toBeLessThan(0.05);

      const hash = createHash('sha256').update(bytes).digest('hex');
      expect(hash).toBe(sample.sha256);
      expect(hashes.has(hash)).toBe(false);
      hashes.add(hash);
    }
  });
});

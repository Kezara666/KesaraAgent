/** Minimal, feature-detected wrapper around the browser speech APIs. Audio is handled by the browser, not sent to the agent. */

export interface SpeechSupport {
  recognition: boolean;
  synthesis: boolean;
}

export interface SpeechRecognitionLike {
  lang: string;
  interimResults: boolean;
  continuous: boolean;
  onresult: ((event: { results: ArrayLike<ArrayLike<{ transcript: string }>> }) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
}

type RecognitionConstructor = new () => SpeechRecognitionLike;

export function detectSpeech(scope: Record<string, unknown>): SpeechSupport {
  const recognition = (scope['SpeechRecognition'] ?? scope['webkitSpeechRecognition']) !== undefined;
  const synthesis = scope['speechSynthesis'] !== undefined && scope['SpeechSynthesisUtterance'] !== undefined;
  return { recognition, synthesis };
}

/** Builds a recognizer for one utterance, or returns null when the browser has no speech recognition. */
export function createRecognizer(scope: Record<string, unknown>, lang = 'en-US'): SpeechRecognitionLike | null {
  const constructor = (scope['SpeechRecognition'] ?? scope['webkitSpeechRecognition']) as RecognitionConstructor | undefined;
  if (!constructor) {
    return null;
  }
  const recognizer = new constructor();
  recognizer.lang = lang;
  recognizer.interimResults = false;
  recognizer.continuous = false;
  return recognizer;
}

/** Reads text aloud when the browser supports it. Returns false when it cannot. */
export function speak(text: string, scope: Record<string, unknown>): boolean {
  const synth = scope['speechSynthesis'] as { speak(utterance: unknown): void } | undefined;
  const UtteranceConstructor = scope['SpeechSynthesisUtterance'] as (new (text: string) => unknown) | undefined;
  if (!synth || !UtteranceConstructor) {
    return false;
  }
  synth.speak(new UtteranceConstructor(text));
  return true;
}

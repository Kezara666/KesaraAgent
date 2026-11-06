import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { AuthStore } from './auth';
import { authInterceptor } from './auth.interceptor';
import { AgentApi, describeError } from './api';
import { detectSpeech, createRecognizer, speak } from './speech';

describe('AuthStore', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('stores a trimmed token for this tab and clears it on sign-out (FR-8.4)', () => {
    const auth = TestBed.inject(AuthStore);

    auth.signIn('  abc123  ');
    expect(auth.token()).toBe('abc123');
    expect(sessionStorage.getItem('agent.token')).toBe('abc123');

    auth.signOut();
    expect(auth.isSignedIn()).toBe(false);
    expect(sessionStorage.getItem('agent.token')).toBeNull();
  });

  it('refuses a blank token', () => {
    const auth = TestBed.inject(AuthStore);

    auth.signIn('   ');

    expect(auth.isSignedIn()).toBe(false);
  });

  it('keeps working when sessionStorage throws', () => {
    const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    try {
      const auth = TestBed.inject(AuthStore);
      auth.signIn('in-memory-only');
      expect(auth.token()).toBe('in-memory-only');
    } finally {
      spy.mockRestore();
    }
  });
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthStore).signIn('secret-token');
  });

  afterEach(() => controller.verify());

  it('adds the bearer token to API calls (FR-8.4)', () => {
    http.get('/api/v1/tasks').subscribe();

    const request = controller.expectOne('/api/v1/tasks');
    expect(request.request.headers.get('Authorization')).toBe('Bearer secret-token');
    request.flush([]);
  });

  it('never sends the token to other origins', () => {
    http.get('https://example.com/data').subscribe();

    const request = controller.expectOne('https://example.com/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
});

describe('AgentApi', () => {
  let api: AgentApi;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AgentApi);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('submits a task with an idempotency key so retries are safe (FR-8.5)', () => {
    api.submitTask('summarize mail', 'key-1').subscribe();

    const request = controller.expectOne('/api/v1/tasks');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(request.request.body).toEqual({ prompt: 'summarize mail' });
    request.flush({});
  });

  it('approves and rejects through the approval endpoints (FR-9.7)', () => {
    api.approve('a1').subscribe();
    api.reject('a2').subscribe();

    expect(controller.expectOne('/api/v1/approvals/a1/approve').request.method).toBe('POST');
    expect(controller.expectOne('/api/v1/approvals/a2/reject').request.method).toBe('POST');
    controller.match(() => true).forEach((request) => request.flush({}));
  });

  it('confirms a schedule only when confirm is sent (FR-9.8)', () => {
    api.createSchedule('morning', '0 7 * * *', 'summarize').subscribe();

    const request = controller.expectOne('/api/v1/schedules');
    expect(request.request.body.confirm).toBe(true);
    request.flush({});
  });
});

describe('describeError', () => {
  it('maps status codes to plain messages without exposing response bodies', () => {
    const error = new HttpErrorResponse({ status: 403, error: 'secret detail' });
    expect(describeError(error)).toBe('Your token does not allow this action.');
    expect(describeError(error)).not.toContain('secret detail');
  });

  it('reports an unreachable API', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toBe('The agent API cannot be reached.');
  });
});

describe('speech', () => {
  it('reports no support when the browser has no speech APIs', () => {
    expect(detectSpeech({})).toEqual({ recognition: false, synthesis: false });
    expect(createRecognizer({})).toBeNull();
    expect(speak('hello', {})).toBe(false);
  });

  it('detects recognition and synthesis when present', () => {
    class FakeRecognition {
      lang = '';
      interimResults = true;
      continuous = true;
      onresult = null;
      onerror = null;
      onend = null;
      start() {}
      stop() {}
    }
    const scope = {
      webkitSpeechRecognition: FakeRecognition,
      speechSynthesis: { speak: vi.fn() },
      SpeechSynthesisUtterance: class {
        constructor(public text: string) {}
      },
    };

    expect(detectSpeech(scope)).toEqual({ recognition: true, synthesis: true });
    const recognizer = createRecognizer(scope);
    expect(recognizer?.interimResults).toBe(false);
    expect(recognizer?.continuous).toBe(false);
  });

  it('reads text aloud through the browser synthesis API', () => {
    const synth = { speak: vi.fn() };
    const scope = {
      speechSynthesis: synth,
      SpeechSynthesisUtterance: class {
        constructor(public text: string) {}
      },
    };

    expect(speak('done', scope)).toBe(true);
    expect(synth.speak).toHaveBeenCalledTimes(1);
  });
});

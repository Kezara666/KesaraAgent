import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'agent.token';

/**
 * Holds the API token for this browser tab. The token lives in sessionStorage, so it is gone when the tab closes.
 * Storage can be blocked (private mode, policy), so every access is guarded and the app still works without it.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly tokenSignal = signal<string | null>(this.read());

  readonly token = this.tokenSignal.asReadonly();

  isSignedIn(): boolean {
    return this.tokenSignal() !== null;
  }

  signIn(token: string): void {
    const trimmed = token.trim();
    const value = trimmed.length > 0 ? trimmed : null;
    this.tokenSignal.set(value);
    this.write(value);
  }

  signOut(): void {
    this.tokenSignal.set(null);
    this.write(null);
  }

  private read(): string | null {
    try {
      return sessionStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }

  private write(value: string | null): void {
    try {
      if (value === null) {
        sessionStorage.removeItem(STORAGE_KEY);
      } else {
        sessionStorage.setItem(STORAGE_KEY, value);
      }
    } catch {
      // Storage unavailable. The token stays in memory for this page only.
    }
  }
}

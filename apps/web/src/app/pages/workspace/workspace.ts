import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { describeError } from '../../core/api';

interface EditResponse {
  path: string;
  diff: string;
}

/** Applies one exact, unique search-and-replace edit and shows the diff for review (FR-3.2). */
@Component({
  selector: 'app-workspace',
  imports: [FormsModule],
  template: `
    <section class="panel">
      <h1>Workspace edit</h1>
      <p class="muted">The search text must match exactly once. The diff shows what changed.</p>
      <form (ngSubmit)="apply()">
        <label for="path">File (relative to the workspace)</label>
        <input id="path" name="path" [(ngModel)]="path" placeholder="notes.md" />
        <label for="search">Search for</label>
        <textarea id="search" name="search" rows="2" [(ngModel)]="search"></textarea>
        <label for="replace">Replace with</label>
        <textarea id="replace" name="replace" rows="2" [(ngModel)]="replace"></textarea>
        <button type="submit" [disabled]="busy() || path.trim().length === 0 || search.length === 0">Apply edit</button>
      </form>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      @if (diff()) {
        <h2>Diff for {{ diffPath() }}</h2>
        <pre class="result" data-testid="diff">{{ diff() }}</pre>
      }
    </section>
  `,
})
export class WorkspaceComponent {
  private readonly http = inject(HttpClient);

  path = '';
  search = '';
  replace = '';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly diff = signal<string | null>(null);
  readonly diffPath = signal('');

  apply(): void {
    this.busy.set(true);
    this.error.set(null);
    this.http.post<EditResponse>('/api/v1/workspace/edits', {
      path: this.path.trim(),
      search: this.search,
      replace: this.replace,
    }).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.diffPath.set(result.path);
        this.diff.set(result.diff);
      },
      error: (err) => {
        this.busy.set(false);
        this.diff.set(null);
        this.error.set(describeError(err));
      },
    });
  }
}

import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { AgentApi, AuditView, describeError } from '../../core/api';

@Component({
  selector: 'app-audit',
  imports: [DatePipe],
  template: `
    <section class="panel">
      <h1>Audit log</h1>
      <p class="muted">Read-only. Each entry is written once and cannot be edited or deleted.</p>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      <table>
        <thead>
          <tr><th>Time</th><th>Actor</th><th>Action</th><th>Target</th><th>Result</th><th>Trace</th></tr>
        </thead>
        <tbody>
          @for (entry of entries(); track entry.id) {
            <tr>
              <td>{{ entry.time | date: 'short' }}</td>
              <td>{{ entry.actor }}</td>
              <td>{{ entry.action }}</td>
              <td>{{ entry.target }}</td>
              <td>{{ entry.result }}</td>
              <td><code>{{ entry.traceId ?? '' }}</code></td>
            </tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class AuditComponent implements OnInit {
  private readonly api = inject(AgentApi);

  readonly entries = signal<AuditView[]>([]);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.listAudit(100).subscribe({
      next: (items) => this.entries.set(items),
      error: (err) => this.error.set(describeError(err)),
    });
  }
}

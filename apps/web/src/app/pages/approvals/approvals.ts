import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ApprovalView, AgentApi, describeError } from '../../core/api';

@Component({
  selector: 'app-approvals',
  imports: [DatePipe],
  template: `
    <section class="panel">
      <h1>Pending approvals</h1>
      <p class="muted">Each item runs only if you approve it before it expires. Rejecting it means nothing runs.</p>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      @if (approvals().length === 0) {
        <p class="muted">Nothing is waiting for you.</p>
      }
      <ul class="list">
        @for (item of approvals(); track item.id) {
          <li>
            <div class="row between">
              <strong>{{ item.kind }}</strong>
              <span class="muted">expires {{ item.expiresAt | date: 'short' }}</span>
            </div>
            <p>Target: <code>{{ item.target }}</code></p>
            <div class="row">
              <button type="button" (click)="decide(item.id, true)">Approve</button>
              <button type="button" class="secondary" (click)="decide(item.id, false)">Reject</button>
            </div>
          </li>
        }
      </ul>
    </section>
  `,
})
export class ApprovalsComponent implements OnInit {
  private readonly api = inject(AgentApi);

  readonly approvals = signal<ApprovalView[]>([]);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  decide(id: string, approve: boolean): void {
    const request = approve ? this.api.approve(id) : this.api.reject(id);
    request.subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(describeError(err)),
    });
  }

  private load(): void {
    this.api.listApprovals('Pending').subscribe({
      next: (items) => this.approvals.set(items),
      error: (err) => this.error.set(describeError(err)),
    });
  }
}

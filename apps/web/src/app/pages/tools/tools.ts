import { Component, OnInit, inject, signal } from '@angular/core';
import { AgentApi, ToolView, describeError } from '../../core/api';

@Component({
  selector: 'app-tools',
  template: `
    <section class="panel">
      <h1>Tools</h1>
      <p class="muted">
        Tools a plan may use. Read-only tools run without a question. Write and external-send tools wait for approval.
      </p>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      @if (loaded() && tools().length === 0 && !error()) {
        <p class="muted">No tools are configured. Add MCP servers under Agent:McpServers.</p>
      }
      <ul class="list">
        @for (tool of tools(); track tool.name) {
          <li>
            <strong>{{ tool.name }}</strong>
            <span class="badge">{{ label(tool.sideEffect) }}</span>
            @if (tool.requiredInputs.length > 0) {
              <div class="muted">Needs: {{ tool.requiredInputs.join(', ') }}</div>
            }
          </li>
        }
      </ul>
    </section>
  `,
})
export class ToolsComponent implements OnInit {
  private readonly api = inject(AgentApi);

  readonly tools = signal<ToolView[]>([]);
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.listTools().subscribe({
      next: (items) => {
        this.tools.set(items);
        this.loaded.set(true);
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loaded.set(true);
      },
    });
  }

  label(sideEffect: ToolView['sideEffect']): string {
    switch (sideEffect) {
      case 'ReadOnly':
        return 'Read only';
      case 'Write':
        return 'Writes data (needs approval)';
      case 'ExternalSend':
        return 'Sends outside (needs approval)';
    }
  }
}

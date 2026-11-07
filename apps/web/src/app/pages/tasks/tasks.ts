import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { interval, switchMap, startWith } from 'rxjs';
import { renderMarkdown } from '../../core/markdown';
import { AgentApi, ApprovalView, TaskStatus, TaskView, describeError } from '../../core/api';
import { createRecognizer, detectSpeech, speak, SpeechRecognitionLike } from '../../core/speech';

const TERMINAL: TaskStatus[] = ['Done', 'Failed', 'Cancelled'];

// The current chat survives a page reload in this browser tab. Storage can be blocked, so it is optional.
const CONVERSATION_KEY = 'agent.conversation';

// Approval ids appear in the agent's replies as "Waiting for approval: <id>".
const APPROVAL_ID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

const TARGET_LABELS: Record<string, string> = {
  workspace_edit: 'Edit a file',
  workspace_create: 'Create a file',
  workspace_run: 'Run a script',
};

function loadConversationId(): string {
  try {
    const saved = sessionStorage.getItem(CONVERSATION_KEY);
    if (saved) {
      return saved;
    }
  } catch {
    // Storage unavailable. A fresh chat is used for this page only.
  }
  const id = crypto.randomUUID();
  saveConversationId(id);
  return id;
}

function saveConversationId(id: string): void {
  try {
    sessionStorage.setItem(CONVERSATION_KEY, id);
  } catch {
    // Storage unavailable. The chat still works until the page reloads.
  }
}

@Component({
  selector: 'app-tasks',
  imports: [FormsModule, DatePipe],
  template: `
    <section class="panel">
      <h1>Chat</h1>
      <form (ngSubmit)="submit()">
        <label for="prompt">What should the agent do?</label>
        <textarea id="prompt" name="prompt" rows="3" maxlength="4000" [(ngModel)]="prompt"></textarea>
        <p class="muted">Chat {{ conversationId().slice(0, 8) }}: earlier messages in this chat are sent as context.</p>
        <div class="row">
          <button type="submit" [disabled]="busy() || prompt.trim().length === 0">Send</button>
          <button type="button" class="secondary" (click)="newChat()">New chat</button>
          @if (canListen) {
            <button type="button" class="secondary" (click)="listen()" [disabled]="listening()">
              {{ listening() ? 'Listening…' : 'Speak prompt' }}
            </button>
          }
        </div>
      </form>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </section>

    <section class="panel">
      <h2>This chat</h2>
      @if (chat().length === 0) {
        <p class="muted">No messages yet. Send one above.</p>
      }
      <ul class="list">
        @for (task of chat(); track task.id) {
          <li>
            <p><strong>You:</strong> {{ task.prompt }}</p>
            @if (task.result) {
              <div class="result" [innerHTML]="render(task.result)"></div>
              @if (canSpeak) {
                <button type="button" class="secondary" (click)="read(task.result)">Read aloud</button>
              }
            }
            @for (id of approvalIdsOf(task.result); track id) {
              @if (approvals().get(id); as approval) {
                <div class="approval-card">
                  <div class="row between">
                    <strong>Approval needed: {{ label(approval.target) }}</strong>
                    <span class="badge">{{ approval.status }}</span>
                  </div>
                  @if (approval.status === 'Pending') {
                    <p class="muted">Nothing runs until you approve. Expires {{ approval.expiresAt | date: 'short' }}.</p>
                    <div class="row">
                      <button type="button" (click)="decide(approval.id, true)" [disabled]="busy()">Approve</button>
                      <button type="button" class="secondary" (click)="decide(approval.id, false)" [disabled]="busy()">Reject</button>
                    </div>
                  } @else {
                    <p class="muted">{{ approval.resultSummary ?? approval.status }}</p>
                  }
                </div>
              }
            }
            @if (task.error) {
              <p class="error">Failed: {{ task.error }}</p>
            }
          </li>
        }
      </ul>
    </section>
  `,
})
export class TasksComponent implements OnInit {
  private readonly api = inject(AgentApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scope = globalThis as unknown as Record<string, unknown>;
  private readonly support = detectSpeech(this.scope);
  private recognizer: SpeechRecognitionLike | null = null;

  prompt = '';
  readonly tasks = signal<TaskView[]>([]);
  readonly conversationId = signal<string>(loadConversationId());
  readonly busy = signal(false);
  readonly listening = signal(false);
  readonly error = signal<string | null>(null);
  readonly approvals = signal<Map<string, ApprovalView>>(new Map());

  // Only the messages of the current chat, oldest first.
  readonly chat = computed(() =>
    this.tasks()
      .filter((task) => task.conversationId === this.conversationId())
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt)),
  );

  get canListen(): boolean {
    return this.support.recognition;
  }

  get canSpeak(): boolean {
    return this.support.synthesis;
  }

  ngOnInit(): void {
    this.refresh();
    // Refresh the task list every two seconds while the page is open.
    interval(2000)
      .pipe(
        startWith(0),
        switchMap(() => this.api.listTasks(50)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (tasks) => this.applyTasks(tasks),
        error: (err) => this.error.set(describeError(err)),
      });
  }

  submit(): void {
    const text = this.prompt.trim();
    if (text.length === 0) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.submitTask(text, crypto.randomUUID(), this.conversationId()).subscribe({
      next: () => {
        this.prompt = '';
        this.busy.set(false);
        this.refresh();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(describeError(err));
      },
    });
  }

  newChat(): void {
    const id = crypto.randomUUID();
    this.conversationId.set(id);
    saveConversationId(id);
    this.error.set(null);
  }

  decide(id: string, approve: boolean): void {
    this.busy.set(true);
    this.error.set(null);
    const request = approve ? this.api.approve(id) : this.api.reject(id);
    request.subscribe({
      next: (view) => {
        this.busy.set(false);
        this.approvals.update((map) => new Map(map).set(id, view));
        this.refresh();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(describeError(err));
      },
    });
  }

  approvalIdsOf(text: string | null): string[] {
    if (!text || !text.includes('pproval')) {
      return [];
    }
    return [...new Set(text.match(APPROVAL_ID) ?? [])];
  }

  label(target: string): string {
    return TARGET_LABELS[target] ?? target;
  }

  listen(): void {
    this.recognizer = createRecognizer(this.scope);
    if (!this.recognizer) {
      this.error.set('Speech input is not available in this browser.');
      return;
    }
    this.listening.set(true);
    this.recognizer.onresult = (event) => {
      this.prompt = event.results[0][0].transcript;
    };
    this.recognizer.onerror = () => {
      this.error.set('Speech input failed. Type the prompt instead.');
    };
    this.recognizer.onend = () => this.listening.set(false);
    this.recognizer.start();
  }

  render(text: string | null): string {
    return text ? renderMarkdown(text) : '';
  }

  read(text: string | null): void {
    if (text === null || !speak(text, this.scope)) {
      this.error.set('Read-aloud is not available in this browser.');
    }
  }

  private refresh(): void {
    this.api.listTasks(50).subscribe({
      next: (tasks) => this.applyTasks(tasks),
      error: (err) => this.error.set(describeError(err)),
    });
  }

  private applyTasks(tasks: TaskView[]): void {
    this.tasks.set(tasks);
    this.loadApprovals(tasks);
  }

  // Fetch each approval that a reply mentions. Pending ones are refreshed so their buttons change when decided.
  private loadApprovals(tasks: TaskView[]): void {
    const ids = new Set(tasks.flatMap((task) => this.approvalIdsOf(task.result)));
    for (const id of ids) {
      const known = this.approvals().get(id);
      if (known && known.status !== 'Pending') {
        continue;
      }
      this.api.getApproval(id).subscribe({
        next: (view) => this.approvals.update((map) => new Map(map).set(id, view)),
        error: () => {
          // A missing or unreadable approval shows no card, so the rest of the page still works.
        },
      });
    }
  }
}

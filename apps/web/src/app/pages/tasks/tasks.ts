import { DatePipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { interval, switchMap, startWith } from 'rxjs';
import { renderMarkdown } from '../../core/markdown';
import { AgentApi, ApprovalView, TaskView, describeError } from '../../core/api';
import { createRecognizer, detectSpeech, speak, SpeechRecognitionLike } from '../../core/speech';

// The current chat survives a page reload in this browser tab. Storage can be blocked, so it is optional.
const CONVERSATION_KEY = 'agent.conversation';

// Approval ids appear in the agent's replies as "Waiting for approval: <id>".
const APPROVAL_ID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

const TARGET_LABELS: Record<string, string> = {
  workspace_edit: 'Edit a file',
  workspace_create: 'Create a file',
  workspace_run: 'Run a script',
};

const TITLE_LENGTH = 48;

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

interface ChatSummary {
  id: string;
  title: string;
  updatedAt: string;
}

@Component({
  selector: 'app-tasks',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="chat-shell">
      <aside class="sidebar">
        <button type="button" class="new-chat" (click)="newChat()">+ New chat</button>
        <p class="section-label">Chats</p>
        @if (conversations().length === 0) {
          <p class="muted small">Your chats appear here.</p>
        }
        <ul class="chat-list">
          @for (chat of conversations(); track chat.id) {
            <li>
              <button
                type="button"
                class="chat-item"
                [class.active]="chat.id === conversationId()"
                (click)="openChat(chat.id)"
              >
                <span class="chat-title">{{ chat.title }}</span>
                <span class="chat-time">{{ chat.updatedAt | date: 'MMM d, HH:mm' }}</span>
              </button>
            </li>
          }
        </ul>
      </aside>

      <section class="chat-main">
        <div class="thread" #thread>
          @if (messages().length === 0) {
            <div class="empty">
              <h2>What can I help you with?</h2>
              <p class="muted">Ask a question, or ask me to read, search, or change files in your workspace.</p>
            </div>
          }

          @for (task of messages(); track task.id) {
            <div class="turn">
              <div class="user-row">
                <div class="user-bubble">{{ task.prompt }}</div>
              </div>

              <div class="assistant-row">
                @if (task.result) {
                  <div class="result" [innerHTML]="render(task.result)"></div>
                  @if (canSpeak) {
                    <button type="button" class="link" (click)="read(task.result)">Read aloud</button>
                  }
                } @else if (task.status === 'Queued' || task.status === 'Running') {
                  <p class="muted thinking">Thinking…</p>
                }

                @for (id of approvalIdsOf(task.result); track id) {
                  @if (approvals().get(id); as approval) {
                    <div class="approval-card">
                      <div class="row between">
                        <strong>Approval needed: {{ label(approval.target) }}</strong>
                        <span class="badge">{{ approval.status }}</span>
                      </div>
                      @if (approval.status === 'Pending') {
                        <p class="muted small">Nothing runs until you approve. Expires {{ approval.expiresAt | date: 'short' }}.</p>
                        <div class="row">
                          <button type="button" (click)="decide(approval.id, true)" [disabled]="busy()">Approve</button>
                          <button type="button" class="secondary" (click)="decide(approval.id, false)" [disabled]="busy()">Reject</button>
                        </div>
                      } @else {
                        <p class="muted small">{{ approval.resultSummary ?? approval.status }}</p>
                      }
                    </div>
                  }
                }

                @if (task.error) {
                  <p class="error">Failed: {{ task.error }}</p>
                }
              </div>
            </div>
          }
        </div>

        @if (error()) {
          <p class="error bar" role="alert">{{ error() }}</p>
        }

        <form class="composer" (ngSubmit)="submit()">
          <textarea
            name="prompt"
            rows="2"
            maxlength="4000"
            placeholder="Message the agent"
            aria-label="Message the agent"
            [(ngModel)]="prompt"
            (keydown.enter)="onEnter($event)"
          ></textarea>
          <div class="composer-actions">
            @if (canListen) {
              <button type="button" class="link" (click)="listen()" [disabled]="listening()">
                {{ listening() ? 'Listening…' : 'Speak' }}
              </button>
            }
            <button type="submit" [disabled]="busy() || prompt.trim().length === 0">Send</button>
          </div>
        </form>
        <p class="footnote muted">Chat {{ conversationId().slice(0, 8) }}. Earlier messages in this chat are sent as context.</p>
      </section>
    </div>
  `,
  styles: [
    `
      .chat-shell {
        display: grid;
        grid-template-columns: 260px 1fr;
        height: calc(100vh - 64px);
        min-height: 480px;
        margin: -1rem;
        background: var(--bg);
      }
      .sidebar {
        border-right: 1px solid rgba(127, 127, 127, 0.25);
        padding: 0.75rem;
        overflow-y: auto;
        background: var(--panel);
      }
      .new-chat {
        width: 100%;
        margin-bottom: 0.75rem;
      }
      .section-label {
        font-size: 0.75rem;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        opacity: 0.6;
        margin: 0.5rem 0;
      }
      .chat-list {
        list-style: none;
        padding: 0;
        margin: 0;
      }
      .chat-item {
        width: 100%;
        display: flex;
        flex-direction: column;
        align-items: flex-start;
        text-align: left;
        background: transparent;
        color: inherit;
        border: none;
        border-radius: 8px;
        padding: 0.5rem 0.6rem;
        margin-bottom: 0.2rem;
        cursor: pointer;
      }
      .chat-item:hover,
      .chat-item.active {
        background: rgba(127, 127, 127, 0.15);
      }
      .chat-title {
        font-size: 0.92rem;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 100%;
      }
      .chat-time {
        font-size: 0.72rem;
        opacity: 0.6;
      }
      .chat-main {
        display: flex;
        flex-direction: column;
        min-width: 0;
        height: 100%;
      }
      .thread {
        flex: 1;
        overflow-y: auto;
        padding: 1.5rem clamp(1rem, 6vw, 4rem);
      }
      .empty {
        text-align: center;
        margin-top: 15vh;
      }
      .turn {
        max-width: 760px;
        margin: 0 auto 1.5rem;
      }
      .user-row {
        display: flex;
        justify-content: flex-end;
      }
      .user-bubble {
        background: rgba(127, 127, 127, 0.16);
        border-radius: 16px;
        padding: 0.6rem 0.9rem;
        max-width: 80%;
        white-space: pre-wrap;
      }
      .assistant-row {
        margin-top: 0.75rem;
        line-height: 1.6;
      }
      .thinking {
        font-style: italic;
      }
      .composer {
        max-width: 760px;
        width: calc(100% - 2rem);
        margin: 0 auto;
        border: 1px solid rgba(127, 127, 127, 0.35);
        border-radius: 14px;
        background: var(--panel);
        padding: 0.6rem 0.75rem;
      }
      .composer textarea {
        width: 100%;
        border: none;
        background: transparent;
        color: inherit;
        resize: none;
        font: inherit;
        outline: none;
      }
      .composer-actions {
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
        align-items: center;
      }
      .bar {
        max-width: 760px;
        margin: 0.5rem auto;
      }
      .footnote {
        text-align: center;
        font-size: 0.75rem;
        margin: 0.4rem 0 0.6rem;
      }
      .link {
        background: none;
        border: none;
        color: inherit;
        text-decoration: underline;
        padding: 0.2rem 0;
        cursor: pointer;
        font: inherit;
        font-size: 0.85rem;
      }
      .small {
        font-size: 0.85rem;
      }
      @media (max-width: 720px) {
        .chat-shell {
          grid-template-columns: 1fr;
        }
        .sidebar {
          display: none;
        }
      }
    `,
  ],
})
export class TasksComponent implements OnInit {
  private readonly api = inject(AgentApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scope = globalThis as unknown as Record<string, unknown>;
  private readonly support = detectSpeech(this.scope);
  private readonly thread = viewChild<ElementRef<HTMLElement>>('thread');
  private recognizer: SpeechRecognitionLike | null = null;

  prompt = '';
  readonly tasks = signal<TaskView[]>([]);
  readonly conversationId = signal<string>(loadConversationId());
  readonly busy = signal(false);
  readonly listening = signal(false);
  readonly error = signal<string | null>(null);
  readonly approvals = signal<Map<string, ApprovalView>>(new Map());

  // Messages of the open chat, oldest first.
  readonly messages = computed(() =>
    this.tasks()
      .filter((task) => task.conversationId === this.conversationId())
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt)),
  );

  // One entry per chat, newest activity first. Standalone tasks are not listed.
  readonly conversations = computed<ChatSummary[]>(() => {
    const byId = new Map<string, ChatSummary>();
    for (const task of [...this.tasks()].sort((a, b) => a.createdAt.localeCompare(b.createdAt))) {
      if (!task.conversationId) {
        continue;
      }
      const existing = byId.get(task.conversationId);
      if (!existing) {
        byId.set(task.conversationId, {
          id: task.conversationId,
          title: task.prompt.length > TITLE_LENGTH ? task.prompt.slice(0, TITLE_LENGTH) + '…' : task.prompt,
          updatedAt: task.updatedAt,
        });
      } else if (task.updatedAt > existing.updatedAt) {
        existing.updatedAt = task.updatedAt;
      }
    }
    return [...byId.values()].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  });

  get canListen(): boolean {
    return this.support.recognition;
  }

  get canSpeak(): boolean {
    return this.support.synthesis;
  }

  ngOnInit(): void {
    this.refresh();
    // Refresh every two seconds while the page is open, so replies and approvals appear without a reload.
    interval(2000)
      .pipe(
        startWith(0),
        switchMap(() => this.api.listTasks(100)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (tasks) => this.applyTasks(tasks),
        error: (err) => this.error.set(describeError(err)),
      });
  }

  onEnter(event: Event): void {
    const keyboard = event as KeyboardEvent;
    if (!keyboard.shiftKey) {
      keyboard.preventDefault();
      this.submit();
    }
  }

  submit(): void {
    const text = this.prompt.trim();
    if (text.length === 0 || this.busy()) {
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
    this.openChat(crypto.randomUUID());
  }

  openChat(id: string): void {
    this.conversationId.set(id);
    saveConversationId(id);
    this.error.set(null);
    this.scrollToEnd();
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
      this.error.set('Speech input failed. Type the message instead.');
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
    this.api.listTasks(100).subscribe({
      next: (tasks) => this.applyTasks(tasks),
      error: (err) => this.error.set(describeError(err)),
    });
  }

  private applyTasks(tasks: TaskView[]): void {
    this.tasks.set(tasks);
    this.loadApprovals(tasks);
    this.scrollToEnd();
  }

  // Fetch each approval a reply mentions. Pending ones are refreshed so their buttons change once decided.
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
          // A missing approval shows no card, so the rest of the page still works.
        },
      });
    }
  }

  private scrollToEnd(): void {
    setTimeout(() => {
      const element = this.thread()?.nativeElement;
      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    });
  }
}

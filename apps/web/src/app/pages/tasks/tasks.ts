import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { interval, switchMap, startWith } from 'rxjs';
import { AgentApi, TaskStatus, TaskView, describeError } from '../../core/api';
import { createRecognizer, detectSpeech, speak, SpeechRecognitionLike } from '../../core/speech';

const TERMINAL: TaskStatus[] = ['Done', 'Failed', 'Cancelled'];

@Component({
  selector: 'app-tasks',
  imports: [FormsModule, DatePipe],
  template: `
    <section class="panel">
      <h1>Tasks</h1>
      <form (ngSubmit)="submit()">
        <label for="prompt">What should the agent do?</label>
        <textarea id="prompt" name="prompt" rows="3" maxlength="4000" [(ngModel)]="prompt"></textarea>
        <div class="row">
          <button type="submit" [disabled]="busy() || prompt.trim().length === 0">Submit</button>
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
      <h2>Recent</h2>
      @if (tasks().length === 0) {
        <p class="muted">No tasks yet.</p>
      }
      <ul class="list">
        @for (task of tasks(); track task.id) {
          <li>
            <div class="row between">
              <strong>{{ task.status }}</strong>
              <span class="muted">{{ task.createdAt | date: 'short' }}</span>
            </div>
            <p>{{ task.prompt }}</p>
            @if (task.result) {
              <pre class="result">{{ task.result }}</pre>
              @if (canSpeak) {
                <button type="button" class="secondary" (click)="read(task.result)">Read aloud</button>
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
  readonly busy = signal(false);
  readonly listening = signal(false);
  readonly error = signal<string | null>(null);

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
        switchMap(() => this.api.listTasks(20)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (tasks) => this.tasks.set(tasks),
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
    this.api.submitTask(text, crypto.randomUUID()).subscribe({
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

  read(text: string | null): void {
    if (text === null || !speak(text, this.scope)) {
      this.error.set('Read-aloud is not available in this browser.');
    }
  }

  private refresh(): void {
    this.api.listTasks(20).subscribe({
      next: (tasks) => this.tasks.set(tasks),
      error: (err) => this.error.set(describeError(err)),
    });
  }
}

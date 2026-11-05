import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AgentApi, ScheduleView, describeError } from '../../core/api';

@Component({
  selector: 'app-schedules',
  imports: [FormsModule, DatePipe],
  template: `
    <section class="panel">
      <h1>Schedules</h1>
      <p class="muted">Times are UTC. Preview the next runs, then confirm to save.</p>
      <form>
        <label for="name">Name</label>
        <input id="name" name="name" [(ngModel)]="name" maxlength="64" />
        <label for="cron">Cron (five fields)</label>
        <input id="cron" name="cron" [(ngModel)]="cron" placeholder="0 7 * * 1-5" />
        <label for="prompt">Task to run</label>
        <textarea id="prompt" name="prompt" rows="2" [(ngModel)]="prompt"></textarea>
        <div class="row">
          <button type="button" class="secondary" (click)="preview()">Preview</button>
          <button type="button" (click)="save()" [disabled]="!previewed()">Confirm and save</button>
        </div>
      </form>
      @if (nextRuns().length > 0) {
        <p>Next runs:</p>
        <ul>
          @for (run of nextRuns(); track run) {
            <li>{{ run | date: 'medium' : 'UTC' }} UTC</li>
          }
        </ul>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </section>

    <section class="panel">
      <h2>Saved</h2>
      <ul class="list">
        @for (item of schedules(); track item.id) {
          <li><strong>{{ item.name }}</strong> <code>{{ item.cron }}</code></li>
        }
      </ul>
    </section>
  `,
})
export class SchedulesComponent implements OnInit {
  private readonly api = inject(AgentApi);

  name = '';
  cron = '';
  prompt = '';
  readonly nextRuns = signal<string[]>([]);
  readonly previewed = signal(false);
  readonly schedules = signal<ScheduleView[]>([]);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.reloadSaved();
  }

  preview(): void {
    this.previewed.set(false);
    this.api.previewSchedule(this.cron.trim()).subscribe({
      next: (result) => {
        this.nextRuns.set(result.nextRuns);
        this.previewed.set(true);
        this.error.set(null);
      },
      error: (err) => {
        this.nextRuns.set([]);
        this.error.set(describeError(err));
      },
    });
  }

  save(): void {
    this.api.createSchedule(this.name.trim(), this.cron.trim(), this.prompt.trim()).subscribe({
      next: () => {
        this.previewed.set(false);
        this.nextRuns.set([]);
        this.name = '';
        this.cron = '';
        this.prompt = '';
        this.reloadSaved();
      },
      error: (err) => this.error.set(describeError(err)),
    });
  }

  private reloadSaved(): void {
    this.api.listSchedules().subscribe({
      next: (items) => this.schedules.set(items),
      error: (err) => this.error.set(describeError(err)),
    });
  }
}

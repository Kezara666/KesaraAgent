import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthStore } from '../../core/auth';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <section class="panel narrow">
      <h1>Sign in</h1>
      <p class="muted">Paste your API token. It stays in this tab only and is cleared when the tab closes.</p>
      <form (ngSubmit)="submit()">
        <label for="token">API token</label>
        <input id="token" name="token" type="password" autocomplete="off" [(ngModel)]="token" required />
        <button type="submit" [disabled]="token.trim().length === 0">Sign in</button>
      </form>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  token = '';
  readonly error = signal<string | null>(null);

  submit(): void {
    if (this.token.trim().length === 0) {
      this.error.set('Enter a token.');
      return;
    }
    this.auth.signIn(this.token);
    this.token = '';
    this.router.navigateByUrl('/tasks');
  }
}

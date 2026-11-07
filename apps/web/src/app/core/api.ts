import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type TaskStatus = 'Queued' | 'Running' | 'WaitingApproval' | 'Done' | 'Failed' | 'Cancelled';

export interface TaskView {
  id: string;
  status: TaskStatus;
  prompt: string;
  result: string | null;
  error: string | null;
  createdAt: string;
  updatedAt: string;
  conversationId: string | null;
}

export interface ApprovalView {
  id: string;
  kind: string;
  target: string;
  status: string;
  createdAt: string;
  expiresAt: string;
  resultSummary: string | null;
}

export interface AuditView {
  id: string;
  time: string;
  actor: string;
  action: string;
  target: string;
  result: string;
  traceId: string | null;
}

export interface SchedulePreview {
  cron: string;
  nextRuns: string[];
}

export interface ScheduleView {
  id: string;
  name: string;
  cron: string;
  enabled: boolean;
}

export interface ToolView {
  name: string;
  sideEffect: 'ReadOnly' | 'Write' | 'ExternalSend';
  requiredInputs: string[];
}

/** Typed client for the agent API. Calls use a relative base path, and the interceptor adds the token. */
@Injectable({ providedIn: 'root' })
export class AgentApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1';

  submitTask(prompt: string, idempotencyKey: string, conversationId?: string): Observable<TaskView> {
    return this.http.post<TaskView>(
      `${this.base}/tasks`,
      conversationId ? { prompt, conversationId } : { prompt },
      { headers: { 'Idempotency-Key': idempotencyKey } },
    );
  }

  getTask(id: string): Observable<TaskView> {
    return this.http.get<TaskView>(`${this.base}/tasks/${id}`);
  }

  listTasks(limit = 20): Observable<TaskView[]> {
    return this.http.get<TaskView[]>(`${this.base}/tasks`, { params: new HttpParams().set('limit', limit) });
  }

  listApprovals(status = 'Pending'): Observable<ApprovalView[]> {
    return this.http.get<ApprovalView[]>(`${this.base}/approvals`, { params: new HttpParams().set('status', status) });
  }

  approve(id: string): Observable<ApprovalView> {
    return this.http.post<ApprovalView>(`${this.base}/approvals/${id}/approve`, null);
  }

  reject(id: string): Observable<ApprovalView> {
    return this.http.post<ApprovalView>(`${this.base}/approvals/${id}/reject`, null);
  }

  listAudit(limit = 100): Observable<AuditView[]> {
    return this.http.get<AuditView[]>(`${this.base}/audit`, { params: new HttpParams().set('limit', limit) });
  }

  previewSchedule(cron: string): Observable<SchedulePreview> {
    return this.http.post<SchedulePreview>(`${this.base}/schedules/preview`, { cron, count: 3 });
  }

  createSchedule(name: string, cron: string, prompt: string): Observable<ScheduleView> {
    return this.http.post<ScheduleView>(`${this.base}/schedules`, { name, cron, prompt, confirm: true });
  }

  listSchedules(): Observable<ScheduleView[]> {
    return this.http.get<ScheduleView[]>(`${this.base}/schedules`);
  }

  listTools(): Observable<ToolView[]> {
    return this.http.get<ToolView[]>(`${this.base}/tools`);
  }
}

/** Turns an HTTP error into a short message. Response bodies are never shown, because they could echo secrets. */
export function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) return 'Sign in with a valid token.';
    if (error.status === 403) return 'Your token does not allow this action.';
    if (error.status === 409) return 'That item cannot be changed in its current state.';
    if (error.status === 0) return 'The agent API cannot be reached.';
    return `The request failed (HTTP ${error.status}).`;
  }
  return 'Something went wrong.';
}

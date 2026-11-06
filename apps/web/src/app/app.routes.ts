import { Routes } from '@angular/router';
import { authGuard } from './core/guard';
import { LoginComponent } from './pages/login/login';
import { TasksComponent } from './pages/tasks/tasks';
import { ApprovalsComponent } from './pages/approvals/approvals';
import { AuditComponent } from './pages/audit/audit';
import { SchedulesComponent } from './pages/schedules/schedules';
import { WorkspaceComponent } from './pages/workspace/workspace';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { path: 'tasks', component: TasksComponent, canActivate: [authGuard] },
  { path: 'approvals', component: ApprovalsComponent, canActivate: [authGuard] },
  { path: 'schedules', component: SchedulesComponent, canActivate: [authGuard] },
  { path: 'workspace', component: WorkspaceComponent, canActivate: [authGuard] },
  { path: 'audit', component: AuditComponent, canActivate: [authGuard] },
  { path: '', pathMatch: 'full', redirectTo: 'tasks' },
  { path: '**', redirectTo: 'tasks' },
];

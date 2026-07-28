import { ChangeDetectionStrategy, Component } from '@angular/core'; import { MatCardModule } from '@angular/material/card';
@Component({ imports: [MatCardModule], template: '<mat-card><mat-card-header><mat-card-title>Dashboard</mat-card-title></mat-card-header><mat-card-content>WhatsBiz ERP platform services are available.</mat-card-content></mat-card>', changeDetection: ChangeDetectionStrategy.OnPush })
export class DashboardComponent {}

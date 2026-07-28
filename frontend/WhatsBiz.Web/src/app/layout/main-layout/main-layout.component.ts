import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterOutlet } from '@angular/router';
@Component({ selector: 'app-main-layout', imports: [MatToolbarModule, RouterOutlet], template: '<mat-toolbar color="primary">WhatsBiz ERP</mat-toolbar><main><router-outlet /></main>', styles: ['main { padding: 1.5rem; }'], changeDetection: ChangeDetectionStrategy.OnPush })
export class MainLayoutComponent {}

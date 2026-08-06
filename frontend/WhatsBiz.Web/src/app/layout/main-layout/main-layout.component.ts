import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink, RouterOutlet } from '@angular/router';
@Component({ selector: 'app-main-layout', imports: [MatButtonModule, MatToolbarModule, RouterLink, RouterOutlet], template: '<mat-toolbar color="primary"><a mat-button routerLink="/dashboard">WhatsBiz ERP</a><span class="spacer"></span><a mat-button routerLink="/products">Products</a><a mat-button routerLink="/suppliers">Suppliers</a><a mat-button routerLink="/customers">Customers</a><a mat-button routerLink="/product-categories">Categories</a><a mat-button routerLink="/brands">Brands</a><a mat-button routerLink="/units">Units</a></mat-toolbar><main><router-outlet /></main>', styles: ['main { padding: 1.5rem; max-width: 1440px; margin: auto; } .spacer { flex: 1; } @media(max-width:700px){mat-toolbar{overflow:auto}}'], changeDetection: ChangeDetectionStrategy.OnPush })
export class MainLayoutComponent {}

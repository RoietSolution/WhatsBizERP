import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { InventoryApiService } from './inventory-api.service';
import { InventorySummary, WarehouseOption } from './inventory.models';

@Component({
  imports: [DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatSelectModule],
  template: `<header><h1>Inventory Dashboard</h1><mat-form-field><mat-label>Warehouse</mat-label><mat-select [(ngModel)]="warehouseId" (selectionChange)="load()"><mat-option value="">All warehouses</mat-option>@for(x of warehouses();track x.warehouseId){<mat-option [value]="x.warehouseId">{{x.warehouseName}}</mat-option>}</mat-select></mat-form-field></header>@if(summary();as s){<div class="cards"><mat-card><mat-card-title>{{s.totalQuantity}}</mat-card-title><mat-card-subtitle>Total stock</mat-card-subtitle></mat-card><mat-card><mat-card-title>{{s.totalStockValue|number:'1.2-2'}}</mat-card-title><mat-card-subtitle>Total stock value</mat-card-subtitle></mat-card><mat-card><mat-card-title>{{s.reservedStock}}</mat-card-title><mat-card-subtitle>Reserved stock</mat-card-subtitle></mat-card><mat-card><mat-card-title>{{s.lowStockProducts}}</mat-card-title><mat-card-subtitle>Low stock</mat-card-subtitle></mat-card><mat-card><mat-card-title>{{s.outOfStockProducts}}</mat-card-title><mat-card-subtitle>Out of stock</mat-card-subtitle></mat-card></div>}<nav><a mat-flat-button routerLink="/inventory/balance">Stock Balance</a><a mat-button routerLink="/inventory/transactions">Transactions</a><a mat-button routerLink="/inventory/adjustment">Adjust Stock</a><a mat-button routerLink="/inventory/transfer">Transfer Stock</a><a mat-button routerLink="/inventory/reservation">Reservations</a></nav>`,
  styles: [`header,nav{display:flex;justify-content:space-between;gap:1rem;align-items:center;flex-wrap:wrap}.cards{display:grid;grid-template-columns:repeat(5,1fr);gap:1rem;margin:1rem 0}mat-card{padding:1rem}@media(max-width:900px){.cards{grid-template-columns:repeat(2,1fr)}}`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InventoryDashboardComponent {
  readonly summary=signal<InventorySummary|null>(null);readonly warehouses=signal<WarehouseOption[]>([]);warehouseId='';
  constructor(private readonly api:InventoryApiService){api.warehouses().subscribe(x=>this.warehouses.set(x));this.load()}
  load(){this.api.summary(this.warehouseId||undefined).subscribe(x=>this.summary.set(x))}
}

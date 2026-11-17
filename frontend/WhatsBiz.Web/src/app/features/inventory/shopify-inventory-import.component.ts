import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { InventoryApiService } from './inventory-api.service';
import { WarehouseOption } from './inventory.models';

@Component({ selector: 'app-shopify-inventory-import', standalone: true, imports: [CommonModule, FormsModule], templateUrl: './shopify-inventory-import.component.html', styleUrl: './shopify-inventory-import.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class ShopifyInventoryImportComponent {
  readonly file = signal<File | null>(null); readonly sourceStore = signal(''); readonly preview = signal<any>(null); readonly busy = signal(false); readonly error = signal(''); readonly result = signal<any>(null); readonly warehouses = signal<WarehouseOption[]>([]); readonly history = signal<any[]>([]); readonly view = signal<'all'|'changed'|'unmatched'|'errors'>('all');
  readonly mappings: Record<string,string> = {};
  applyHsnMetadata = false;
  constructor(private readonly api: InventoryApiService) { api.warehouses().subscribe({ next: x => this.warehouses.set(x), error: () => this.error.set('Authorized warehouses could not be loaded.') }); this.loadHistory(); }
  loadHistory() { this.api.shopifyInventoryHistory().subscribe({ next: x => this.history.set(x), error: () => this.history.set([]) }); }
  chooseFile(event: Event) { this.file.set((event.target as HTMLInputElement).files?.[0] ?? null); this.preview.set(null); this.result.set(null); this.error.set(''); }
  locations(): string[] { return [...new Set((this.preview()?.rows ?? []).map((x: any) => x.location).filter(Boolean))] as string[]; }
  rows(): any[] { const rows = this.preview()?.rows ?? []; return rows.filter((x: any) => this.view()==='changed' ? x.adjustment!==0 && x.status==='READY' : this.view()==='unmatched' ? x.errors?.some((e: string)=>e.includes('matches')) : this.view()==='errors' ? x.status==='ERROR' : true); }
  countStatus(status: string): number { return (this.preview()?.rows ?? []).filter((x: any) => x.status === status).length; }
  hasErrors(): boolean { return this.countStatus('ERROR') > 0; }
  hasHsnMetadata(): boolean { return (this.preview()?.rows ?? []).some((x: any) => !!x.hsnCode); }
  previewFile() { if (!this.file() || !this.sourceStore().trim()) { this.error.set('Choose a Shopify inventory file and enter its Shopify source store.'); return; } this.busy.set(true); this.error.set(''); this.api.previewShopifyInventory(this.file()!,this.sourceStore(),this.mappings).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>this.preview.set(x),error:e=>this.error.set(e?.error?.message??'The inventory snapshot could not be previewed.')}); }
  refresh() { this.previewFile(); }
  commit() { const p=this.preview(); if(!p||p.alreadyImported||this.rows().some(x=>x.status==='ERROR'))return; const expectedRows=p.rows.map((x:any)=>({key:x.key,current:x.current})); this.busy.set(true); this.error.set(''); this.api.commitShopifyInventory(this.file()!,this.sourceStore(),this.mappings,expectedRows,this.applyHsnMetadata).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.result.set(x);this.loadHistory();},error:e=>this.error.set(e?.error?.message??'Stock was not updated. Refresh preview and review the current quantities.')}); }
  exportErrors() { const rows=(this.preview()?.rows??[]).filter((x:any)=>x.status==='ERROR'); const safe=(value:unknown)=>{const text=String(value??'');return `"${(/^[=+\-@\t\r]/.test(text)?"'":"")}${text.replaceAll('"','""')}"`;}; const csv=['Row,Handle,SKU,Location,Errors',...rows.map((x:any)=>[x.rowNumber,x.handle,x.sku,x.location,(x.errors??[]).join('; ')].map(safe).join(','))].join('\r\n'); const link=document.createElement('a');link.href=URL.createObjectURL(new Blob([csv],{type:'text/csv'}));link.download='shopify-inventory-errors.csv';link.click();URL.revokeObjectURL(link.href); }
}

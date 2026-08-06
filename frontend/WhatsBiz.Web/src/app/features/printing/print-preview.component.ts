import { DecimalPipe } from '@angular/common';
import { Component, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { PrintApiService, PrintTemplate, Printer } from './print-api.service';

@Component({
  imports: [DecimalPipe, FormsModule, MatButtonModule],
  template: `<header><h1>Print Preview</h1><div><button mat-stroked-button (click)="zoomOut()">−</button><span>{{zoom*100|number:'1.0-0'}}%</span><button mat-stroked-button (click)="zoomIn()">+</button><button mat-flat-button (click)="print()">Print</button></div></header><section class="controls"><label>Document type<select [(ngModel)]="type"><option>SALES_INVOICE</option><option>PURCHASE_INVOICE</option><option>RECEIPT</option><option>PAYMENT_VOUCHER</option><option>CUSTOMER_LEDGER</option><option>SUPPLIER_LEDGER</option><option>GST_REPORT</option></select></label><label>Template<select [(ngModel)]="template"><option value="">Default</option>@for(x of templates();track x.id){<option [value]="x.code">{{x.name}}</option>}</select></label><label>Paper<select [(ngModel)]="paper"><option>A4</option><option>80MM</option><option>58MM</option></select></label><label>Printer<select [(ngModel)]="printer"><option value="">Browser printer</option>@for(x of printers();track x.id){<option [value]="x.id">{{x.displayName}}</option>}</select></label><button mat-flat-button (click)="preview()">Generate preview</button></section><div class="stage">@if(url()){<iframe [src]="url()" [style.transform]="'scale('+zoom+')'"></iframe>}@else{<p>Generate a document to preview it.</p>}</div>`,
  styles: [`header,.controls{display:flex;justify-content:space-between;gap:1rem;flex-wrap:wrap}.controls{padding:1rem;background:#fff}.controls label{display:grid}.stage{margin-top:1rem;min-height:600px;overflow:auto;background:#9da3aa;padding:2rem;text-align:center}iframe{width:210mm;height:297mm;border:0;background:#fff;transform-origin:top center}@media(max-width:800px){iframe{width:100%}}`]
})
export class PrintPreviewComponent implements OnDestroy {
  readonly templates=signal<PrintTemplate[]>([]);readonly printers=signal<Printer[]>([]);readonly url=signal<SafeResourceUrl|null>(null);
  raw='';zoom=1;type='SALES_INVOICE';paper='A4';template='';printer='';
  constructor(private api:PrintApiService,private safe:DomSanitizer){api.templates().subscribe(x=>this.templates.set(x));api.printers().subscribe(x=>this.printers.set(x));}
  zoomOut(){this.zoom=Math.max(.5,this.zoom-.1);}zoomIn(){this.zoom=Math.min(2,this.zoom+.1);}
  preview(){this.api.document({documentType:this.type,documentNumber:'PREVIEW-001',title:this.type.replaceAll('_',' '),bodyHtml:'<h2>Document preview</h2><table><tr><th>Description</th><th>Amount</th></tr><tr><td>Sample item</td><td>100.00</td></tr></table>',paperType:this.paper,output:'html',templateCode:this.template||null}).subscribe(b=>{if(this.raw)URL.revokeObjectURL(this.raw);this.raw=URL.createObjectURL(b);this.url.set(this.safe.bypassSecurityTrustResourceUrl(this.raw));});}
  print(){const frame=document.querySelector('iframe') as HTMLIFrameElement;frame?.contentWindow?.print();}
  ngOnDestroy(){if(this.raw)URL.revokeObjectURL(this.raw);}
}

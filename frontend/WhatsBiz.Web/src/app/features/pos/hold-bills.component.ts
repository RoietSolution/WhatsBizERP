import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { concatMap, from, switchMap, toArray } from 'rxjs';
import { POSApiService } from './pos-api.service';
import { InvoiceList, PaymentMethod } from './pos.models';
import { PaymentDialogComponent, PaymentResult } from './payment-dialog.component';
@Component({
  imports: [RouterLink, MatButtonModule],
  templateUrl: './hold-bills.component.html',
  styles: [
    `
      section {
        display: flex;
        justify-content: space-between;
        padding: 1rem;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      .source-badge { margin-left:.5rem;padding:.2rem .45rem;border-radius:999px;background:#dcfce7;color:#166534;font-size:.75rem; }
      section>span:last-child { display:flex;gap:.4rem; }
      header { display:flex;justify-content:space-between;align-items:center;gap:1rem; }
      header p { color:var(--wb-text-secondary);margin:.3rem 0 1rem; }
    `,
  ],
})
export class HoldBillsComponent {
  readonly bills = signal<InvoiceList[]>([]);
  readonly methods = signal<PaymentMethod[]>([]);
  constructor(
    private readonly api: POSApiService,
    private readonly dialog: MatDialog,
    private readonly snack: MatSnackBar,
  ) {
    this.load();
    this.api.methods().subscribe((x) => this.methods.set(x));
  }
  load(){this.api.invoices('HELD').subscribe((x) => this.bills.set(x.items));}
  complete(x:InvoiceList){
    this.dialog.open(PaymentDialogComponent,{data:{total:x.grandTotal,methods:this.methods(),preferredMethod:'CASH',hasCustomer:!!x.customerName},width:'720px',maxWidth:'96vw'}).afterClosed().subscribe((result:PaymentResult|undefined)=>{
      if(!result)return;
      this.api.completeHeld(x.invoiceId).pipe(
        switchMap(()=>from(result.payments).pipe(concatMap(payment=>this.api.payment({invoiceId:x.invoiceId,...payment})),toArray())),
      ).subscribe({next:()=>{this.snack.open(`Held bill ${x.invoiceNumber} completed.`,undefined,{duration:3000,panelClass:'wb-success'});this.load();this.api.print(x.invoiceId);},error:()=>this.snack.open('The held bill could not be completed. Check stock and payment details, then retry.','Dismiss',{duration:6000})});
    });
  }
  cancel(x:InvoiceList){this.api.cancelHeld(x.invoiceId).subscribe(()=>this.load());}
}

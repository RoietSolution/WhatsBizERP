import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Payment, PaymentMethod } from './pos.models';

@Component({ imports: [CurrencyPipe, FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule], templateUrl: './payment-dialog.component.html', styleUrl: './payment-dialog.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class PaymentDialogComponent {
  readonly payments=signal<Payment[]>([]); readonly paid=signal(0); method:string; amount:number; reference='';
  constructor(@Inject(MAT_DIALOG_DATA)readonly data:{total:number;methods:PaymentMethod[];preferredMethod?:string;quickAmount?:number},private ref:MatDialogRef<PaymentDialogComponent>){this.method=data.preferredMethod==='SPLIT'?'CASH':data.preferredMethod||'CASH';this.amount=data.quickAmount||data.total}
  icon(code:string){return({CASH:'payments',UPI:'qr_code_2',CARD:'credit_card',WALLET:'account_balance_wallet',CREDIT:'schedule'}as Record<string,string>)[code]||'account_balance'}
  balance(){return Math.max(0,this.data.total-this.paid())}
  add(){if(this.amount<=0)return;this.payments.update(x=>[...x,{methodCode:this.method,amount:this.amount,referenceNumber:this.reference||undefined}]);this.recalculate();this.amount=this.balance();this.reference=''}
  remove(index:number){this.payments.update(x=>x.filter((_,i)=>i!==index));this.recalculate()}
  complete(){this.ref.close(this.payments())}
  private recalculate(){this.paid.set(this.payments().reduce((a,b)=>a+b.amount,0))}
}

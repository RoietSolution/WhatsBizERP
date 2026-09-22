import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CommercePayment, PaymentsApiService } from './payments-api.service';

@Component({selector:'app-payment-list',imports:[DatePipe,DecimalPipe,MatButtonModule,PageContainerComponent,PageHeaderComponent],templateUrl:'./payment-list.component.html',styleUrl:'./payments.component.scss',changeDetection:ChangeDetectionStrategy.OnPush})
export class PaymentListComponent implements OnInit {readonly rows=signal<CommercePayment[]>([]);readonly loading=signal(false);readonly error=signal('');constructor(private readonly api:PaymentsApiService){}ngOnInit(){this.load();}load(){this.loading.set(true);this.api.list().subscribe({next:x=>{this.rows.set(x);this.loading.set(false);},error:()=>{this.error.set('Payments could not be loaded.');this.loading.set(false);}});}verify(row:CommercePayment){if(!globalThis.confirm(`Confirm that ₹${row.amount.toFixed(2)} was received for ${row.orderNumber}?`))return;const reference=globalThis.prompt('Bank/UPI reference (optional)');this.api.verify(row.paymentId,reference).subscribe({next:()=>this.load(),error:()=>this.error.set('Payment could not be verified. Refresh and retry.')});}}

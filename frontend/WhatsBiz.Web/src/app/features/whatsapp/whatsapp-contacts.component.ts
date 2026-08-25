import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CustomerApiService } from '../customers/customer-api.service';
import { CustomerDropdown } from '../customers/customer.models';
import { WhatsAppApiService, WhatsAppContact } from './whatsapp-api.service';

@Component({
  selector: 'app-whatsapp-contacts',
  imports: [DatePipe,FormsModule,MatButtonModule,MatFormFieldModule,MatIconModule,MatInputModule,MatPaginatorModule,MatSelectModule,PageContainerComponent,PageHeaderComponent],
  templateUrl: './whatsapp-contacts.component.html',
  styleUrl: './whatsapp-contacts.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhatsAppContactsComponent {
  readonly items=signal<WhatsAppContact[]>([]);readonly total=signal(0);readonly newCount=signal(0);readonly matchedCount=signal(0);readonly convertedCount=signal(0);readonly busy=signal(false);readonly customers=signal<CustomerDropdown[]>([]);
  readonly canLink:boolean;readonly canCreate:boolean;search='';status='';page=1;size=20;linkingContactId='';linkCustomerId='';
  constructor(private readonly api:WhatsAppApiService,customers:CustomerApiService,permissions:PermissionService,private readonly router:Router,private readonly snack:MatSnackBar){
    this.canLink=permissions.has('customer.edit');this.canCreate=this.canLink&&permissions.has('customer.create');if(this.canLink)customers.dropdown().subscribe(x=>this.customers.set(x));this.load();
  }
  load(){this.busy.set(true);this.api.contacts({search:this.search||undefined,status:this.status||undefined,pageNumber:this.page,pageSize:this.size}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.items.set(x.items);this.total.set(x.totalCount);this.newCount.set(x.newCount);this.matchedCount.set(x.matchedCount);this.convertedCount.set(x.convertedCount);},error:()=>this.snack.open('WhatsApp contacts could not be loaded. Apply database V18 and verify feature access.','Dismiss',{duration:5000})});}
  filter(){this.page=1;this.load();}
  paged(event:PageEvent){this.page=event.pageIndex+1;this.size=event.pageSize;this.load();}
  choose(contact:WhatsAppContact){this.linkingContactId=contact.whatsAppContactId;this.linkCustomerId=contact.customerId??'';}
  cancel(){this.linkingContactId='';this.linkCustomerId='';}
  link(){if(!this.linkingContactId||!this.linkCustomerId)return;this.api.linkContact(this.linkingContactId,this.linkCustomerId).subscribe({next:()=>{this.snack.open('WhatsApp contact linked to the ERP customer.',undefined,{duration:3000});this.cancel();this.load();},error:()=>this.snack.open('Contact could not be linked. Confirm the customer belongs to this retailer.','Dismiss',{duration:5000})});}
  create(contact:WhatsAppContact){const digits=contact.mobile.replace(/\D/g,'');void this.router.navigate(['/customers/new'],{queryParams:{whatsappContactId:contact.whatsAppContactId,name:contact.profileName??'',mobile:digits.slice(-10),code:`WA-${digits.slice(-10)}`}});}
  chat(contact:WhatsAppContact){window.open(`https://wa.me/${contact.mobile.replace(/\D/g,'')}`,'_blank','noopener,noreferrer');}
}

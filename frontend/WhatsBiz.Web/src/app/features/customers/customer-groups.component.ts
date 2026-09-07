import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CustomerGroupApiService } from './customer-group-api.service';
import { CustomerGroup } from './customer-group.models';

@Component({selector:'app-customer-groups',imports:[FormsModule,RouterLink,MatButtonModule,MatFormFieldModule,MatInputModule,MatIconModule,PageContainerComponent,PageHeaderComponent],templateUrl:'./customer-groups.component.html',styleUrls:['./customer-groups.component.scss'],changeDetection:ChangeDetectionStrategy.OnPush})
export class CustomerGroupsComponent {
 groups=signal<CustomerGroup[]>([]); code=''; name=''; editing: string|null=null; submitted=false;
 constructor(private api:CustomerGroupApiService,private snack:MatSnackBar){this.load();}
 load(){this.api.list().subscribe({next:x=>this.groups.set(x),error:e=>this.error(e,'Unable to load customer groups.')});}
 save(){this.submitted=true;if(!this.code.trim()||!this.name.trim())return;const input={groupCode:this.code.trim(),groupName:this.name.trim(),isActive:true};const request=this.editing?this.api.update(this.editing,input):this.api.create(input);request.subscribe({next:x=>{this.groups.update(xs=>this.editing?xs.map(g=>g.customerGroupId===x.customerGroupId?x:g):[...xs,x].sort((a,b)=>a.groupName.localeCompare(b.groupName)));this.reset();this.snack.open(this.editing?'Customer group updated.':'Customer group created.','Close',{duration:2500});},error:e=>this.error(e,'Unable to save customer group.')});}
 edit(g:CustomerGroup){this.editing=g.customerGroupId;this.code=g.groupCode;this.name=g.groupName;this.submitted=false;} cancel(){this.reset();}
 remove(g:CustomerGroup){if(!confirm(`Delete customer group "${g.groupName}"?`))return;this.api.delete(g.customerGroupId).subscribe({next:()=>{this.groups.update(xs=>xs.filter(x=>x.customerGroupId!==g.customerGroupId));this.snack.open('Customer group deleted.','Close',{duration:2500});},error:e=>this.error(e,'Unable to delete customer group.')});}
 private reset(){this.code='';this.name='';this.editing=null;this.submitted=false;} private error(e:any,fallback:string){this.snack.open(e?.error?.detail||e?.error?.title||fallback,'Close',{duration:5000});}
}

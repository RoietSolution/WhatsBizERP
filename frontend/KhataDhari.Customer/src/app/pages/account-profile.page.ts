import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { StorefrontNotificationService } from '../storefront-notification.service';

const MAX_PROFILE_IMAGE_BYTES = 5 * 1024 * 1024;

@Component({selector:'shop-account-profile-page',standalone:true,templateUrl:'./account-profile.page.html',styleUrl:'./account-profile.page.css'})
export class AccountProfilePage implements OnInit {
  storeKey=''; name=''; email=''; readonly saving=signal(false); readonly photoBusy=signal(false); readonly message=signal('');
  constructor(private readonly route:ActivatedRoute,readonly session:CustomerSessionService,private readonly data:StorefrontDataService,private readonly notify:StorefrontNotificationService){}
  ngOnInit():void{this.storeKey=this.route.parent?.parent?.snapshot.paramMap.get('storeKey')??'';this.session.restore(this.storeKey);const c=this.session.customer();this.name=c?.name??'';this.email=c?.email??'';}
  initials(name:string):string{return name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join('').toUpperCase();}
  async save():Promise<void>{this.saving.set(true);try{const c=await this.session.update(this.storeKey,this.name,this.email);this.name=c.name;this.email=c.email??'';this.message.set('Account updated.');this.notify.show('Account updated.');}catch{this.message.set('Account could not be updated.');this.notify.show('Account could not be updated.');}finally{this.saving.set(false);}}
  async photo(event:Event):Promise<void>{const input=event.target as HTMLInputElement;const file=input.files?.item(0);if(!file)return;if(file.size>MAX_PROFILE_IMAGE_BYTES||!['image/jpeg','image/png','image/webp'].includes(file.type)){this.message.set('Choose a JPG, PNG or WebP image up to 5 MB.');input.value='';return;}this.photoBusy.set(true);try{await this.data.uploadProfileImage(this.storeKey,file);await this.session.refresh(this.storeKey);this.message.set('Profile photo updated.');this.notify.show('Profile photo updated.');}catch{this.message.set('Profile photo could not be uploaded.');this.notify.show('Profile photo could not be uploaded.');}finally{this.photoBusy.set(false);}}
  async removePhoto():Promise<void>{this.photoBusy.set(true);try{await this.data.removeProfileImage(this.storeKey);await this.session.refresh(this.storeKey);this.message.set('Profile photo removed.');this.notify.show('Profile photo removed.');}catch{this.message.set('Profile photo could not be removed.');this.notify.show('Profile photo could not be removed.');}finally{this.photoBusy.set(false);}}
}

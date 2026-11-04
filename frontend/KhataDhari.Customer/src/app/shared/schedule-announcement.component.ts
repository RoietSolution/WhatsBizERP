import { Component, computed, effect, input, output } from '@angular/core';
import { AvailabilityAnnouncement } from '../models/storefront.models';
import { StorefrontCountdownService } from '../storefront-countdown.service';

@Component({
  selector:'shop-schedule-announcement', standalone:true,
  template:`<article class="announcement" [class.open]="isOpen()" [attr.aria-label]="item().scheduleName">
    <div class="announcement-copy"><span class="badge"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>{{isOpen()?'Orders Open':'Ordering Closed'}}</span><h2>{{item().scheduleName}}</h2></div>
    @if(!isOpen()){
    <div class="announcement-time">
      @if(parts(); as tiles){<span class="timer-heading">Orders Open In</span><div class="tiles" role="timer" aria-live="off" aria-label="Time until ordering opens">@for(tile of tiles;track tile.label){<span class="tile"><b>{{tile.value}}</b><small>{{tile.label}}</small></span>}</div>}
      @if(nextLabel()){<p class="next-opening">Next opening: {{nextLabel()}}</p>}
      @else{<p class="next-opening">Check back for the next booking window.</p>}
      @if(remaining()===0){<small role="status">Refreshing ordering availability…</small>}
    </div>
    }
  </article>`,
  styleUrl:'./schedule-announcement.component.css'
})
export class ScheduleAnnouncementComponent {
  readonly item=input.required<AvailabilityAnnouncement>();
  readonly boundary=output<void>();
  readonly isOpen=computed(()=>this.item().status.startsWith('ORDERING_OPEN'));
  readonly timestamp=computed(()=>this.isOpen()?this.item().nextOrderCloseAtUtc:this.item().nextOrderOpenAtUtc);
  readonly remaining=computed(()=>this.clock.remaining(this.timestamp()??undefined,this.item().serverNowUtc??undefined,this.item().clientReceivedAtUtc));
  readonly nextLabel=computed(()=>this.clock.openingLabel(this.timestamp()??undefined,this.item().timeZoneId??undefined,this.item().serverNowUtc??undefined,this.item().clientReceivedAtUtc));
  readonly parts=computed(()=>{
    const remaining=this.remaining();if(remaining===null)return null;
    const total=Math.ceil(remaining/1000);
    const tiles=[{label:'Hours',value:String(Math.floor(total%86400/3600)).padStart(2,'0')},{label:'Minutes',value:String(Math.floor(total%3600/60)).padStart(2,'0')},{label:'Seconds',value:String(total%60).padStart(2,'0')}];
    if(total>=86400)tiles.unshift({label:'Days',value:String(Math.floor(total/86400))});
    return tiles;
  });
  private refreshedBoundary='';
  private readonly watchBoundary=effect(()=>{
    const key=this.item().scheduleId+':'+this.timestamp();
    if(this.remaining()===0&&this.refreshedBoundary!==key){this.refreshedBoundary=key;this.boundary.emit();}
  });
  constructor(readonly clock:StorefrontCountdownService){}
}

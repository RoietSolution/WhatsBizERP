export interface PopupCampaign {campaignId:string;headline:string;htmlContent:string;frequency:'VISIT'|'DAY'|'CAMPAIGN';priority:number;imageUrl?:string;ctaText?:string;destination?:string;localDay:string;endsAtUtc:string;}
export interface CampaignFeed {campaigns:PopupCampaign[];serverNowUtc:string;refreshAtUtc:string;}
export function impressionKey(store:string,campaign:PopupCampaign):string{return `khata.campaign.${encodeURIComponent(store.toLowerCase())}.${campaign.campaignId}.${campaign.frequency}${campaign.frequency==='DAY'?'.'+campaign.localDay:''}`;}
export function nextCampaign(items:PopupCampaign[],seen:(item:PopupCampaign)=>boolean):PopupCampaign|null{return [...items].sort((a,b)=>b.priority-a.priority||a.campaignId.localeCompare(b.campaignId)).find(item=>!seen(item))??null;}

import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface WhatsAppConfiguration {
  providerMode: 'MOCK' | 'META_TEST' | 'LIVE';
  metaAppId?: string;
  whatsAppBusinessAccountId?: string;
  phoneNumberId?: string;
  displayPhoneNumber?: string;
  businessDisplayName?: string;
  apiVersion?: string;
  testRecipientNumber?: string;
  isEnabled: boolean;
  connectionStatus: string;
  lastValidatedDate?: string;
  lastError?: string;
  hasAccessToken: boolean;
  hasWebhookVerifyToken: boolean;
  hasAppSecret: boolean;
  usesSharedPlatformCredentials: boolean;
}
export interface WhatsAppPlatformConfiguration { metaAppId?:string; isEnabled:boolean; hasAppSecret:boolean; hasWebhookVerifyToken:boolean; modifiedOn?:string; }
export interface RetailerWhatsAppConnection { tenantId:string;tenantKey:string;tenantName:string;tenantIsActive:boolean;providerMode?:string;wabaId?:string;phoneNumberId?:string;displayPhoneNumber?:string;businessDisplayName?:string;configurationIsEnabled:boolean;connectionStatus:string;lastValidatedOn?:string;usesSharedPlatformCredentials:boolean; }
export interface SaveWhatsAppConfiguration {
  providerMode: 'MOCK' | 'META_TEST' | 'LIVE';
  metaAppId: string;
  whatsAppBusinessAccountId: string;
  phoneNumberId: string;
  apiVersion: string;
  testRecipientNumber?: string;
  isEnabled: boolean;
  accessToken?: string;
  webhookVerifyToken?: string;
  appSecret?: string;
}
export interface WhatsAppConnectionResult {
  succeeded: boolean;
  connectionStatus: string;
  displayPhoneNumber?: string;
  businessDisplayName?: string;
  validatedAt: string;
  message?: string;
}
export interface WhatsAppTestMessageResult { succeeded:boolean; metaMessageId?:string; attemptedAt:string; message?:string; }
export interface WhatsAppMetaTestDiagnostics {
  webhookPath:string; webhookCallbackUrl?:string; lastWebhookVerifiedOn?:string; lastWebhookReceivedOn?:string;
  lastInboundEventType?:string; lastMetaMessageId?:string; tenantResolutionSucceeded:boolean;
  duplicateWebhookCount:number; lastTestMessageOn?:string; lastTestMessageId?:string;
}
@Injectable({ providedIn: 'root' })
export class WhatsAppApiService {
  private readonly root = '/api/whatsapp';
  constructor(private readonly http: HttpClient) {}
  get() { return this.http.get<WhatsAppConfiguration>(`${this.root}/configuration`); }
  save(input: SaveWhatsAppConfiguration) { return this.http.put<WhatsAppConfiguration>(`${this.root}/configuration`, input); }
  validate(accessToken?: string) { return this.http.post<WhatsAppConnectionResult>(`${this.root}/configuration/validate`, { accessToken: accessToken || null }); }
  sendTestMessage(recipientNumber:string,message?:string) { return this.http.post<WhatsAppTestMessageResult>(`${this.root}/configuration/test-message`, { recipientNumber, message:message||null }); }
  diagnostics() { return this.http.get<WhatsAppMetaTestDiagnostics>(`${this.root}/configuration/diagnostics`); }
  platform(){return this.http.get<WhatsAppPlatformConfiguration>(`${this.root}/administration/platform`);}
  savePlatform(input:{metaAppId:string;isEnabled:boolean;appSecret?:string;webhookVerifyToken?:string}){return this.http.put<WhatsAppPlatformConfiguration>(`${this.root}/administration/platform`,input);}
  retailerConnections(){return this.http.get<RetailerWhatsAppConnection[]>(`${this.root}/administration/retailer-connections`);}
}

import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface WhatsAppConfiguration {
  providerMode: 'MOCK' | 'META_TEST' | 'LIVE';
  whatsAppBusinessAccountId?: string;
  phoneNumberId?: string;
  displayPhoneNumber?: string;
  businessDisplayName?: string;
  apiVersion?: string;
  isEnabled: boolean;
  connectionStatus: string;
  lastValidatedDate?: string;
  lastError?: string;
  hasAccessToken: boolean;
  hasWebhookVerifyToken: boolean;
  hasAppSecret: boolean;
}
export interface SaveWhatsAppConfiguration {
  providerMode: 'MOCK' | 'META_TEST' | 'LIVE';
  whatsAppBusinessAccountId: string;
  phoneNumberId: string;
  apiVersion: string;
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
@Injectable({ providedIn: 'root' })
export class WhatsAppApiService {
  private readonly root = '/api/whatsapp';
  constructor(private readonly http: HttpClient) {}
  get() { return this.http.get<WhatsAppConfiguration>(`${this.root}/configuration`); }
  save(input: SaveWhatsAppConfiguration) { return this.http.put<WhatsAppConfiguration>(`${this.root}/configuration`, input); }
  validate(accessToken?: string) { return this.http.post<WhatsAppConnectionResult>(`${this.root}/configuration/validate`, { accessToken: accessToken || null }); }
}

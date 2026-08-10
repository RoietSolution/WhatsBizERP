import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { PaperSize } from './paper-size';
export interface PrintTemplate {
  id: string;
  code: string;
  name: string;
  documentType: string;
  paperType: string;
  isDefault: boolean;
  content: string;
}
export interface Printer {
  id: string;
  printerName: string;
  displayName: string;
  printerType: string;
  paperSize: string;
  documentType?: string;
  isDefault: boolean;
  autoCut: boolean;
  isActive: boolean;
}
export interface PrintingSettings {
  paperSize: PaperSize;
  supportedPaperSizes: PaperSize[];
}
@Injectable({ providedIn: 'root' })
export class PrintApiService {
  private root = '/api/print';
  constructor(private http: HttpClient) {}
  templates(type?: string) {
    return this.http.get<PrintTemplate[]>(`${this.root}/template`, {
      params: type ? new HttpParams().set('documentType', type) : undefined,
    });
  }
  printers() {
    return this.http.get<Printer[]>(`${this.root}/printers`);
  }
  settings() {
    return this.http.get<PrintingSettings>(`${this.root}/settings`);
  }
  savePrinter(x: Partial<Printer>) {
    return this.http.post<void>(`${this.root}/printers`, x);
  }
  barcode(x: object) {
    return this.http.post(`${this.root}/barcode`, x, { responseType: 'blob' });
  }
  qrcode(x: object) {
    return this.http.post(`${this.root}/qrcode`, x, { responseType: 'blob' });
  }
  document(x: object) {
    return this.http.post(`${this.root}/document`, x, { responseType: 'blob' });
  }
  label(x: object) {
    return this.http.post(`${this.root}/label`, x, { responseType: 'blob' });
  }
}

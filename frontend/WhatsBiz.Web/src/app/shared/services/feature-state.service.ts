import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface FeatureState {
  whatsAppEnabled: boolean;
  smsEnabled: boolean;
}

const disabled: FeatureState = { whatsAppEnabled: false, smsEnabled: false };

@Injectable({ providedIn: 'root' })
export class FeatureStateService {
  readonly state = signal<FeatureState>(disabled);
  readonly loaded = signal(false);

  constructor(private readonly http: HttpClient) {}

  load(): Observable<FeatureState> {
    return this.http.get<FeatureState>('/api/config/features').pipe(
      tap((state) => {
        this.state.set(state);
        this.loaded.set(true);
      }),
    );
  }
}

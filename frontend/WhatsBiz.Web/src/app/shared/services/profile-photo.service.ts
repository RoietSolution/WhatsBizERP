import { Injectable, signal } from '@angular/core';

const storageKey = 'khatadhari.profile.photo';

@Injectable({ providedIn: 'root' })
export class ProfilePhotoService {
  readonly photo = signal<string | null>(localStorage.getItem(storageKey));

  set(value: string): void {
    localStorage.setItem(storageKey, value);
    this.photo.set(value);
  }
}

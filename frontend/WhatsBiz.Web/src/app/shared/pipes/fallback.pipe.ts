import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'fallback' })
export class FallbackPipe implements PipeTransform {
  transform(value: unknown, fallback = '—'): unknown { return value === null || value === undefined || value === '' ? fallback : value; }
}

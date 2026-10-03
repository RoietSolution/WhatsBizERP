import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PwaStatusComponent } from './shared/components/pwa-status/pwa-status.component';
@Component({
  selector: 'app-root',
  imports: [PwaStatusComponent, RouterOutlet],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}

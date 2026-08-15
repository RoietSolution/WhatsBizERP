import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-authentication-shell',
  imports: [MatButtonModule, MatMenuModule, RouterOutlet],
  templateUrl: './authentication-shell.component.html',
  styleUrl: './authentication-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthenticationShellComponent {}

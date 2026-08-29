import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-authentication-shell',
  imports: [RouterOutlet],
  templateUrl: './authentication-shell.component.html',
  styleUrl: './authentication-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthenticationShellComponent {}

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

@Component({ selector: 'app-confirm-dialog', imports: [MatDialogModule, MatButtonModule], template: '<h2 mat-dialog-title>{{ data.title }}</h2><mat-dialog-content>{{ data.message }}</mat-dialog-content><mat-dialog-actions align="end"><button mat-button [mat-dialog-close]="false">Cancel</button><button mat-flat-button color="warn" (click)="close()">Confirm</button></mat-dialog-actions>', changeDetection: ChangeDetectionStrategy.OnPush })
export class ConfirmDialogComponent { readonly data = inject<{ title: string; message: string }>(MAT_DIALOG_DATA); private readonly ref = inject(MatDialogRef<ConfirmDialogComponent>); close(): void { this.ref.close(true); } }

import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { AdminApiService, Branch } from './admin-api.service';
@Component({
  imports: [FormsModule, MatButtonModule],
  templateUrl: './branch-management.component.html',
  styles: [
    `
      form {
        display: flex;
        gap: 0.7rem;
        flex-wrap: wrap;
        background: #fff;
        padding: 1rem;
      }
      input {
        padding: 0.6rem;
      }
      section {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        margin-top: 1rem;
      }
      article {
        background: #fff;
        padding: 1rem;
        border: 1px solid #ddd;
      }
      @media (max-width: 700px) {
        section {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class BranchManagementComponent {
  branches = signal<Branch[]>([]);
  model = { branchCode: '', branchName: '', phone: '', city: '', isDefault: false, isActive: true };
  constructor(private api: AdminApiService) {
    this.load();
  }
  load() {
    this.api.branches().subscribe((x) => this.branches.set(x));
  }
  save() {
    this.api.addBranch(this.model).subscribe(() => {
      this.model = {
        branchCode: '',
        branchName: '',
        phone: '',
        city: '',
        isDefault: false,
        isActive: true,
      };
      this.load();
    });
  }
}

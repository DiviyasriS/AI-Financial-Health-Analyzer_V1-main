import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TransactionService, UploadResult } from '../../services/transaction.service';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './upload.component.html'
})
export class UploadComponent {

  selectedFile: File | null = null;
  uploading   = false;
  result: UploadResult | null = null;
  error       = '';

  constructor(
    private transactionService: TransactionService,
    private router: Router
  ) {}

  // Called when user picks a file
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
      this.result       = null;
      this.error        = '';
    }
  }

  // Called when user clicks Upload
  onUpload(): void {
    if (!this.selectedFile) {
      this.error = 'Please select a file first.';
      return;
    }

    this.uploading = true;
    this.error     = '';
    this.result    = null;

    this.transactionService.uploadFile(this.selectedFile)
      .subscribe({
        next: (res) => {
          this.uploading = false;
          this.result    = res;
          this.selectedFile = null;
        },
        error: (err) => {
          this.uploading = false;
          this.error = err.error?.message
            || 'Upload failed. Please try again.';
        }
      });
  }

  goToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }
}
import { Component, ChangeDetectorRef } from '@angular/core';
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
    private router: Router,
    private cdr: ChangeDetectorRef   // ← forces UI to update
  ) {}

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
      this.result       = null;
      this.error        = '';
      this.cdr.detectChanges();
    }
  }

  onUpload(): void {
    if (!this.selectedFile) {
      this.error = 'Please select a file first.';
      return;
    }

    this.uploading = true;
    this.error     = '';
    this.result    = null;
    this.cdr.detectChanges();

    this.transactionService.uploadFile(this.selectedFile)
      .subscribe({
        next: (res) => {
          console.log('Upload success:', res);
          this.uploading    = false;
          this.result       = res;
          this.selectedFile = null;
          this.cdr.detectChanges();  // ← force UI refresh
        },
        error: (err) => {
          console.error('Upload error:', err);
          this.uploading = false;

          if (err.status === 0) {
            this.error = 'Cannot reach the server. Make sure backend is running on port 5257.';
          } else if (err.status === 401) {
            this.error = 'Session expired. Please log out and log in again.';
          } else {
            this.error = err.error?.message || `Upload failed (${err.status}).`;
          }
          this.cdr.detectChanges();  // ← force UI refresh on error too
        }
      });
  }

  goToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }
}
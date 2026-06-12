import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TransactionService, UploadResult, DeleteResult } from '../../services/transaction.service';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './upload.component.html',
  styleUrls: ['./upload.css']
})
export class UploadComponent {

  // ── Upload state ────────────────────────────────────────────────────────────
  selectedFile: File | null = null;
  uploading = false;
  result: UploadResult | null = null;
  error = '';

  // ── Delete state ────────────────────────────────────────────────────────────
  // confirmingDelete: user has clicked "Clear All Data" and sees the warning prompt
  // deleting: DELETE request is in flight
  // deleteResult: set after a successful delete to show feedback
  confirmingDelete = false;
  deleting = false;
  deleteResult: DeleteResult | null = null;

  constructor(
    private transactionService: TransactionService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  // ── Upload ──────────────────────────────────────────────────────────────────

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
      this.result       = null;
      this.error        = '';
      this.deleteResult = null;
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

    this.transactionService.uploadFile(this.selectedFile).subscribe({
      next: (res) => {
        this.uploading    = false;
        this.result       = res;
        this.selectedFile = null;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.uploading = false;

        if (err.status === 0) {
          this.error = 'Cannot reach the server. Make sure the backend is running on port 5257.';
        } else if (err.status === 401) {
          this.error = 'Session expired. Please log out and log in again.';
        } else {
          this.error = err.error?.message || `Upload failed (${err.status}).`;
        }
        this.cdr.detectChanges();
      }
    });
  }

  // ── Delete (clear all data) ─────────────────────────────────────────────────

  /** Step 1: user clicks "Clear All Data" — show confirmation prompt */
  onRequestDelete(): void {
    this.confirmingDelete = true;
    this.error            = '';
    this.result           = null;
    this.deleteResult     = null;
    this.cdr.detectChanges();
  }

  /** Step 2: user clicks "Cancel" in the confirmation prompt */
  onCancelDelete(): void {
    this.confirmingDelete = false;
    this.cdr.detectChanges();
  }

  /** Step 3: user clicks "Yes, Delete Everything" — fire the DELETE request */
  onConfirmDelete(): void {
    this.confirmingDelete = false;
    this.deleting         = true;
    this.error            = '';
    this.cdr.detectChanges();

    this.transactionService.deleteAllTransactions().subscribe({
      next: (res) => {
        this.deleting     = false;
        this.deleteResult = res;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.deleting = false;

        if (err.status === 400) {
          // No transactions to delete — treat as info, not a hard error
          this.error = 'No transactions found to delete.';
        } else if (err.status === 401) {
          this.error = 'Session expired. Please log out and log in again.';
        } else {
          this.error = err.error?.message || `Delete failed (${err.status}).`;
        }
        this.cdr.detectChanges();
      }
    });
  }

  // ── Navigation ──────────────────────────────────────────────────────────────

  goToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }
}
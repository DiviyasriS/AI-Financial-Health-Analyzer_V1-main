import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { ChangeDetectorRef } from '@angular/core';
import { UploadComponent } from './upload.component';
import { TransactionService } from '../../services/transaction.service';

describe('UploadComponent', () => {
  let component: UploadComponent;
  let fixture: ComponentFixture<UploadComponent>;

  let uploadFileCalled = false;
  let uploadedFile: File | null = null;
  let navigateCalledWith: any[] | null = null;

  const transactionServiceMock = {
    uploadFile: (file: File) => {
      uploadFileCalled = true;
      uploadedFile = file;
      return of({});
    }
  };

  const routerMock = {
    navigate: (commands: any[]) => {
      navigateCalledWith = commands;
      return Promise.resolve(true);
    }
  };

  beforeEach(async () => {
    uploadFileCalled = false;
    uploadedFile = null;
    navigateCalledWith = null;

    await TestBed.configureTestingModule({
      imports: [UploadComponent],
      providers: [
        { provide: TransactionService, useValue: transactionServiceMock },
        { provide: Router, useValue: routerMock },
        ChangeDetectorRef
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(UploadComponent);
    component = fixture.componentInstance;
  });

  it('should select file', () => {
    const file = new File(['sample'], 'transactions.csv', { type: 'text/csv' });

    const event = {
      target: {
        files: [file]
      }
    } as unknown as Event;

    component.onFileSelected(event);

    expect(component.selectedFile).toBe(file);
    expect(component.error).toBe('');
    expect(component.result).toBeNull();
  });

  it('should show error when upload is clicked without file', () => {
    component.onUpload();

    expect(component.error).toBe('Please select a file first.');
    expect(uploadFileCalled).toBe(false);
  });

  it('should upload selected file successfully', () => {
    const file = new File(['sample'], 'transactions.csv', { type: 'text/csv' });

    transactionServiceMock.uploadFile = (uploaded: File) => {
      uploadFileCalled = true;
      uploadedFile = uploaded;

      return of({
        savedCount: 2,
        duplicateCount: 0,
        skippedCount: 0,
        totalRowsFound: 2,
        message: 'Uploaded',
        fileType: 'CSV',
        monthWarning: null
      });
    };

    component.selectedFile = file;
    component.onUpload();

    expect(uploadFileCalled).toBe(true);
    expect(uploadedFile).toBe(file);
    expect(component.uploading).toBe(false);
    expect(component.result?.savedCount).toBe(2);
    expect(component.selectedFile).toBeNull();
  });

  it('should show server connection error', () => {
    const file = new File(['sample'], 'transactions.csv');

    transactionServiceMock.uploadFile = (uploaded: File) => {
      uploadFileCalled = true;
      uploadedFile = uploaded;

      return throwError(() => ({
        status: 0
      }));
    };

    component.selectedFile = file;
    component.onUpload();

    expect(uploadFileCalled).toBe(true);
    expect(uploadedFile).toBe(file);
    expect(component.uploading).toBe(false);
    expect(component.error).toContain('Cannot reach the server');
  });

  it('should navigate to dashboard', () => {
    component.goToDashboard();

    expect(navigateCalledWith).toEqual(['/dashboard']);
  });
});
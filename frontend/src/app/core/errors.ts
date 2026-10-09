import { HttpErrorResponse } from '@angular/common/http';

/** Extracts the user-facing message from an API ProblemDetails response (or any error). */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'Cannot reach the Chitra API. Make sure the backend is running (start-dev script) on http://localhost:5080.';
    }
    const body = error.error;
    if (body && typeof body === 'object') {
      return body.detail ?? body.title ?? error.message;
    }
    if (typeof body === 'string' && body.length < 300) return body;
    return `${error.status} ${error.statusText}`;
  }
  if (error instanceof Error) return error.message;
  return String(error);
}

export function errorCode(error: unknown): string | null {
  return error instanceof HttpErrorResponse && error.error && typeof error.error === 'object' ? (error.error.code ?? null) : null;
}

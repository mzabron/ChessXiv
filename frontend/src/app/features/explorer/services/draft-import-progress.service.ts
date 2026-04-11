import { Injectable, NgZone, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { BehaviorSubject, Observable } from 'rxjs';
import { AuthStateService } from '../../../core/auth/auth-state.service';

export interface DraftImportProgressUpdate {
  parsedCount: number;
  importedCount: number;
  skippedCount: number;
  isCompleted: boolean;
  isFailed: boolean;
  message?: string | null;
}

@Injectable({ providedIn: 'root' })
export class DraftImportProgressService {
  private readonly authState = inject(AuthStateService);
  private readonly ngZone = inject(NgZone);
  private readonly updatesSubject = new BehaviorSubject<DraftImportProgressUpdate | null>(null);
  private connection: HubConnection | null = null;

  readonly updates$: Observable<DraftImportProgressUpdate | null> = this.updatesSubject.asObservable();

  async connect(): Promise<void> {
    if (!this.authState.isAuthenticated()) {
      return;
    }

    if (this.connection?.state === HubConnectionState.Connected || this.connection?.state === HubConnectionState.Connecting) {
      return;
    }

    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl('/hubs/import-progress', {
          withCredentials: false,
          accessTokenFactory: () => this.authState.getAccessToken() ?? ''
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      this.connection.on('draftImportProgress', (update: DraftImportProgressUpdate) => {
        this.ngZone.run(() => this.updatesSubject.next(update));
      });
    }

    await this.connection.start();
  }

  async disconnect(): Promise<void> {
    if (!this.connection || this.connection.state === HubConnectionState.Disconnected) {
      return;
    }

    await this.connection.stop();
  }

  reset(): void {
    this.updatesSubject.next(null);
  }
}
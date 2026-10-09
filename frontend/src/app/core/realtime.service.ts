import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Generation, ModelDownloadStatus, VideoExport } from './models';

/** SignalR connection that pushes generation, export and model-download progress. Reconnects automatically. */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  readonly connected = signal(false);
  readonly generationUpdated = new Subject<Generation>();
  readonly exportUpdated = new Subject<VideoExport>();
  readonly modelDownloadUpdated = new Subject<ModelDownloadStatus>();

  private connection?: HubConnection;

  start(): void {
    if (this.connection) return;
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/studio')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('generationUpdated', (g: Generation) => this.generationUpdated.next(g));
    this.connection.on('exportUpdated', (e: VideoExport) => this.exportUpdated.next(e));
    this.connection.on('modelDownloadUpdated', (s: ModelDownloadStatus) => this.modelDownloadUpdated.next(s));
    this.connection.onreconnecting(() => this.connected.set(false));
    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => {
      this.connected.set(false);
      setTimeout(() => this.connect(), 5000);
    });
    void this.connect();
  }

  private async connect(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Disconnected) return;
    try {
      await this.connection.start();
      this.connected.set(true);
    } catch {
      this.connected.set(false);
      setTimeout(() => this.connect(), 5000);
    }
  }
}

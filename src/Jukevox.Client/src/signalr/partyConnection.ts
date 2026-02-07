import * as signalR from '@microsoft/signalr';
import type { PlaybackState, QueueItem } from '../types';

export type PartyCallbacks = {
  onNowPlayingChanged: (state: PlaybackState | null) => void;
  onPlaybackStateUpdated: (state: PlaybackState) => void;
  onQueueUpdated: (queue: QueueItem[]) => void;
  onCreditsUpdated: (credits: number) => void;
};

let connection: signalR.HubConnection | null = null;

export function createPartyConnection(partyId: string, callbacks: PartyCallbacks): signalR.HubConnection {
  if (connection) {
    connection.stop();
  }

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`/hubs/party?partyId=${encodeURIComponent(partyId)}`)
    .withAutomaticReconnect()
    .build();

  connection.on('NowPlayingChanged', callbacks.onNowPlayingChanged);
  connection.on('PlaybackStateUpdated', callbacks.onPlaybackStateUpdated);
  connection.on('QueueUpdated', callbacks.onQueueUpdated);
  connection.on('CreditsUpdated', callbacks.onCreditsUpdated);

  return connection;
}

export function getConnection(): signalR.HubConnection | null {
  return connection;
}

export async function startConnection(): Promise<void> {
  if (connection && connection.state === signalR.HubConnectionState.Disconnected) {
    await connection.start();
  }
}

export async function stopConnection(): Promise<void> {
  if (connection) {
    await connection.stop();
    connection = null;
  }
}

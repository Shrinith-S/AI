import { Pipe, PipeTransform } from '@angular/core';
import { GenerationStatus, GenerationType } from '../core/models';

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(value >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
}

export function timeAgo(iso: string): string {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return 'just now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return new Date(iso).toLocaleDateString();
}

export const typeLabel: Record<GenerationType, string> = {
  TextToImage: 'Image',
  TextToVideo: 'Text → Video',
  ImageToVideo: 'Image → Video',
};

export const statusClass: Record<GenerationStatus, string> = {
  Queued: 'bg-sky-500/15 text-sky-400',
  Processing: 'bg-amber-500/15 text-amber-400',
  Completed: 'bg-emerald-500/15 text-emerald-400',
  Failed: 'bg-red-500/15 text-red-400',
  Cancelled: 'bg-zinc-500/15 text-zinc-400',
};

@Pipe({ name: 'bytes' })
export class BytesPipe implements PipeTransform {
  transform = formatBytes;
}

@Pipe({ name: 'ago' })
export class AgoPipe implements PipeTransform {
  transform = timeAgo;
}

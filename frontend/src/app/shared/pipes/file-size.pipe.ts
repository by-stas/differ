import { Pipe, PipeTransform } from '@angular/core';

const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];

/** Formats a byte count for display, e.g. `1.2 MB`. Nullish sizes render as an em dash. */
@Pipe({ name: 'fileSize' })
export class FileSizePipe implements PipeTransform {
  transform(bytes: number | null | undefined): string {
    if (bytes === null || bytes === undefined) {
      return '—';
    }

    if (bytes < 1024) {
      return `${bytes} B`;
    }

    let value = bytes;
    let unit = 0;

    while (value >= 1024 && unit < UNITS.length - 1) {
      value /= 1024;
      unit++;
    }

    return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${UNITS[unit]}`;
  }
}

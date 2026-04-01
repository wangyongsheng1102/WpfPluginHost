import type { PdfProcessTableRow } from '../../api';

/** 工程表 1 ページあたりの最大プロセス列（行）数（A4 横想定） */
export const MAX_PROCESS_KEYS_PER_PAGE = 8;

export function sortProcessKeys(a: string, b: string): number {
  const [t1, p1] = a.split('-').map(Number);
  const [t2, p2] = b.split('-').map(Number);
  if (Number.isNaN(t1) || Number.isNaN(p1)) return String(a).localeCompare(String(b));
  if (Number.isNaN(t2) || Number.isNaN(p2)) return String(a).localeCompare(String(b));
  return t1 !== t2 ? t1 - t2 : p1 - p2;
}

export function getProcessKeysFromTable(rows: PdfProcessTableRow[]): string[] {
  if (!rows.length) return [];
  return Array.from(new Set(rows.flatMap((r) => Object.keys(r.values || {})))).sort(sortProcessKeys);
}

export function getProcessTablePageCount(keys: string[]): number {
  if (keys.length === 0) return 1;
  return Math.ceil(keys.length / MAX_PROCESS_KEYS_PER_PAGE);
}

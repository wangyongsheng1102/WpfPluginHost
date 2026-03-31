import type { PdfPartsListItem } from '../../api';

/** 部品表 1 ページあたりの最大行数（A4 横・ヘッダー・フッター考慮） */
export const MAX_PARTS_ROWS_PER_PAGE = 15;

export function getPartsListPageCount(partsList: PdfPartsListItem[]): number {
  if (partsList.length === 0) return 1;
  return Math.ceil(partsList.length / MAX_PARTS_ROWS_PER_PAGE);
}

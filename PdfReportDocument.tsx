'use client';

import React from 'react';
import { Document, Page, Font } from '@react-pdf/renderer';
import { styles } from './pdf/styles';
import { CoverPage } from './pdf/CoverPage';
import { VtsDiagramPage } from './pdf/VtsDiagramPage';
import { PartsListPage } from './pdf/PartsListPage';
import { CalculationResultPage } from './pdf/CalculationResultPage';
import { InputConditionsPage } from './pdf/InputConditionsPage';
import { ProcessTablePage } from './pdf/ProcessTablePage';
import { MAX_PARTS_ROWS_PER_PAGE, getPartsListPageCount } from './pdf/partsListConstants';
import type { PdfReportContext } from '../api';

// 日本語フォントを登録（public/fonts/NotoSansJP-Regular.ttf を配置してください）
Font.register({
  family: 'NotoSansJP',
  src: '/fonts/NotoSansJP-Regular.otf',
});

interface PdfReportDocumentProps {
  context: PdfReportContext;
}

export const PdfReportDocument: React.FC<PdfReportDocumentProps> = ({ context }) => {
  const partsList = context.parts_list ?? [];
  const partsPageCount = getPartsListPageCount(partsList);
  const totalPages = 5 + partsPageCount;

  return (
    <Document>
      <Page size="A4" orientation="landscape" style={styles.page}>
        <CoverPage context={context} pageNum={1} totalPages={totalPages} />
      </Page>
      <Page size="A4" orientation="landscape" style={styles.page}>
        <VtsDiagramPage context={context} pageNum={2} totalPages={totalPages} />
      </Page>
      {Array.from({ length: partsPageCount }, (_, i) => {
        const start = i * MAX_PARTS_ROWS_PER_PAGE;
        const slice =
          partsList.length === 0 ? [] : partsList.slice(start, start + MAX_PARTS_ROWS_PER_PAGE);
        return (
          <Page key={`parts-${i}`} size="A4" orientation="landscape" style={styles.page}>
            <PartsListPage
              context={context}
              rows={slice}
              pageNum={3 + i}
              totalPages={totalPages}
            />
          </Page>
        );
      })}
      <Page size="A4" orientation="landscape" style={styles.page}>
        <CalculationResultPage
          context={context}
          pageNum={3 + partsPageCount}
          totalPages={totalPages}
        />
      </Page>
      <Page size="A4" orientation="landscape" style={styles.page} wrap={false}>
        <InputConditionsPage
          context={context}
          pageNum={4 + partsPageCount}
          totalPages={totalPages}
        />
      </Page>
      <Page size="A4" orientation="landscape" style={styles.page}>
        <ProcessTablePage
          context={context}
          pageNum={5 + partsPageCount}
          totalPages={totalPages}
        />
      </Page>
    </Document>
  );
};

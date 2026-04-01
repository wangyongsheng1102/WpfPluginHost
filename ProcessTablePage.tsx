import React from 'react';
import { View, Text, Svg, Line } from '@react-pdf/renderer';
import { useTranslation } from 'react-i18next';
import { styles } from './styles';
import { PageHeader } from './PageHeader';
import { PageFooter } from './PageFooter';
import type { PdfReportContext } from '../../api';

interface ProcessTablePageProps {
  context: PdfReportContext;
  /** 当ページに表示するプロセスキー（親でスライス済み） */
  processKeys: string[];
  pageNum: number;
  totalPages: number;
  /** 注釈は工程表セクションの最終ページのみ表示 */
  showFootnote?: boolean;
}

export const ProcessTablePage: React.FC<ProcessTablePageProps> = ({
  context,
  processKeys,
  pageNum,
  totalPages,
  showFootnote = true,
}) => {
  const { t } = useTranslation();
  const rows = context.process_table || [];

  // 数値末尾の .0000 を除去する補助関数
  const formatValue = (val: string): string => {
    if (/\.0+$/.test(val)) {
      return val.replace(/\.0+$/, '');
    }
    return val;
  };

  const getValue = (itemLabel: string, key: string): string => {
    const row = rows.find((r) => r.item === itemLabel);
    if (!row || !row.values) return '';
    const v = (row.values as Record<string, any>)[key];
    if (v == null) return '';

    let rawValue = String(v);
    if (itemLabel === 'プロセス中禁止領域[mm]') {
      const match = rawValue.match(/-?\d+(\.\d+)?/);
      rawValue = match ? match[0] : rawValue;
    }
    return formatValue(rawValue);
  };

  const CELL_HEIGHT = 80;

  return (
    <View style={{ flex: 1, flexDirection: 'column' }}>
      <PageHeader />
      <View style={{ flex: 1 }}>
        <Text style={styles.sectionTitle}>{t('MSG_PDF_042')}</Text>

        {/* ヘッダー行 - 上枠線を追加する。 */}
        <View style={[styles.tableHeaderRow, { borderTopWidth: 1, borderTopColor: '#000000' }]}>
          {/* 斜線セル - 左枠線を追加し、斜線は左上から右下へ。 */}
          <View
            style={[
              styles.cellFixed,
              {
                position: 'relative',
                padding: 2,
                height: CELL_HEIGHT,
                borderLeftWidth: 1,
                borderLeftColor: '#000000',
              },
            ]}
          >
            <Svg
              width={120}
              height={CELL_HEIGHT}
              style={{ position: 'absolute', top: 0, left: 0 }}
            >
              <Line
                x1={0}
                y1={0}
                x2={119}
                y2={CELL_HEIGHT}
                stroke="#000000"
                strokeWidth={1}
              />
            </Svg>
            <View style={{ position: 'absolute', top: 2, right: 4 }}>
              <Text style={{ fontSize: 9 }}>{t('MSG_PDF_043')}</Text>
            </View>
            <View style={{ position: 'absolute', bottom: 2, left: 4 }}>
              <Text style={{ fontSize: 9 }}>{t('MSG_PDF_044')}</Text>
            </View>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_045')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_046')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_047')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_048')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center'  }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_049')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_050')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_051')}</Text>
          </View>
          <View style={[styles.cell, { justifyContent: 'center', alignItems: 'center' }]}>
            <Text style={{ textAlign: 'center' }}>{t('MSG_PDF_052')}</Text>
          </View>
        </View>

        {/* データ行 */}
        {processKeys.length === 0 ? (
          <View style={styles.tableRow} wrap={false}>
            <Text style={[styles.cell, { flex: 9, borderLeftWidth: 1, borderLeftColor: '#000000' }]}>
              {t('MSG_PDF_041')}
            </Text>
          </View>
        ) : (
          processKeys.map((key) => (
            <View key={key} style={styles.tableRow} wrap={false}>
              {/* 1 列目に左枠線を追加する。 */}
              <Text style={[styles.cellFixed, { borderLeftWidth: 1, borderLeftColor: '#000000' }]}>
                {key}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('プロセス名称', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('プロセスCT動作時間[s]', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('プロセスCT動作時間公差[s]', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('静定時間[s]', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('プロセス停止位置[mm]', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('プロセス中禁止領域[mm]', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('本プロセスへの移動速度 [mm/s]', key)}
              </Text>
              <Text style={[styles.cell, { textAlign: 'center' }]}>
                {getValue('本プロセスへの移動加減速度 [mm/s^2]', key)}
              </Text>
            </View>
          ))
        )}

        {showFootnote ? (
          <View style={{ marginTop: 8 }}>
            <Text style={{ fontSize: 10 }}>{t('MSG_PDF_053')}</Text>
          </View>
        ) : null}
      </View>
      <PageFooter pageNum={pageNum} totalPages={totalPages} />
    </View>
  );
};
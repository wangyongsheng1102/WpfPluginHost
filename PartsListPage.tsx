import React from 'react';
import { View, Text } from '@react-pdf/renderer';
import { useTranslation } from 'react-i18next';
import { styles } from './styles';
import { PageHeader } from './PageHeader';
import { PageFooter } from './PageFooter';
import type { PdfPartsListItem, PdfReportContext } from '../../api';

interface PartsListPageProps {
  context: PdfReportContext;
  /** 当ページに表示する行（親でスライス済み。全件を複数ページに分割する） */
  rows: PdfPartsListItem[];
  pageNum: number;
  totalPages: number;
}

export const PartsListPage: React.FC<PartsListPageProps> = ({ context, rows, pageNum, totalPages }) => {
  const { t } = useTranslation();
  const inputConditions = context.input_conditions || [];

  // 国际化列名
  const COLS = [
    'No',
    t('MSG_D005_020'), // 種類
    t('MSG_D002_001'), // 名称
    t('MSG_P001_008'), // 型番（形番）
    t('MSG_P001_009'), // 商品コード
    t('MSG_D005_012'), // 数量
  ];

  // 列幅：No と 数量は 40pt の内容幅、他の列は必要に応じて調整
  const COL_CONTENT_WIDTHS = [60, 150, 150, 150, 120, 60];

  // パディング（左右6pt）と右ボーダー（1pt）を含む列の合計幅を計算
  const COL_TOTAL_WIDTHS = COL_CONTENT_WIDTHS.map((w) => {
    const padding = 12; // 左右のパディング
    const borderRight = 1; // すべての列に右ボーダー
    return w + padding + borderRight;
  });

  const TABLE_WIDTH = COL_TOTAL_WIDTHS.reduce((a, b) => a + b, 0);

  // input_conditions から循環軸に関するパラメータを取得する。
  const getConditionValue = (itemName: string): string => {
    const found = inputConditions.find(cond => cond.item === itemName);
    return found ? found.value : '';
  };

  // 補助関数：数値末尾の .0000 を除去する。
  const trimTrailingZeros = (val: string): string => {
    if (val.endsWith('.0000')) {
      return val.slice(0, -5);
    }
    return val;
  };

  const verticalType = getConditionValue('垂直循環タイプ');
  const rawVerticalLead = getConditionValue('垂直循環リード');
  const verticalLead = trimTrailingZeros(rawVerticalLead);
  const rawVerticalStroke = getConditionValue('垂直循環ストローク');
  const verticalStroke = trimTrailingZeros(rawVerticalStroke);
  const motorOrientation = getConditionValue('垂直循環モータ設置向き');
  const housing = getConditionValue('垂直循環ハウジング');
  const sensor = getConditionValue('循環軸センサ');

  // 周回軸の型番（すべてのパラメータが存在する場合のみ。存在しない場合は空文字へフォールバックする）。
  const axisModelNumber = `VTS-CMD-${verticalType}${verticalLead}-${verticalStroke}-${motorOrientation}-${housing}-${sensor}`;

  // データ行セルのスタイル生成関数（上枠を追加し、下枠は最終行のみ適用する）。
  const getDataCellStyle = (rowIndex: number, colIndex: number, isLastRow: boolean) => {
    const isFirstCol = colIndex === 0;

    const style: any = {
      width: COL_TOTAL_WIDTHS[colIndex],
      padding: 6,
      justifyContent: 'center',
      borderRightWidth: 1,
      borderRightColor: '#000000',
      // すべてのデータ行には上枠線を適用する。
      borderTopWidth: 1,
      borderTopColor: '#000000',
      // 下边框仅最后一行有
      borderBottomWidth: isLastRow ? 1 : 0,
      borderBottomColor: '#000000',
    };

    if (isFirstCol) {
      style.borderLeftWidth = 1;
      style.borderLeftColor = '#000000';
    }

    return style;
  };

  return (
    <View style={{ flex: 1, flexDirection: 'column' }}>
      <PageHeader />
      <View style={{ flex: 1 }}>
        <Text style={styles.sectionTitle}>{t('MSG_D005_001')}</Text> {/* 部品表 */}

        {/* テーブルコンテナ（固定幅） */}
        <View style={{ width: TABLE_WIDTH }}>
          {/* 1行目：VTS部品群（5列結合）+ 数量、上部ボーダー付き */}
          <View style={[styles.tableHeaderRow, { borderTopWidth: 1, borderTopColor: '#000000', borderBottomWidth: 0 }]}>
            {/* 左側の結合セル：左ボーダー、右ボーダー、下部ボーダーあり */}
            <View
              style={{
                width: COL_TOTAL_WIDTHS.slice(0, 5).reduce((a, b) => a + b, 0),
                paddingLeft: 6,
                paddingVertical: 4,
                justifyContent: 'center',
                borderLeftWidth: 1,
                borderLeftColor: '#000000',
                borderRightWidth: 1,
                borderRightColor: '#000000',
                borderBottomWidth: 1,
                borderBottomColor: '#000000',
              }}
            >
              <Text style={{ fontSize: 12, fontWeight: 'bold', textAlign: 'center' }}>
                {t('MSG_D005_019')} {/* VTS部品群 */}
              </Text>
            </View>
            {/* 右側の「数量」セル：右ボーダーあり、下部ボーダーなし（要件を満たす） */}
            <View
              style={{
                width: COL_TOTAL_WIDTHS[5],
                paddingVertical: 4,
                justifyContent: 'center',
                alignItems: 'center',
                borderRightWidth: 1,
                borderRightColor: '#000000',
              }}
            >
              <Text style={{ fontSize: 12, fontWeight: 'bold' }}>{t('MSG_D005_012')}</Text> {/* 数量 */}
            </View>
          </View>

          {/* 2行目：列名（最初の5列にテキスト表示、6列目は空白プレースホルダ）、下部ボーダー付き（データ行との区切り） */}
          <View style={[styles.tableHeaderRow, { borderBottomWidth: 0 }]}>
            {COLS.slice(0, 5).map((col, index) => (
              <View
                key={col}
                style={{
                  width: COL_TOTAL_WIDTHS[index],
                  padding: 6,
                  justifyContent: 'center',
                  borderRightWidth: 1,
                  borderRightColor: '#000000',
                  borderBottomWidth: 0,
                  ...(index === 0 ? { borderLeftWidth: 1, borderLeftColor: '#000000' } : {}),
                }}
              >
                <Text style={{ fontSize: 11 }}>{col}</Text>
              </View>
            ))}
            {/* 6列目は空白のプレースホルダ。下枠線は削除する。 */}
            <View
              style={{
                width: COL_TOTAL_WIDTHS[5],
                borderRightWidth: 1,
                borderRightColor: '#000000',
                borderBottomWidth: 0,
              }}
            />
          </View>

          {/* データ行 */}
          {rows.length === 0 ? (
            <View style={[styles.tableRow, { width: TABLE_WIDTH }]} wrap={false}>
              <View
                style={{
                  width: TABLE_WIDTH,
                  padding: 6,
                  justifyContent: 'center',
                  borderLeftWidth: 1,
                  borderLeftColor: '#000000',
                  borderRightWidth: 1,
                  borderRightColor: '#000000',
                  borderBottomWidth: 1,
                  borderBottomColor: '#000000',
                  borderTopWidth: 1,
                  borderTopColor: '#000000',
                }}
              >
                <Text>{t('MSG_COMMON_NO_DATA', 'データがありません')}</Text> {/* データがありません */}
              </View>
            </View>
          ) : (
            rows.map((r, rowIndex) => {
              const isLastRow = rowIndex === rows.length - 1;

              let displayModelNumber = r.model_number;
              let displayProductCode = r.product_code;

              if (r.category === '循環軸') {
                displayModelNumber = axisModelNumber;
                displayProductCode = '問合わせ';
              }else if(r.category === 'Circulation Axis'){
                displayModelNumber = axisModelNumber;
                displayProductCode = 'Inquiry';
              }

              return (
                <View
                  key={rowIndex}
                  style={[styles.tableRow, { width: TABLE_WIDTH, borderBottomWidth: 0 }]}
                  wrap={false}
                >
                  {/* No */}
                  <View style={getDataCellStyle(rowIndex, 0, isLastRow)}>
                    <Text>{r.no}</Text>
                  </View>
                  {/* 分類 */}
                  <View style={getDataCellStyle(rowIndex, 1, isLastRow)}>
                    <Text>{r.category}</Text>
                  </View>
                  {/* 名称 */}
                  <View style={getDataCellStyle(rowIndex, 2, isLastRow)}>
                    <Text>{r.name}</Text>
                  </View>
                  {/* 形番 */}
                  <View style={getDataCellStyle(rowIndex, 3, isLastRow)}>
                    <Text>{displayModelNumber}</Text>
                  </View>
                  {/* 商品コード */}
                  <View style={getDataCellStyle(rowIndex, 4, isLastRow)}>
                    <Text>{displayProductCode}</Text>
                  </View>
                  {/* 数量 */}
                  <View style={getDataCellStyle(rowIndex, 5, isLastRow)}>
                    <Text style={{ textAlign: 'center' }}>{r.quantity}</Text>
                  </View>
                </View>
              );
            })
          )}
        </View>
      </View>
      <PageFooter pageNum={pageNum} totalPages={totalPages} />
    </View>
  );
};
export type PaperSize = '58MM' | '80MM' | 'A4';

export const DEFAULT_PAPER_SIZE: PaperSize = '80MM';
export const PAPER_SIZES: ReadonlyArray<{ value: PaperSize; label: string; description: string }> = [
  { value: '58MM', label: '58mm', description: 'Small thermal POS printer' },
  { value: '80MM', label: '80mm', description: 'Standard thermal POS printer' },
  { value: 'A4', label: 'A4', description: 'Full-size invoice' },
];

export function normalizePaperSize(value?: string | null): PaperSize {
  return PAPER_SIZES.some((x) => x.value === value?.trim().toUpperCase())
    ? (value!.trim().toUpperCase() as PaperSize)
    : DEFAULT_PAPER_SIZE;
}

export function previewDimensions(value?: string | null): { width: string; minHeight: string } {
  switch (normalizePaperSize(value)) {
    case '58MM': return { width: '58mm', minHeight: '120mm' };
    case 'A4': return { width: '210mm', minHeight: '297mm' };
    default: return { width: '80mm', minHeight: '160mm' };
  }
}

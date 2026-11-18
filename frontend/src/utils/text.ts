/** Bỏ dấu tiếng Việt và chuyển chữ thường, để tìm kiếm không phân biệt dấu ("Nội" khớp "noi"). */
export function normalizeForSearch(value: string): string {
  return value
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase()
    .trim()
}

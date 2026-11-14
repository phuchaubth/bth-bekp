/**
 * Số thẻ BHYT: mã mới 17 ký tự (5 ký tự đầu như mã cũ + 12 số định danh) hoặc mã cũ 15 ký tự (2 chữ + 13 số).
 * Mã cũ vẫn được chấp nhận nhưng hiện cảnh báo nhẹ để người dùng cập nhật. Khớp với HealthInsuranceNumber ở backend.
 */
export const HEALTH_INSURANCE_PATTERN = /^[A-Za-z]{2}([0-9]{15}|[0-9]{13})$/

const LEGACY_PATTERN = /^[A-Za-z]{2}[0-9]{13}$/

export const HEALTH_INSURANCE_MESSAGE =
  'Số thẻ BHYT gồm 17 ký tự (2 chữ + 15 số); mã thẻ cũ 15 ký tự (2 chữ + 13 số) vẫn được chấp nhận.'

export const HEALTH_INSURANCE_LEGACY_WARNING =
  'Đây là mã thẻ BHYT cũ 15 ký tự. Vẫn lưu được, nhưng nên cập nhật sang mã thẻ mới 17 ký tự.'

export const isLegacyHealthInsurance = (value?: string | null) => !!value && LEGACY_PATTERN.test(value)

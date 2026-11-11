import { AutoComplete, Button, Col, DatePicker, Divider, Form, Input, Radio, Row, type FormInstance } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { citizenIdMismatch, isValidCitizenId } from './cccd'
import { commonEthnicities } from './labels'
import type { PatientFormValues } from './patientFormValues'

const DATE_FORMAT = 'DD/MM/YYYY'
const MIN_DATE = dayjs('1900-01-01')

const notFuture = (current: Dayjs) => current.isAfter(dayjs(), 'day')

interface Props {
  form: FormInstance<PatientFormValues>
  onFinish: (values: PatientFormValues) => void
  disabled?: boolean
  initialValues?: Partial<PatientFormValues>
}

/** Form thông tin người bệnh (dùng cho tiếp nhận và sửa hồ sơ). Quy tắc kiểm tra khớp với máy chủ. */
export function PatientForm({ form, onFinish, disabled = false, initialValues }: Props) {
  return (
    <Form<PatientFormValues>
      form={form}
      layout="vertical"
      onFinish={onFinish}
      disabled={disabled}
      initialValues={{ nationality: 'Việt Nam', ...initialValues }}
      scrollToFirstError
    >
      <Divider titlePlacement="start" plain style={{ marginTop: 0 }}>
        Thông tin trên CCCD
      </Divider>
      <Row gutter={16}>
        <Col xs={24} md={12}>
          <Form.Item
            label="Họ và tên"
            name="fullName"
            rules={[
              { required: true, message: 'Vui lòng nhập họ tên người bệnh.' },
              { max: 200, message: 'Họ tên tối đa 200 ký tự.' },
            ]}
          >
            <Input />
          </Form.Item>
        </Col>
        <Col xs={12} md={6}>
          <Form.Item
            label="Ngày sinh"
            name="dateOfBirth"
            rules={[
              { required: true, message: 'Vui lòng chọn ngày sinh.' },
              {
                validator: (_, value?: Dayjs) =>
                  !value || (!notFuture(value) && !value.isBefore(MIN_DATE))
                    ? Promise.resolve()
                    : Promise.reject(new Error('Ngày sinh không hợp lệ.')),
              },
            ]}
          >
            <DatePicker format={DATE_FORMAT} disabledDate={notFuture} style={{ width: '100%' }} placeholder="dd/mm/yyyy" />
          </Form.Item>
        </Col>
        <Col xs={12} md={6}>
          <Form.Item label="Giới tính" name="gender" rules={[{ required: true, message: 'Vui lòng chọn giới tính.' }]}>
            <Radio.Group
              options={[
                { value: 'Male', label: 'Nam' },
                { value: 'Female', label: 'Nữ' },
              ]}
            />
          </Form.Item>
        </Col>
        <Col xs={24} md={12}>
          <Form.Item
            label="Số CCCD"
            name="citizenId"
            dependencies={['dateOfBirth', 'gender']}
            rules={[
              ({ getFieldValue }) => ({
                validator: (_, value?: string) => {
                  if (!value) {
                    return Promise.resolve()
                  }
                  if (!isValidCitizenId(value)) {
                    return Promise.reject(new Error('Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.'))
                  }
                  const dob: Dayjs | undefined = getFieldValue('dateOfBirth')
                  const gender = getFieldValue('gender')
                  const mismatch = dob && gender ? citizenIdMismatch(value, dob.year(), gender) : null
                  return mismatch ? Promise.reject(new Error(mismatch)) : Promise.resolve()
                },
              }),
            ]}
          >
            <Input inputMode="numeric" maxLength={12} placeholder="12 chữ số" autoComplete="off" />
          </Form.Item>
        </Col>
        <Col xs={12} md={6}>
          <Form.Item
            label="Ngày cấp"
            name="citizenIdIssueDate"
            dependencies={['dateOfBirth']}
            rules={[
              ({ getFieldValue }) => ({
                validator: (_, value?: Dayjs) => {
                  const dob: Dayjs | undefined = getFieldValue('dateOfBirth')
                  return !value || (!notFuture(value) && (!dob || !value.isBefore(dob, 'day')))
                    ? Promise.resolve()
                    : Promise.reject(new Error('Ngày cấp phải sau ngày sinh và không sau hôm nay.'))
                },
              }),
            ]}
          >
            <DatePicker format={DATE_FORMAT} disabledDate={notFuture} style={{ width: '100%' }} placeholder="dd/mm/yyyy" />
          </Form.Item>
        </Col>
        <Col xs={12} md={6}>
          <Form.Item
            label="Số CMND cũ"
            name="oldIdNumber"
            rules={[{ pattern: /^(\d{9}|\d{12})$/, message: 'Số CMND cũ gồm 9 hoặc 12 chữ số.' }]}
          >
            <Input inputMode="numeric" maxLength={12} autoComplete="off" />
          </Form.Item>
        </Col>
        <Col xs={24}>
          <Form.Item label="Nơi thường trú" name="permanentAddress" rules={[{ max: 500, message: 'Địa chỉ tối đa 500 ký tự.' }]}>
            <Input />
          </Form.Item>
        </Col>
      </Row>

      <Divider titlePlacement="start" plain>
        Thông tin bổ sung
      </Divider>
      <Row gutter={16}>
        <Col xs={24}>
          <Form.Item label="Địa chỉ hiện tại" style={{ marginBottom: 0 }}>
            <Form.Item name="currentAddress" rules={[{ max: 500, message: 'Địa chỉ tối đa 500 ký tự.' }]}>
              <Input placeholder="Để trống nếu giống nơi thường trú" />
            </Form.Item>
          </Form.Item>
          <Button
            type="link"
            size="small"
            style={{ paddingLeft: 0, marginTop: -16, marginBottom: 8 }}
            onClick={() => form.setFieldValue('currentAddress', form.getFieldValue('permanentAddress'))}
          >
            Giống nơi thường trú
          </Button>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item
            label="Số điện thoại"
            name="phoneNumber"
            rules={[{ pattern: /^(0|\+84)[0-9]{9,10}$/, message: 'Số điện thoại không hợp lệ (ví dụ 0912345678).' }]}
          >
            <Input inputMode="tel" autoComplete="off" />
          </Form.Item>
        </Col>
        <Col xs={12} md={8}>
          <Form.Item label="Dân tộc" name="ethnicity" rules={[{ max: 50, message: 'Tối đa 50 ký tự.' }]}>
            <AutoComplete
              options={commonEthnicities.map((value) => ({ value }))}
              filterOption={(input, option) => (option?.value ?? '').toLowerCase().includes(input.toLowerCase())}
            />
          </Form.Item>
        </Col>
        <Col xs={12} md={8}>
          <Form.Item label="Quốc tịch" name="nationality" rules={[{ max: 100, message: 'Tối đa 100 ký tự.' }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item label="Nghề nghiệp" name="occupation" rules={[{ max: 100, message: 'Tối đa 100 ký tự.' }]}>
            <Input />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item
            label="Mã thẻ BHYT"
            name="healthInsuranceNumber"
            normalize={(value?: string) => value?.toUpperCase()}
            rules={[
              {
                pattern: /^([A-Za-z]{2}[0-9]{13}|[0-9]{10})$/,
                message: 'Mã BHYT gồm 15 ký tự (2 chữ + 13 số) hoặc mã số BHXH 10 chữ số.',
              },
            ]}
          >
            <Input maxLength={15} autoComplete="off" />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item
            label="Mã người bệnh (HIS)"
            name="patientCode"
            extra="Tự điền khi tìm thấy trên HIS."
            rules={[
              { max: 50, message: 'Tối đa 50 ký tự.' },
              { pattern: /^[A-Za-z0-9_-]+$/, message: 'Chỉ gồm chữ không dấu, số, gạch ngang hoặc gạch dưới.' },
            ]}
          >
            <Input autoComplete="off" />
          </Form.Item>
        </Col>
      </Row>
    </Form>
  )
}

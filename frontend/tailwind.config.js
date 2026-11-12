/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        // Mực xanh: chữ chính và nền sidebar
        ink: '#0E1B3D',
        navy: '#0B1634',
        // Tên token cũ được giữ để các trang hiện có không phải sửa
        forest: '#1A3FC4',   // cobalt đậm
        emerald: '#2350F0',  // cobalt — màu nhấn chính
        mint: '#EAF0FF',     // nền nhấn rất nhạt
        canvas: '#F3F6FB',   // giấy xanh nhạt
        line: '#E2E8F2',
        amberx: '#E9A23B',
        // Màu nghiệp vụ: tiền vào / tiền ra & con dấu
        jade: '#0F9F6E',
        seal: '#D9452F',
      },
      boxShadow: {
        panel: '0 1px 0 rgba(14,27,61,.04), 0 1px 2px rgba(14,27,61,.04), 0 12px 32px -12px rgba(14,27,61,.10)',
        lift: '0 1px 0 rgba(14,27,61,.04), 0 18px 40px -16px rgba(35,80,240,.28)',
      },
      fontFamily: {
        sans: ['"Be Vietnam Pro"', 'ui-sans-serif', 'system-ui', '-apple-system', 'Segoe UI', 'sans-serif'],
      },
      transitionTimingFunction: {
        out: 'cubic-bezier(.2,.7,.2,1)',
      },
    },
  },
  plugins: [],
}

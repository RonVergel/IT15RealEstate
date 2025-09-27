/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Pages/**/*.{cshtml,cs}",
    "./Views/**/*.{cshtml,cs,js}",
    "./Areas/**/*.{cshtml,cs}",
    "./wwwroot/**/*.js"
  ],
  darkMode: 'class',
  theme: {
    extend: {},
  },
  plugins: [],
  // Important: Prevent Tailwind from purging dynamic classes
  safelist: [
    'hidden',
    'scale-95',
    'scale-100',
    'opacity-0',
    'opacity-100',
    'bg-transparent',
    'bg-[rgba(29,29,29,0.4)]'
  ]
}
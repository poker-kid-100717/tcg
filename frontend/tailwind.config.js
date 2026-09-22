/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{js,jsx,ts,tsx}",
    "./public/index.html"
  ],
  theme: {
    extend: {
      colors: {
        primary: {
          50: "#f5f7ff",
          100: "#eaefff",
          200: "#d4deff",
          300: "#b0c1ff",
          400: "#839bff",
          500: "#5a77ff",
          600: "#3e5ff8",
          700: "#2e49e8",
          800: "#2a3dbc",
          900: "#263696",
          950: "#1a256d",
        },
        pokemon: {
          red: "#EE1515",
          blue: "#3B4CCA",
          yellow: "#FFDE00",
          yellowDark: "#B3A125",
          pokeblue: "#0A285F",
          background: "#F8F9FA",
          dark: "#1a1a1a",
        },
        secondary: {
          50: "#fff8f5",
          100: "#ffefea",
          200: "#ffdecd",
          300: "#ffc2a3",
          400: "#ff9e6e",
          500: "#ff7a3f",
          600: "#ff5d1f",
          700: "#e74c12",
          800: "#bf3c12",
          900: "#9c3312",
          950: "#541708",
        },
        neutral: {
          850: "#2e2e2e",
        },
      },
      fontFamily: {
        sans: ['Inter var', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        heading: ['Montserrat', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      boxShadow: {
        card: '0 4px 15px rgba(0, 0, 0, 0.1)',
        'card-hover': '0 12px 24px rgba(0, 0, 0, 0.15)',
      },
      animation: {
        'fade-in': 'fadeIn 0.5s ease-in-out',
        'slide-up': 'slideUp 0.5s ease-out',
      },
      keyframes: {
        fadeIn: {
          '0%': { opacity: '0' },
          '100%': { opacity: '1' },
        },
        slideUp: {
          '0%': { transform: 'translateY(20px)', opacity: '0' },
          '100%': { transform: 'translateY(0)', opacity: '1' },
        },
      },
    },
  },
  plugins: [],
};
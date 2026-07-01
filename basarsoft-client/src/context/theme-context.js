import { createContext, useContext } from 'react'

// Kept in a non-component module so Fast Refresh stays happy
// (ThemeContext.jsx only exports the ThemeProvider component).
export const ThemeContext = createContext(null)

export function useTheme() {
  const ctx = useContext(ThemeContext)
  if (!ctx) {
    throw new Error('useTheme must be used within a ThemeProvider')
  }
  return ctx
}

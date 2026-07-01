import { useCallback, useEffect, useMemo, useState } from 'react'
import { ThemeContext } from './theme-context'

const STORAGE_KEY = 'basarsoft-theme'

// Dark is the default; only an explicitly stored 'light'/'dark' overrides it.
function loadInitialTheme() {
  const stored = localStorage.getItem(STORAGE_KEY)
  return stored === 'light' || stored === 'dark' ? stored : 'dark'
}

export function ThemeProvider({ children }) {
  const [theme, setTheme] = useState(loadInitialTheme)

  // Reflect the choice on <html data-theme> (drives the CSS variables) and persist it.
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    localStorage.setItem(STORAGE_KEY, theme)
  }, [theme])

  const toggleTheme = useCallback(() => {
    setTheme((current) => (current === 'dark' ? 'light' : 'dark'))
  }, [])

  const value = useMemo(() => ({ theme, toggleTheme }), [theme, toggleTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

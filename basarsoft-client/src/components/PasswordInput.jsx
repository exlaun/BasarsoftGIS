import { useState } from 'react'
import './PasswordInput.css'

// Inline feather-style SVGs so no icon dependency is needed; they inherit the
// button's text color via `currentColor`.
const eyeIcon = (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7-11-7-11-7z" />
    <circle cx="12" cy="12" r="3" />
  </svg>
)
const eyeOffIcon = (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
    <line x1="1" y1="1" x2="23" y2="23" />
  </svg>
)

// A password text input with a built-in show/hide eye toggle. Extra props
// (value, onChange, autoComplete, required, minLength, …) pass straight through
// to the underlying <input>. Each instance tracks its own visibility.
export default function PasswordInput({ className = '', disabled, ...inputProps }) {
  const [show, setShow] = useState(false)

  return (
    <div className="password-field">
      <input
        {...inputProps}
        type={show ? 'text' : 'password'}
        disabled={disabled}
        className={`${className} password-field__input`.trim()}
      />
      <button
        type="button"
        className="password-field__toggle"
        onClick={() => setShow((v) => !v)}
        disabled={disabled}
        aria-label={show ? 'Hide password' : 'Show password'}
        aria-pressed={show}
        title={show ? 'Hide password' : 'Show password'}
      >
        {show ? eyeOffIcon : eyeIcon}
      </button>
    </div>
  )
}

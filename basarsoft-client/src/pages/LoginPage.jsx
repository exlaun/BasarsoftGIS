import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/auth-context'
import SpaceGlobe from '../components/SpaceGlobe'
import PasswordInput from '../components/PasswordInput'
import './LoginPage.css'

export default function LoginPage() {
  const { login, register, forgotPassword, resetPassword } = useAuth()
  const navigate = useNavigate()
  const globeRef = useRef(null)

  const [mode, setMode] = useState('login') // 'login' | 'register' | 'forgot' | 'reset'
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [info, setInfo] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [leaving, setLeaving] = useState(false)

  const isLogin = mode === 'login'
  const isRegister = mode === 'register'
  const isForgot = mode === 'forgot'
  const isReset = mode === 'reset'

  const handleSubmit = async (event) => {
    event.preventDefault()
    setError('')
    setInfo('')

    // Reset screen: make sure the two passwords match before calling the API.
    if (isReset && newPassword !== confirmPassword) {
      setError('Passwords do not match.')
      return
    }

    setSubmitting(true)
    try {
      if (isLogin || isRegister) {
        if (isRegister) {
          await register(username, password)
        } else {
          await login(username, password)
        }
        // Auth succeeded — play the cinematic, then hand off to the map.
        setLeaving(true)
        await globeRef.current?.flyToTurkey()
        navigate('/map', { replace: true })
        return
      }

      if (isForgot) {
        // Step 1: only advance to the reset screen once the username is confirmed valid.
        await forgotPassword(username)
        setMode('reset')
        setSubmitting(false)
        return
      }

      if (isReset) {
        // Step 2: set the new password, then send the user back to sign in.
        await resetPassword(username, newPassword)
        setMode('login')
        setPassword('')
        setNewPassword('')
        setConfirmPassword('')
        setInfo('Password updated — please sign in.')
        setSubmitting(false)
      }
    } catch (err) {
      const status = err.response?.status
      if (status === 401) setError('Wrong username or password. Please try again.')
      else if (status === 409) setError('That username is already taken.')
      else if (status === 404) setError('No account found with that username.')
      else setError('Something went wrong. Please try again.')
      setSubmitting(false)
      setLeaving(false)
    }
  }

  const toggleMode = () => {
    setMode(isRegister ? 'login' : 'register')
    setError('')
    setInfo('')
  }

  const goForgot = () => {
    setMode('forgot')
    setError('')
    setInfo('')
    setPassword('')
  }

  const backToLogin = () => {
    setMode('login')
    setError('')
    setInfo('')
    setNewPassword('')
    setConfirmPassword('')
  }

  const subtitle = isRegister
    ? 'Create an account'
    : isForgot
      ? 'Reset your password'
      : isReset
        ? `Set a new password for "${username}"`
        : 'Sign in to continue'

  const submitLabel = submitting
    ? 'Please wait…'
    : isRegister
      ? 'Register'
      : isForgot
        ? 'Continue'
        : isReset
          ? 'Update password'
          : 'Login'

  return (
    <div className="login-page">
      <SpaceGlobe ref={globeRef} />

      <form
        className={`login-card${leaving ? ' login-card--leaving' : ''}`}
        onSubmit={handleSubmit}
      >
        <h1 className="login-title">Başarsoftproject</h1>
        <p className="login-subtitle">{subtitle}</p>

        {(isLogin || isRegister || isForgot) && (
          <label className="login-label">
            Username
            <input
              className="login-input"
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoComplete="username"
              disabled={submitting}
              required
            />
          </label>
        )}

        {(isLogin || isRegister) && (
          <label className="login-label">
            Password
            <PasswordInput
              className="login-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete={isRegister ? 'new-password' : 'current-password'}
              disabled={submitting}
              required
            />
          </label>
        )}

        {isReset && (
          <>
            <label className="login-label">
              New password
              <PasswordInput
                className="login-input"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                autoComplete="new-password"
                disabled={submitting}
                minLength={6}
                required
              />
            </label>

            <label className="login-label">
              Confirm password
              <PasswordInput
                className="login-input"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                autoComplete="new-password"
                disabled={submitting}
                minLength={6}
                required
              />
            </label>
          </>
        )}

        {error && <p className="login-error">{error}</p>}
        {info && <p className="login-info">{info}</p>}

        <button className="login-button" type="submit" disabled={submitting}>
          {submitLabel}
        </button>

        {(isLogin || isRegister) && (
          <p className="login-switch">
            {isRegister ? 'Already have an account?' : "Don't have an account?"}{' '}
            <button
              type="button"
              className="login-link"
              onClick={toggleMode}
              disabled={submitting}
            >
              {isRegister ? 'Sign in' : 'Register'}
            </button>
          </p>
        )}

        {isLogin && (
          <button
            type="button"
            className="login-forgot"
            onClick={goForgot}
            disabled={submitting}
          >
            Forgot password
          </button>
        )}

        {(isForgot || isReset) && (
          <button
            type="button"
            className="login-link"
            onClick={backToLogin}
            disabled={submitting}
          >
            Back to sign in
          </button>
        )}
      </form>
    </div>
  )
}

import axios from 'axios'

// localStorage key holding { token, username, expiresAt }.
export const STORAGE_KEY = 'basarsoft-auth'

export function getStoredAuth() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

// Backend runs on the http profile (no HTTPS-redirect friction in dev).
const client = axios.create({
  baseURL: 'http://localhost:5032',
})

// Attach the bearer token to every request when we have one.
client.interceptors.request.use((config) => {
  const auth = getStoredAuth()
  if (auth?.token) {
    config.headers.Authorization = `Bearer ${auth.token}`
  }
  return config
})

// Safety net: if the server ever rejects the token, drop it and bounce to login.
client.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem(STORAGE_KEY)
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    return Promise.reject(error)
  },
)

export default client

import client from './client'

// Thin wrappers over the /api/admin/* endpoints (bearer token attached automatically by the axios
// client). Every call requires the caller to hold a management permission — the server enforces the
// "AdminAccess" policy and returns 403 otherwise.

// ---- Users ----
export async function listUsers() {
  const { data } = await client.get('/api/admin/users')
  return data
}

export async function createUser(body) {
  // body = { username, password, roleIds? }
  const { data } = await client.post('/api/admin/users', body)
  return data
}

export async function updateUser(id, body) {
  // body = { username, isActive, newPassword? }
  const { data } = await client.put(`/api/admin/users/${id}`, body)
  return data
}

export async function deleteUser(id) {
  await client.delete(`/api/admin/users/${id}`)
}

// The full permission catalogue annotated per user: source "role" | "direct" | "none".
export async function getUserPermissions(id) {
  const { data } = await client.get(`/api/admin/users/${id}/permissions`)
  return data
}

export async function setUserRoles(id, ids) {
  await client.put(`/api/admin/users/${id}/roles`, { ids })
}

// Sets the user's DIRECT permission grants (role-derived permissions are untouched server-side).
export async function setUserPermissions(id, ids) {
  await client.put(`/api/admin/users/${id}/permissions`, { ids })
}

// ---- Roles ----
export async function listRoles() {
  const { data } = await client.get('/api/admin/roles')
  return data
}

export async function createRole(body) {
  const { data } = await client.post('/api/admin/roles', body)
  return data
}

export async function updateRole(id, body) {
  const { data } = await client.put(`/api/admin/roles/${id}`, body)
  return data
}

export async function deleteRole(id) {
  await client.delete(`/api/admin/roles/${id}`)
}

export async function setRolePermissions(id, ids) {
  await client.put(`/api/admin/roles/${id}/permissions`, { ids })
}

// ---- Permissions ----
export async function listPermissions() {
  const { data } = await client.get('/api/admin/permissions')
  return data
}

export async function createPermission(body) {
  const { data } = await client.post('/api/admin/permissions', body)
  return data
}

export async function deletePermission(id) {
  await client.delete(`/api/admin/permissions/${id}`)
}

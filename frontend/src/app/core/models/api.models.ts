/**
 * The API contract, mirrored one-to-one.
 *
 * Hand-written rather than generated: the surface is small, and a reviewer can compare these against
 * docs/06-api-contract.md in one pass. The names match the JSON exactly, so there is no mapping layer to get
 * out of step with the server.
 */

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPreviousPage: boolean;
  readonly hasNextPage: boolean;
}

export interface UserListItem {
  readonly id: string;
  readonly username: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly roleId: number;
  readonly role: string;
  readonly isDeleted: boolean;
  readonly createdAt: string;
  readonly lastModifiedAt: string | null;
  readonly lastLoginAt: string | null;
  readonly isLockedOut: boolean;
  readonly deletedAt: string | null;
  readonly deletedBy: string | null;
}

export interface UserDetails {
  readonly id: string;
  readonly username: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly roleId: number;
  readonly role: string;
  readonly isDeleted: boolean;
  readonly createdAt: string;
  readonly createdBy: string;
  readonly lastModifiedAt: string | null;
  readonly lastModifiedBy: string | null;
  readonly lastLoginAt: string | null;
  readonly deletedAt: string | null;
  readonly deletedBy: string | null;

  /**
   * The base64 concurrency token. It has to round-trip on every update: without it the server cannot tell
   * which version of the row was edited, and the last writer would silently win.
   */
  readonly rowVersion: string;
}

export interface Role {
  readonly id: number;
  readonly name: string;
}

export interface Availability {
  readonly usernameAvailable: boolean | null;
  readonly emailAvailable: boolean | null;
}

export interface AuditLogEntry {
  readonly id: number;
  readonly entityName: string;
  readonly entityId: string;
  readonly entityDisplayName: string | null;
  readonly action: string;
  readonly performedByUserId: string | null;
  readonly performedByUsername: string;
  readonly timestamp: string;
  readonly ipAddress: string;
  readonly oldValues: Record<string, unknown> | null;
  readonly newValues: Record<string, unknown> | null;
  readonly correlationId: string | null;
}

export interface AuthenticatedUser {
  readonly id: string;
  readonly username: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly role: string;
}

export interface LoginResponse {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly user: AuthenticatedUser;
}

/** The three roles, as the server names them. */
export const RoleName = {
  admin: 'Admin',
  user: 'User',
  readOnly: 'ReadOnlyUser',
} as const;

export type RoleNameValue = (typeof RoleName)[keyof typeof RoleName];

export type SortDirection = 'Ascending' | 'Descending';

export interface UserListQuery {
  readonly pageNumber: number;
  readonly pageSize: number;
  readonly search?: string;
  readonly roleId?: number;
  readonly sortBy?: string;
  readonly sortDirection?: SortDirection;
}

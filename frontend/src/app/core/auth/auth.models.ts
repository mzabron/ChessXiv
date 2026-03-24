export interface AuthTokenResponse {
  accessToken: string;
  expiresAtUtc: string;
}

export interface AuthRegisterResponse {
  requiresEmailConfirmation: boolean;
  email: string;
  message: string;
}

export interface AuthRegisterRequest {
  login: string;
  email: string;
  password: string;
}

export interface AuthLoginRequest {
  login: string;
  password: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResendEmailConfirmationRequest {
  usernameOrEmail: string;
}

export interface ConfirmEmailRequest {
  userId: string;
  token: string;
}

export interface ChangePendingEmailRequest {
  usernameOrEmail: string;
  password: string;
  newEmail: string;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export interface AuthUser {
  userId: string;
  userName: string;
  email: string;
}

export interface AuthErrorResponse {
  code?: string;
  message?: string;
  email?: string;
  errors?: string[];
}

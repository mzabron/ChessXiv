export interface AccountSummary {
  nickname: string;
  email: string;
  savedGamesUsed: number;
  savedGamesLimit: number;
  importedGamesUsed: number;
  importedGamesLimit: number;
}

export interface ChangeAccountEmailRequest {
  newEmail: string;
  currentPassword: string;
}

export interface ChangeAccountPasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface DeleteAccountRequest {
  password: string;
}

export interface ConfirmAccountEmailChangeRequest {
  userId: string;
  newEmail: string;
  token: string;
}

import { DraftGamesSortBy, DraftGamesSortDirection } from './draft-import-api.service';

export type ExplorerEloMode = 'none' | 'one' | 'both' | 'avg';
export type ExplorerPositionMode = 'exact' | 'samePosition';

export interface ExplorerGamesFilterState {
  whiteFirstName: string;
  whiteLastName: string;
  blackFirstName: string;
  blackLastName: string;
  ignoreColors: boolean;
  eloEnabled: boolean;
  eloFrom: number | null;
  eloTo: number | null;
  eloMode: ExplorerEloMode;
  yearEnabled: boolean;
  yearFrom: number | null;
  yearTo: number | null;
  ecoCode: string;
  result: string;
  moveEnabled: boolean;
  moveCountFrom: number | null;
  moveCountTo: number | null;
  searchByPosition: boolean;
  fen: string;
  positionMode: ExplorerPositionMode;
  sortBy: DraftGamesSortBy;
  sortDirection: DraftGamesSortDirection;
  page: number;
  pageSize: number;
}

export interface ExplorerGamesFiltersQuery {
  whiteFirstName?: string;
  whiteLastName?: string;
  blackFirstName?: string;
  blackLastName?: string;
  ignoreColors?: boolean;
  eloEnabled?: boolean;
  eloFrom?: number;
  eloTo?: number;
  eloMode?: number;
  yearEnabled?: boolean;
  yearFrom?: number;
  yearTo?: number;
  ecoCode?: string;
  result?: string;
  moveCountFrom?: number;
  moveCountTo?: number;
  searchByPosition?: boolean;
  fen?: string;
  positionMode?: number;
}

const defaultState: ExplorerGamesFilterState = {
  whiteFirstName: '',
  whiteLastName: '',
  blackFirstName: '',
  blackLastName: '',
  ignoreColors: false,
  eloEnabled: false,
  eloFrom: null,
  eloTo: null,
  eloMode: 'none',
  yearEnabled: false,
  yearFrom: null,
  yearTo: null,
  ecoCode: '',
  result: '',
  moveEnabled: false,
  moveCountFrom: null,
  moveCountTo: null,
  searchByPosition: false,
  fen: '',
  positionMode: 'exact',
  sortBy: 'createdAt',
  sortDirection: 'desc',
  page: 1,
  pageSize: 18
};

export function createDefaultExplorerGamesFilterState(
  overrides: Partial<ExplorerGamesFilterState> = {}
): ExplorerGamesFilterState {
  return {
    ...defaultState,
    ...overrides
  };
}

export function toExplorerGamesFiltersQuery(state: ExplorerGamesFilterState): ExplorerGamesFiltersQuery {
  const whiteFirstName = normalizeText(state.whiteFirstName);
  const whiteLastName = normalizeText(state.whiteLastName);
  const blackFirstName = normalizeText(state.blackFirstName);
  const blackLastName = normalizeText(state.blackLastName);
  const ecoCode = normalizeText(state.ecoCode);
  const result = normalizeText(state.result);
  const fen = normalizeFenText(state.fen);

  const query: ExplorerGamesFiltersQuery = {
    whiteFirstName,
    whiteLastName,
    blackFirstName,
    blackLastName,
    ignoreColors: state.ignoreColors || undefined,
    eloEnabled: state.eloEnabled || undefined,
    eloFrom: normalizeNumber(state.eloFrom),
    eloTo: normalizeNumber(state.eloTo),
    eloMode: mapEloMode(state.eloMode),
    yearEnabled: state.yearEnabled || undefined,
    yearFrom: normalizeNumber(state.yearFrom),
    yearTo: normalizeNumber(state.yearTo),
    ecoCode,
    result,
    moveCountFrom: state.moveEnabled ? normalizeNumber(state.moveCountFrom) : undefined,
    moveCountTo: state.moveEnabled ? normalizeNumber(state.moveCountTo) : undefined,
    searchByPosition: state.searchByPosition || undefined,
    fen,
    positionMode: mapPositionMode(state.positionMode)
  };

  if (!state.eloEnabled) {
    delete query.eloFrom;
    delete query.eloTo;
    delete query.eloMode;
  }

  if (!state.yearEnabled) {
    delete query.yearFrom;
    delete query.yearTo;
  }

  if (!state.moveEnabled) {
    delete query.moveCountFrom;
    delete query.moveCountTo;
  }

  if (!state.searchByPosition) {
    delete query.fen;
    delete query.positionMode;
  }

  return query;
}

function mapEloMode(mode: ExplorerEloMode): number {
  return mode === 'one' ? 1 : mode === 'both' ? 2 : mode === 'avg' ? 3 : 0;
}

function mapPositionMode(mode: ExplorerPositionMode): number {
  if (mode === 'samePosition') {
    return 2;
  }

  return 0;
}

function normalizeText(value: string): string | undefined {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

function normalizeFenText(value: string): string | undefined {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}

function normalizeNumber(value: number | null): number | undefined {
  if (value === null || Number.isNaN(value)) {
    return undefined;
  }

  return value;
}

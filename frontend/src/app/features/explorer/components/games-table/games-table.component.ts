import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface Game {
  year: number;
  white: string;
  whiteElo: number;
  result: string;
  black: string;
  blackElo: number;
  eco: string;
  event: string;
  moveCount: number;
}

@Component({
  selector: 'app-games-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './games-table.component.html',
  styleUrl: './games-table.component.scss'
})
export class GamesTableComponent {
  @Input() games: Game[] = [];
}

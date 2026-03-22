import { Component, EventEmitter, Input, Output } from '@angular/core';

export interface MoveRow {
  number: number;
  white: string;
  black: string;
  whitePly: number;
  blackPly: number | null;
}

@Component({
  selector: 'app-move-list',
  standalone: true,
  templateUrl: './move-list.component.html',
  styleUrl: './move-list.component.scss'
})
export class MoveListComponent {
  @Input() moveRows: MoveRow[] = [];
  @Input() currentPly = 0;

  @Output() readonly plySelected = new EventEmitter<number>();

  protected selectPly(ply: number): void {
    this.plySelected.emit(ply);
  }
}

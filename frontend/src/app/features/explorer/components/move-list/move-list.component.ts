import { Component } from '@angular/core';

@Component({
  selector: 'app-move-list',
  standalone: true,
  templateUrl: './move-list.component.html',
  styleUrl: './move-list.component.scss'
})
export class MoveListComponent {
  protected readonly moveRows = [
    { number: 1, white: 'e4', black: 'c5' },
    { number: 2, white: 'Nf3', black: 'd6' },
    { number: 3, white: 'd4', black: 'cxd4' },
    { number: 4, white: 'Nxd4', black: 'Nf6' },
    { number: 5, white: 'Nc3', black: 'a6' },
    { number: 6, white: 'Bg5', black: 'e6' }
  ];
}

import { Component, EventEmitter, Output } from '@angular/core';

@Component({
  selector: 'app-empty-games-state',
  standalone: true,
  templateUrl: './empty-games-state.component.html',
  styleUrl: './empty-games-state.component.scss'
})
export class EmptyGamesStateComponent {
  @Output() importDatabase = new EventEmitter<void>();
  @Output() browseDatabases = new EventEmitter<void>();

  protected onImportClick(): void {
    this.importDatabase.emit();
  }

  protected onBrowseClick(): void {
    this.browseDatabases.emit();
  }
}

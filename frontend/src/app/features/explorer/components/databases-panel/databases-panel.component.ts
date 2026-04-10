import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface Database {
  id: string;
  name: string;
  owner: string;
  creationDate: Date;
  gamesCount: number;
}

@Component({
  selector: 'app-databases-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './databases-panel.component.html',
  styleUrl: './databases-panel.component.scss'
})
export class DatabasesPanelComponent {
  @Input() currentUser = '';
  @Input() activeDatabaseId: string | null = null;
  @Input() set databases(value: Database[]) {
    this.databasesSignal.set(value ?? []);
  }
  @Output() openDatabase = new EventEmitter<Database>();
  @Output() deleteDatabase = new EventEmitter<Database>();

  searchQuery = signal('');
  sortByCreationDesc = signal(true);

  private readonly databasesSignal = signal<Database[]>([]);

  filteredAndSortedDatabases = computed(() => {
    let result = this.databasesSignal();
    const query = this.searchQuery().toLowerCase().trim();

    if (query) {
      result = result.filter(db =>
        db.name.toLowerCase().includes(query) ||
        db.owner.toLowerCase().includes(query)
      );
    }

    result = [...result].sort((a, b) => {
      const timeA = a.creationDate.getTime();
      const timeB = b.creationDate.getTime();
      return this.sortByCreationDesc() ? timeB - timeA : timeA - timeB;
    });

    return result;
  });

  toggleSort() {
    this.sortByCreationDesc.update(val => !val);
  }
}

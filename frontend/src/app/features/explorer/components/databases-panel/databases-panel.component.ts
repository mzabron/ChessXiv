import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

interface Database {
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
  searchQuery = signal('');
  sortByCreationDesc = signal(true);

  databases = signal<Database[]>([
    { id: '1', name: 'My White Repertoire', owner: 'max', creationDate: new Date('2026-01-10'), gamesCount: 154 },
    { id: '2', name: 'Grandmaster Games 2025', owner: 'admin', creationDate: new Date('2025-12-01'), gamesCount: 12450 },
    { id: '3', name: 'Tactics Trainer', owner: 'john_doe', creationDate: new Date('2026-02-15'), gamesCount: 500 },
    { id: '4', name: 'Endgame Studies', owner: 'jane_smith', creationDate: new Date('2026-03-01'), gamesCount: 42 }
  ]);

  filteredAndSortedDatabases = computed(() => {
    let result = this.databases();
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

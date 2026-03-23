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
  private readonly currentUser = 'max';

  searchQuery = signal('');
  sortByCreationDesc = signal(true);
  myDatabasesOnly = signal(false);

  databases = signal<Database[]>([
    { id: '1', name: 'My White Repertoire', owner: 'max', creationDate: new Date('2026-01-10'), gamesCount: 154 },
    { id: '2', name: 'Grandmaster Games 2025', owner: 'admin', creationDate: new Date('2025-12-01'), gamesCount: 12450 },
    { id: '3', name: 'Tactics Trainer', owner: 'john_doe', creationDate: new Date('2026-02-15'), gamesCount: 500 },
    { id: '4', name: 'Endgame Studies', owner: 'jane_smith', creationDate: new Date('2026-03-01'), gamesCount: 42 }
  ]);

  filteredAndSortedDatabases = computed(() => {
    let result = this.databases();
    const query = this.searchQuery().toLowerCase().trim();

    if (this.myDatabasesOnly()) {
      result = result.filter(db => db.owner === this.currentUser);
    }

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

  toggleMyDatabases(): void {
    this.myDatabasesOnly.update(value => !value);
  }

  createNewDatabase(): void {
    const name = window.prompt('Database name');
    if (!name || !name.trim()) {
      return;
    }

    const newDatabase: Database = {
      id: this.generateId(),
      name: name.trim(),
      owner: this.currentUser,
      creationDate: new Date(),
      gamesCount: 0
    };

    this.databases.update(existing => [newDatabase, ...existing]);
  }

  private generateId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
  }
}

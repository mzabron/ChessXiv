import { Component } from '@angular/core';
import { ExplorerPageComponent } from './features/explorer/pages/explorer-page/explorer-page.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ExplorerPageComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {}

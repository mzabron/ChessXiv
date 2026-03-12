import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-filters-panel',
  standalone: true,
  templateUrl: './filters-panel.component.html',
  styleUrl: './filters-panel.component.scss'
})
export class FiltersPanelComponent {
  @Input() gamesLoaded = false;
}

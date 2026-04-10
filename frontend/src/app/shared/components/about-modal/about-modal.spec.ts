import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AboutModalComponent } from './about-modal';

describe('AboutModalComponent', () => {
  let component: AboutModalComponent;
  let fixture: ComponentFixture<AboutModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AboutModalComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(AboutModalComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

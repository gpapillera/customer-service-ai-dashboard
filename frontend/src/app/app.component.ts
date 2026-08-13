import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SaveFlashComponent } from './shared/save-flash.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, SaveFlashComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  title = 'customer-service-dashboard';
}

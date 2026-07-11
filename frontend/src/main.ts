import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import {
  Chart,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  LineController,
  BarController,
  Tooltip,
  Legend,
  Title,
  Filler,
} from 'chart.js';

Chart.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  LineController,
  BarController,
  Tooltip,
  Legend,
  Title,
  Filler,
);

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));

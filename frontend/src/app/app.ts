import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * The root component: a router outlet and nothing else.
 *
 * The application frame lives in `layout/app-shell`, which is itself a routed component, so the sign-in page
 * renders without the toolbar and navigation of a session that does not exist yet.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<router-outlet />',
})
export class App {}

import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Renders content only for the given roles.
 *
 * UX, not security. It removes a control the user cannot use, so the interface does not offer actions that
 * would fail - but the server refuses the same operations regardless, and the authorization matrix test proves
 * it for every role. Hiding a button is a courtesy; the 403 is the control.
 */
@Directive({
  selector: '[appHasRole]',
})
export class HasRoleDirective {
  private readonly auth = inject(AuthService);
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);

  readonly appHasRole = input.required<string | readonly string[]>();

  private rendered = false;

  constructor() {
    effect(() => {
      const required = this.appHasRole();
      const roles = typeof required === 'string' ? [required] : required;
      const allowed = this.auth.hasRole(...roles);

      if (allowed && !this.rendered) {
        this.viewContainer.createEmbeddedView(this.templateRef);
        this.rendered = true;
      } else if (!allowed && this.rendered) {
        this.viewContainer.clear();
        this.rendered = false;
      }
    });
  }
}

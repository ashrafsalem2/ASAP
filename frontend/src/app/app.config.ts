import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor } from './core/auth/auth.interceptor';
import { languageInterceptor } from './core/i18n/language.interceptor';
import { I18nService } from './core/i18n/i18n.service';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([languageInterceptor, authInterceptor])),
    {
      // Angular's own pipes read this once at injection. The application formats figures through
      // I18nService instead, which recomputes when the language changes, so this only has to be a
      // sensible starting point rather than the single source of truth.
      provide: LOCALE_ID,
      useFactory: () => inject(I18nService).locale(),
    },
  ],
};

import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { AppConfig } from './core/config/app-config.service';
import { AppTitleStrategy } from './core/ui/title-strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // Every component here is signals + OnPush, and zone.js is not a dependency (package.json never
    // added it). Without this, bootstrapApplication throws "NG0908: zoneless requires either
    // Zone.js or an explicit zoneless provider" -- caught silently by main.ts's .catch(), which
    // logs to the console but leaves <app-root> empty. That is the black screen.
    provideZonelessChangeDetection(),

    // withComponentInputBinding lets route parameters arrive as signal inputs, so a component reads
    // :roomId as an input() rather than subscribing to the ActivatedRoute.
    //
    // Scroll restoration is not the default and its absence is visible: leaving a long admin page
    // for the room list arrived halfway down, and going back did not return to where the user was.
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),

    { provide: TitleStrategy, useClass: AppTitleStrategy },

    provideHttpClient(withInterceptors([authInterceptor])),

    provideAppInitializer(() => {
      // Both inject() calls happen synchronously, before either await. inject() only works inside
      // an active injection context, and provideAppInitializer's factory is only run within one for
      // its synchronous portion -- an async function resumes its post-await continuation as a
      // microtask, by which point the context has already been popped. inject(AuthService) written
      // after the first await throws NG0203 for exactly that reason; capturing both services first
      // and only then awaiting keeps every inject() call in the synchronous part.
      const config = inject(AppConfig);
      const auth = inject(AuthService);

      // Order matters: the API's base URL has to be known before anything calls the API, and the
      // session restore is the first such call.
      return (async () => {
        await config.load();
        await auth.restore();
      })();
    }),
  ],
};

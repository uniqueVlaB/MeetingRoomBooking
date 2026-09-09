import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

bootstrapApplication(App, appConfig).catch((error: unknown) => {
  console.error(error);

  // A bootstrap failure otherwise leaves <app-root> empty with only the page background showing --
  // a blank screen with nothing in the DOM to say why. Writing the error where a person can see it
  // is what turned this exact failure into a support question instead of a five-second fix.
  document.body.innerHTML = `
    <div style="font: 14px system-ui, sans-serif; max-width: 40rem; margin: 3rem auto; padding: 1rem;">
      <h1 style="font-size: 1.1rem;">The application failed to start</h1>
      <p>Check the browser console for details.</p>
      <pre style="white-space: pre-wrap; background: #22272e; color: #e6edf3; padding: 0.75rem; border-radius: 6px;">${String(error)}</pre>
    </div>`;
});

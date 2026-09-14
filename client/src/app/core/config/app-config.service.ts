import { Injectable } from '@angular/core';

/** Shape of `public/config.json`. */
interface AppSettings {
  /** Absolute base URL of the API, or empty for same-origin. */
  apiBaseUrl: string;
}

/**
 * Configuration loaded at start-up from `config.json`.
 *
 * Runtime rather than build time on purpose. The client and API are deployed as two separate Azure
 * Web Apps, so the API's URL differs per environment; reading it at start-up means the same build
 * artefact can be pointed anywhere, and the deployment workflow only has to rewrite one small file.
 * Angular's `fileReplacements` would tie the URL to the build, and silently ships the wrong value if
 * `angular.json` ever stops declaring the replacement.
 */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  private settings: AppSettings = { apiBaseUrl: '' };

  /** Base URL for API calls. Empty means same-origin, served through the dev-server proxy. */
  get apiBaseUrl(): string {
    return this.settings.apiBaseUrl;
  }

  /** Absolute URL of the SignalR hub. */
  get hubUrl(): string {
    return `${this.settings.apiBaseUrl}/hubs/bookings`;
  }

  /**
   * Loads `config.json`.
   *
   * Failure is not fatal: the built-in default of same-origin is correct for local development and
   * for any deployment that serves both halves together, so a missing file should not leave the
   * user staring at a blank page.
   */
  async load(): Promise<void> {
    try {
      const response = await fetch('config.json', { cache: 'no-cache' });

      if (response.ok) {
        this.settings = { ...this.settings, ...(await response.json()) };
      }
    } catch {
      console.warn('config.json could not be read; falling back to same-origin API calls.');
    }
  }
}

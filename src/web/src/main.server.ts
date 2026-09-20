import { BootstrapContext, bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { config } from './app/app.config.server';

// Used only at build time: every route is rendered to its own HTML file (outputMode "static"), so the
// pages are complete for search engines and the store reviewers before any JavaScript runs.
const bootstrap = (context: BootstrapContext) => bootstrapApplication(App, config, context);

export default bootstrap;

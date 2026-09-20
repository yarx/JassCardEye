import { RenderMode, ServerRoute } from '@angular/ssr';

/** Every page is prerendered at build time; the site needs no server at all. */
export const serverRoutes: ServerRoute[] = [{ path: '**', renderMode: RenderMode.Prerender }];

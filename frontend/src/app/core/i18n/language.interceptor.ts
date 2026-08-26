import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { I18nService } from './i18n.service';

/**
 * Tells the server which language to answer in.
 *
 * Menu labels, account names and above all messages are rendered on the server, where the message
 * catalogue lives. Without this header the server would keep using the language stored on the
 * user's account, so switching to Arabic would turn the shell Arabic and leave every
 * server-rendered string in English -- a half-translated screen that looks broken.
 */
export const languageInterceptor: HttpInterceptorFn = (request, next) => {
  const language = inject(I18nService).language();

  return next(request.clone({ setHeaders: { 'Accept-Language': language } }));
};

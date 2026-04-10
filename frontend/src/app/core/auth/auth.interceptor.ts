import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthSessionService } from './auth-session.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const sessionService = inject(AuthSessionService);
  const token = sessionService.getAccessToken();

  const requestUrl = new URL(request.url, window.location.origin);
  const isApiRequest = requestUrl.pathname.startsWith('/api/');

  if (!token || !isApiRequest) {
    return next(request);
  }

  return next(
    request.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    })
  );
};

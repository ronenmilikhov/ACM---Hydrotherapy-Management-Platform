const normalizeUrl = (url) => String(url ?? '').trim().replace(/\/+$/, '');

const resolveApiBaseUrl = () => {
  const explicitBaseUrl = normalizeUrl(process.env.EXPO_PUBLIC_API_BASE_URL);
  if (explicitBaseUrl) return explicitBaseUrl;

  // Fallback directly to the Google Cloud Run API URL if environment variable is not defined
  return 'https://acm-api-service-ra3ceefohq-uc.a.run.app/api';
};

export const API_BASE_URL = resolveApiBaseUrl();

export const buildAuthHeaders = (token, baseHeaders = {}) => {
  const headers = { ...baseHeaders };
  const normalizedToken = String(token ?? '').trim();

  if (normalizedToken) {
    headers.Authorization = `Bearer ${normalizedToken}`;
  }

  return headers;
};

export const buildJsonAuthHeaders = (token) => buildAuthHeaders(token, {
  'Content-Type': 'application/json',
});

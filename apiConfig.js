import { NativeModules, Platform } from 'react-native';
import Constants from 'expo-constants';

const normalizeUrl = (url) => String(url ?? '').trim().replace(/\/+$/, '');

const isLoopbackHost = (host) => {
  const normalized = String(host ?? '').trim().toLowerCase();
  return normalized === 'localhost' || normalized === '127.0.0.1';
};

const isLikelyDevHost = (host) => {
  const normalized = String(host ?? '').trim().toLowerCase();
  if (!normalized) return false;

  if (/\s/.test(normalized)) return false;
  if (normalized === 'localhost' || normalized === '127.0.0.1') return true;
  if (normalized.startsWith('10.')) return true;
  if (normalized.startsWith('192.168.')) return true;

  const match = normalized.match(/^172\.(\d{1,3})\./);
  if (match) {
    const secondOctet = Number(match[1]);
    return secondOctet >= 16 && secondOctet <= 31;
  }

  // Allow common LAN/dev hostnames such as DESKTOP-ABC, my-pc.local, etc.
  if (/^[a-z0-9-]+(?:\.[a-z0-9-]+)*$/i.test(normalized)) return true;

  return false;
};

const extractHostFromUrl = (url) => {
  const raw = String(url ?? '').trim();
  if (!raw) return null;

  try {
    const parsed = new URL(raw);
    return parsed.hostname || null;
  } catch (_error) {
    const match = raw.match(/^[a-z][a-z0-9+.-]*:\/\/([^/:?#]+)(?::\d+)?/i);
    if (match?.[1]) {
      return match[1];
    }

    const hostUriMatch = raw.match(/^([^/:?#]+)(?::\d+)?$/);
    return hostUriMatch?.[1] ?? null;
  }
};

const getHostsFromMetroBundleUrl = () => {
  const candidates = [
    NativeModules?.SourceCode?.scriptURL,
    Constants?.expoConfig?.hostUri,
    Constants?.expoGoConfig?.debuggerHost,
    Constants?.manifest2?.extra?.expoClient?.hostUri,
    Constants?.manifest?.debuggerHost,
  ];

  const resolvedHosts = [];

  for (const candidate of candidates) {
    const host = extractHostFromUrl(candidate);
    if (!host) {
      continue;
    }

    const normalizedHost = String(host).trim();
    if (!normalizedHost) {
      continue;
    }

    if (!resolvedHosts.includes(normalizedHost)) {
      resolvedHosts.push(normalizedHost);
    }
  }

  return resolvedHosts;
};

const pickBestNativeDevHost = (hosts) => {
  const hostList = Array.isArray(hosts) ? hosts : [];
  if (hostList.length === 0) {
    return '';
  }

  const preferredLanHost = hostList.find(
    (host) => isLikelyInferredLanHost(host) && !isLoopbackHost(host)
  );
  if (preferredLanHost) {
    return preferredLanHost;
  }

  const loopbackOrLanHost = hostList.find((host) => isLikelyInferredLanHost(host));
  if (loopbackOrLanHost) {
    return loopbackOrLanHost;
  }

  return String(hostList[0] || '').trim();
};

const isLikelyInferredLanHost = (host) => {
  const normalized = String(host ?? '').trim().toLowerCase();
  if (!normalized) return false;

  if (normalized === 'localhost' || normalized === '127.0.0.1') return true;
  if (normalized.startsWith('10.')) return true;
  if (normalized.startsWith('192.168.')) return true;

  const private172Match = normalized.match(/^172\.(\d{1,3})\./);
  if (private172Match) {
    const secondOctet = Number(private172Match[1]);
    return secondOctet >= 16 && secondOctet <= 31;
  }

  if (normalized.endsWith('.local')) return true;

  // Common Windows/macOS machine names on home LANs.
  if (/^[a-z0-9-]+$/i.test(normalized)) return true;

  return false;
};

const isAndroidEmulator = () => {
  if (Platform.OS !== 'android') return false;

  const model = String(Platform?.constants?.Model ?? '').toLowerCase();
  const fingerprint = String(Platform?.constants?.Fingerprint ?? '').toLowerCase();
  const markerText = `${model} ${fingerprint}`;

  return /emulator|sdk|generic/.test(markerText);
};

const resolveApiBaseUrl = () => {
  const explicitBaseUrl = normalizeUrl(process.env.EXPO_PUBLIC_API_BASE_URL);
  if (explicitBaseUrl) return explicitBaseUrl;

  const hostFromEnvRaw = String(process.env.EXPO_PUBLIC_API_HOST ?? '').trim();
  const hostFromEnv = isLikelyDevHost(hostFromEnvRaw) ? hostFromEnvRaw : '';
  const inferredHost = pickBestNativeDevHost(getHostsFromMetroBundleUrl());
  const validatedInferredHost = isLikelyInferredLanHost(inferredHost) ? inferredHost : '';
  const preferredInferredHost = !isLoopbackHost(validatedInferredHost) ? validatedInferredHost : '';
  const fallbackHost = isAndroidEmulator() ? '10.0.2.2' : 'localhost';

  // On web, prefer localhost by default to avoid stale LAN IPs from .env causing timeouts.
  const webHostFromEnvRaw = String(process.env.EXPO_PUBLIC_API_WEB_HOST ?? '').trim();
  const webHostFromEnv = isLikelyDevHost(webHostFromEnvRaw) ? webHostFromEnvRaw : '';
  const nativeHost = preferredInferredHost || hostFromEnv || fallbackHost;
  const host = Platform.OS === 'web'
    ? (webHostFromEnv || 'localhost')
    : nativeHost;
  const port = String(process.env.EXPO_PUBLIC_API_PORT ?? '5202').trim();
  const scheme = String(process.env.EXPO_PUBLIC_API_SCHEME ?? 'http').trim();

  return `${scheme}://${host}:${port}/api`;
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

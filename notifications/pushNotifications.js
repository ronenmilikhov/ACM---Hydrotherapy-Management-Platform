import { Platform } from 'react-native';
import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { API_BASE_URL } from '../apiConfig';

const registeredUsers = new Set();
let notificationHandlerConfigured = false;
let androidChannelConfigured = false;

const isExpoGoRuntime = () => {
  const appOwnership = String(Constants?.appOwnership || '').trim().toLowerCase();
  const executionEnvironment = String(Constants?.executionEnvironment || '').trim().toLowerCase();
  return appOwnership === 'expo' || executionEnvironment === 'storeclient';
};

const ensureNotificationHandler = () => {
  if (notificationHandlerConfigured) {
    return;
  }

  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowAlert: true,
      shouldPlaySound: true,
      shouldSetBadge: true,
      shouldShowBanner: true,
      shouldShowList: true,
    }),
  });

  notificationHandlerConfigured = true;
};

const ensureAndroidChannel = async () => {
  if (androidChannelConfigured || Platform.OS !== 'android') {
    return;
  }

  try {
    await Notifications.setNotificationChannelAsync('default', {
      name: 'כללי',
      description: 'התראות כלליות מהמערכת',
      importance: Notifications.AndroidImportance.MAX,
      vibrationPattern: [0, 250, 250, 250],
      lightColor: '#00d4ff',
      sound: 'default',
      enableLights: true,
      enableVibrate: true,
      showBadge: true,
    });
    androidChannelConfigured = true;
  } catch (_error) {
    // Channel creation failure is non-fatal.
  }
};

/**
 * Call this once at app startup (e.g. in App.js) to ensure
 * the notification handler is registered immediately.
 * This guarantees that notifications received while the app
 * is in the foreground will be displayed in the system
 * notification center (pull-down shade).
 */
export const initializeNotifications = async () => {
  ensureNotificationHandler();
  await ensureAndroidChannel();
};

/**
 * Subscribe to notifications received while the app is in the foreground.
 * Returns a subscription object; call .remove() to unsubscribe.
 */
export const addNotificationReceivedListener = (callback) => {
  return Notifications.addNotificationReceivedListener(callback);
};

/**
 * Subscribe to user taps on notifications from the system notification center.
 * Returns a subscription object; call .remove() to unsubscribe.
 */
export const addNotificationResponseReceivedListener = (callback) => {
  return Notifications.addNotificationResponseReceivedListener(callback);
};

/**
 * Returns the notification response that launched the app, if any.
 */
export const getLastNotificationResponseAsync = () => {
  return Notifications.getLastNotificationResponseAsync();
};

const normalizeUserType = (rawUserType) => {
  const normalized = String(rawUserType || '').trim().toLowerCase();
  if (normalized === 'parent') {
    return 'Parent';
  }

  if (normalized === 'instructor') {
    return 'Instructor';
  }

  return '';
};

const resolveExpoProjectId = () => {
  const candidates = [
    process.env.EXPO_PUBLIC_EAS_PROJECT_ID,
    process.env.EAS_PROJECT_ID,
    Constants?.expoConfig?.extra?.eas?.projectId,
    Constants?.easConfig?.projectId,
    Constants?.manifest2?.extra?.eas?.projectId,
    Constants?.manifest?.extra?.eas?.projectId,
    Constants?.expoConfig?.extra?.projectId,
    Constants?.manifest2?.extra?.projectId,
    Constants?.manifest?.extra?.projectId,
  ];

  for (const candidate of candidates) {
    const normalized = String(candidate || '').trim();
    if (normalized) {
      return normalized;
    }
  }

  return '';
};

export const registerPushNotificationsForUser = async ({ userType, userId }) => {
  const normalizedUserType = normalizeUserType(userType);
  const normalizedUserId = Number(userId || 0);

  if (!normalizedUserType || normalizedUserId <= 0) {
    return { ok: false, reason: 'invalid-user' };
  }

  if (Platform.OS === 'web') {
    return { ok: false, reason: 'web-not-supported' };
  }

  if (Platform.OS === 'android' && isExpoGoRuntime()) {
    return { ok: false, reason: 'android-expo-go-unsupported' };
  }

  if (!Device.isDevice) {
    return { ok: false, reason: 'physical-device-required' };
  }

  const cacheKey = `${normalizedUserType}-${normalizedUserId}`;
  // Do not short-circuit by local cache. We intentionally re-register to
  // rebind the token on the server when users switch accounts on the same device.

  ensureNotificationHandler();

  let permissionStatus = (await Notifications.getPermissionsAsync()).status;
  if (permissionStatus !== 'granted') {
    permissionStatus = (await Notifications.requestPermissionsAsync()).status;
  }

  if (permissionStatus !== 'granted') {
    return { ok: false, reason: 'permission-denied' };
  }

  await ensureAndroidChannel();

  let tokenResponse = null;
  try {
    const projectId = resolveExpoProjectId();

    if (projectId) {
      // Preferred: use explicit project ID
      tokenResponse = await Notifications.getExpoPushTokenAsync({ projectId });
    } else {
      // Fallback chain for Expo Go / dev environments
      // Try 1: auto-detect from manifest (works when logged into Expo)
      try {
        tokenResponse = await Notifications.getExpoPushTokenAsync();
      } catch (autoDetectError) {
        // Try 2: use experienceId (format: @owner/slug) for Expo Go
        const slug = Constants?.expoConfig?.slug || Constants?.manifest?.slug || '';
        const owner = Constants?.expoConfig?.owner || Constants?.manifest?.owner || '';
        const experienceId = owner && slug ? `@${owner}/${slug}` : '';

        if (experienceId) {
          tokenResponse = await Notifications.getExpoPushTokenAsync({
            experienceId,
          });
        } else {
          // Re-throw the original error if no fallback is available
          throw autoDetectError;
        }
      }
    }
  } catch (error) {
    const normalizedError = String(error?.message || 'token-fetch-failed');

    if (/projectid/i.test(normalizedError) || /experienceid/i.test(normalizedError)) {
      return {
        ok: false,
        reason: 'missing-project-id',
        detail: 'Run "npx expo login" then "npx -y eas-cli init" in your project terminal to configure push notifications.',
      };
    }

    return {
      ok: false,
      reason: normalizedError,
    };
  }

  const pushToken = String(tokenResponse?.data || '').trim();
  if (!pushToken) {
    return { ok: false, reason: 'token-not-available' };
  }

  let registerResponse;
  try {
    registerResponse = await fetch(`${API_BASE_URL}/chat/push/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userType: normalizedUserType,
        userId: normalizedUserId,
        pushToken,
        platform: Platform.OS,
        deviceId: null,
      }),
    });
  } catch (error) {
    return {
      ok: false,
      reason: String(error?.message || 'token-registration-network-failed'),
    };
  }

  if (!registerResponse.ok) {
    const payload = await registerResponse.json().catch(() => null);
    return {
      ok: false,
      reason: payload?.message || 'token-registration-failed',
    };
  }

  registeredUsers.add(cacheKey);
  return { ok: true, pushToken };
};

export const unregisterPushNotificationsForUser = async ({ userType, userId }) => {
  const normalizedUserType = normalizeUserType(userType);
  const normalizedUserId = Number(userId || 0);
  const cacheKey = `${normalizedUserType}-${normalizedUserId}`;

  if (!normalizedUserType || normalizedUserId <= 0) {
    return { ok: false, reason: 'invalid-user' };
  }

  if (Platform.OS === 'web') {
    return { ok: false, reason: 'web-not-supported' };
  }

  if (!Device.isDevice) {
    return { ok: false, reason: 'physical-device-required' };
  }

  try {
    const projectId = resolveExpoProjectId();

    // Just try to get the existing token without registering a new one if possible
    let tokenResponse;
    if (projectId) {
      tokenResponse = await Notifications.getExpoPushTokenAsync({ projectId });
    } else {
      tokenResponse = await Notifications.getExpoPushTokenAsync();
    }

    const pushToken = tokenResponse?.data;
    if (!pushToken) {
      return { ok: false, reason: 'no-token-found' };
    }

    const deregisterResponse = await fetch(`${API_BASE_URL}/chat/push/deregister`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userType: normalizedUserType,
        userId: normalizedUserId,
        pushToken,
      }),
    });

    return { ok: deregisterResponse.ok };
  } catch (error) {
    return { ok: false, reason: String(error?.message || 'network-error') };
  } finally {
    // Ensure the next login can always attempt a fresh server registration.
    registeredUsers.delete(cacheKey);
  }
};

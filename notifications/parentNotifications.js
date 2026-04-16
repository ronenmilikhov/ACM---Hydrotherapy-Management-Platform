import { API_BASE_URL } from '../apiConfig';

export const parseNotificationPayload = (rawPayload) => {
  if (!rawPayload) {
    return null;
  }

  if (typeof rawPayload === 'object') {
    return rawPayload;
  }

  if (typeof rawPayload !== 'string') {
    return null;
  }

  const normalized = rawPayload.trim();
  if (!normalized) {
    return null;
  }

  try {
    return JSON.parse(normalized);
  } catch (_error) {
    return null;
  }
};

export const fetchParentNotifications = async (parentId) => {
  const safeParentId = Number(parentId || 0);
  if (safeParentId <= 0) {
    return [];
  }

  const response = await fetch(`${API_BASE_URL}/lesson-scheduling/parent/${safeParentId}/notifications`);
  const payload = await response.json().catch(() => null);

  if (!response.ok) {
    throw new Error(payload?.message || 'לא ניתן לטעון התראות כרגע.');
  }

  return Array.isArray(payload) ? payload : [];
};

export const markParentNotificationsAsRead = async (parentId) => {
  const safeParentId = Number(parentId || 0);
  if (safeParentId <= 0) {
    return 0;
  }

  const response = await fetch(`${API_BASE_URL}/lesson-scheduling/parent/${safeParentId}/notifications/mark-read`, {
    method: 'POST',
  });

  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message || 'לא ניתן לסמן התראות כנקראו כרגע.');
  }

  return Number(payload?.markedCount || 0);
};

export const markParentChatNotificationsAsRead = async (parentId, instructorId) => {
  const safeParentId = Number(parentId || 0);
  const safeInstructorId = Number(instructorId || 0);

  if (safeParentId <= 0 || safeInstructorId <= 0) {
    return 0;
  }

  const response = await fetch(`${API_BASE_URL}/lesson-scheduling/parent/${safeParentId}/notifications/chat/mark-read`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ instructorId: safeInstructorId }),
  });

  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message || 'לא ניתן לסמן הודעות צ׳אט כנקראו כרגע.');
  }

  return Number(payload?.markedCount || 0);
};

export const countUnreadNotifications = (notifications) => {
  return (Array.isArray(notifications) ? notifications : []).filter((item) => item?.isRead !== true).length;
};

export const buildUnreadChatCountByInstructor = (notifications) => {
  const result = {};

  (Array.isArray(notifications) ? notifications : []).forEach((item) => {
    if (item?.isRead === true) {
      return;
    }

    const notificationType = String(item?.notificationType || '').trim().toLowerCase();
    if (notificationType !== 'chatmessage') {
      return;
    }

    const payload = parseNotificationPayload(item?.payloadJson);
    const senderType = String(payload?.senderType || '').trim().toLowerCase();
    if (senderType !== 'instructor') {
      return;
    }

    const instructorId = Number(payload?.instructorId || 0);
    if (instructorId <= 0) {
      return;
    }

    result[instructorId] = Number(result[instructorId] || 0) + 1;
  });

  return result;
};

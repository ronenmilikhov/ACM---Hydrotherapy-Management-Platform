import React, { useCallback, useEffect, useState } from 'react';
import {
  Platform,
  SafeAreaView,
  ScrollView,
  StatusBar,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import AquaticBackground from '../components/ui/AquaticBackground';
import RoleHeader from '../components/ui/RoleHeader';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';
import {
  fetchParentNotifications,
  markParentNotificationsAsRead,
  parseNotificationPayload,
} from '../notifications/parentNotifications';

const invitationStatusMap = {
  Pending: 'ממתין',
  Confirmed: 'נסגר ואושר',
  Cancelled: 'בוטל',
};

const responseStatusMap = {
  Pending: 'ממתין לתגובה',
  Approved: 'אושר',
  Rejected: 'נדחה',
};

const invitationNotificationTypes = new Set([
  'lessoninvite',
  'lessoncreated',
  'lessoncancelled',
  'lessonresponse',
  'lessonupdate',
]);

const ISRAEL_TIME_ZONE = 'Asia/Jerusalem';

const parseUtcLikeDate = (rawValue) => {
  const normalized = String(rawValue ?? '').trim();
  if (!normalized) {
    return new Date('');
  }

  const hasExplicitTimezone = /(?:[zZ]|[+\-]\d{2}:\d{2})$/.test(normalized);
  const isIsoDateTimeWithoutTimezone = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?$/.test(normalized);
  const normalizedForParsing = (!hasExplicitTimezone && isIsoDateTimeWithoutTimezone)
    ? `${normalized}Z`
    : normalized;

  return new Date(normalizedForParsing);
};

const formatDateTime = (rawValue) => {
  const parsed = parseUtcLikeDate(rawValue);
  if (Number.isNaN(parsed.getTime())) {
    return 'לא זמין';
  }

  const formatOptions = {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: ISRAEL_TIME_ZONE,
  };

  try {
    return parsed.toLocaleString('he-IL', formatOptions);
  } catch (_error) {
    return parsed.toLocaleString('he-IL', {
      day: '2-digit',
      month: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }
};

const normalizeNotificationText = (value) => {
  if (typeof value !== 'string' || !value) {
    return '';
  }

  return value.replace(/הקיעור/g, 'השיעור');
};

export default function ParentNotificationsInbox({ navigation, route }) {
  const authUser = route?.params?.authUser;
  const parentId = Number(authUser?.id || 0);

  const [items, setItems] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [actionLoadingId, setActionLoadingId] = useState(null);
  const [errorText, setErrorText] = useState('');
  const [successText, setSuccessText] = useState('');

  const loadNotifications = useCallback(async (options = {}) => {
    const { markAsRead = false } = options;

    if (!parentId) {
      setErrorText('לא זוהה הורה מחובר.');
      setItems([]);
      return;
    }

    try {
      setIsLoading(true);
      setErrorText('');

      if (markAsRead) {
        await markParentNotificationsAsRead(parentId).catch(() => {
          // Keep going to avoid blocking the inbox if marking failed.
        });
      }

      const notifications = await fetchParentNotifications(parentId);
      setItems(notifications);
    } catch (error) {
      setItems([]);
      setErrorText(error?.message || 'לא ניתן לטעון התראות כרגע.');
    } finally {
      setIsLoading(false);
    }
  }, [parentId]);

  useEffect(() => {
    loadNotifications({ markAsRead: true });
  }, [loadNotifications]);

  useFocusEffect(
    useCallback(() => {
      loadNotifications({ markAsRead: true });
    }, [loadNotifications]),
  );

  const openChatFromNotification = (item) => {
    const payload = parseNotificationPayload(item?.payloadJson);
    const childId = Number(payload?.childId || 0);
    const instructorId = Number(payload?.instructorId || 0);
    const conversationId = Number(payload?.conversationId || 0);

    if (!parentId || childId <= 0 || instructorId <= 0) {
      setErrorText('לא ניתן לפתוח את הצ׳אט מההתראה הזו.');
      return;
    }

    const parentName = String(authUser?.fullName || '').trim() || 'הורה';
    const childName = String(payload?.childName || '').trim();
    const instructorName = String(payload?.instructorName || '').trim();

    navigation.navigate('ChatPage', {
      authUser,
      parentId,
      childId,
      instructorId,
      conversationId: conversationId > 0 ? conversationId : undefined,
      parentName,
      childName,
      instructorName,
    });
  };

  const respond = async (item, action) => {
    const recipientId = Number(item?.relatedRecipientId || 0);
    if (!parentId || !recipientId) {
      setErrorText('לא ניתן להשיב להזמנה זו.');
      return;
    }

    setSuccessText('');
    setErrorText('');

    try {
      setActionLoadingId(recipientId);
      const response = await fetch(
        `${API_BASE_URL}/lesson-scheduling/parent/${parentId}/recipients/${recipientId}/respond`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ action }),
        },
      );
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לעדכן תגובה כרגע.');
      }

      setSuccessText(action === 'approve' ? 'ההזמנה אושרה בהצלחה.' : 'ההזמנה נדחתה.');
      await loadNotifications();
    } catch (error) {
      setErrorText(error?.message || 'לא ניתן לעדכן תגובה כרגע.');
    } finally {
      setActionLoadingId(null);
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="dark-content" />
      <AquaticBackground variant="parent" showWaves={false} />

      <RoleHeader
        title="התראות"
        onMenuPress={() => navigation.goBack()}
        leftIcon="›"
        rightIcon="↻"
        onRightPress={() => loadNotifications()}
        theme="dark"
        titleStyle={{ color: '#092d42', textShadowColor: 'transparent' }}
      />

      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        {isLoading ? <Text style={styles.stateText}>טוען התראות...</Text> : null}
        {!isLoading && errorText ? <Text style={styles.errorText}>{errorText}</Text> : null}
        {successText ? <Text style={styles.successText}>{successText}</Text> : null}

        {!isLoading && !errorText && items.length === 0 ? (
          <Text style={styles.stateText}>אין התראות חדשות כרגע.</Text>
        ) : null}

        {items.map((item) => {
          const payload = parseNotificationPayload(item?.payloadJson);
          const notificationType = String(item.notificationType || '').trim().toLowerCase();
          const isChatNotification = notificationType === 'chatmessage';
          const isBroadcastNotification = notificationType === 'instructorbroadcast';
          const isInvitationNotification = invitationNotificationTypes.has(notificationType);
          const broadcastInstructorName = String(payload?.instructorName || '').trim();
          const invitationPending = String(item.invitationStatus || '').toLowerCase() === 'pending';
          const responsePending = String(item.recipientResponseStatus || '').toLowerCase() === 'pending';
          const canRespond = isInvitationNotification && invitationPending && responsePending && Number(item.relatedRecipientId || 0) > 0;
          const isRowLoading = actionLoadingId === Number(item.relatedRecipientId || 0);

          return (
            <View
              key={String(item.notificationId)}
              style={styles.card}
            >
              <Text style={styles.title}>
                {normalizeNotificationText(item.title) || 'התראה'}
              </Text>
              <Text style={styles.body}>
                {normalizeNotificationText(item.body)}
              </Text>

              {isBroadcastNotification && broadcastInstructorName ? (
                <Text style={styles.meta}>
                  מאת: {normalizeNotificationText(broadcastInstructorName)}
                </Text>
              ) : null}

              {isInvitationNotification ? (
                <>
                  <Text style={styles.meta}>סטטוס הזמנה: {invitationStatusMap[item.invitationStatus] || item.invitationStatus || 'לא זמין'}</Text>
                  <Text style={styles.meta}>תגובה שלכם: {responseStatusMap[item.recipientResponseStatus] || item.recipientResponseStatus || 'לא זמין'}</Text>
                  {String(item.targetMetric || '').trim() ? (
                    <Text style={styles.meta}>מדד מטרה לשיעור: <Text style={{ fontWeight: '700', color: '#ffcf77' }}>{item.targetMetric}</Text></Text>
                  ) : null}
                </>
              ) : null}

              <Text style={styles.meta}>נשלח: {formatDateTime(item.createdAt)}</Text>

              {isChatNotification ? (
                <View style={styles.actionsRow}>
                  <PrimaryButton
                    label="פתח צ׳אט"
                    onPress={() => openChatFromNotification(item)}
                    style={styles.actionShell}
                    gradientStyle={styles.approveButton}
                    textStyle={styles.actionText}
                    colorsOverride={['#2E77BC', '#255E97']}
                  />
                </View>
              ) : canRespond ? (
                <View style={styles.actionsRow}>
                  <PrimaryButton
                    label={isRowLoading ? 'מעדכן...' : 'דחייה'}
                    onPress={() => respond(item, 'reject')}
                    disabled={isRowLoading}
                    style={styles.actionShell}
                    gradientStyle={styles.rejectButton}
                    textStyle={styles.actionText}
                    colorsOverride={['#B8505F', '#9E3D4B']}
                  />
                  <PrimaryButton
                    label={isRowLoading ? 'מעדכן...' : 'אישור'}
                    onPress={() => respond(item, 'approve')}
                    disabled={isRowLoading}
                    style={styles.actionShell}
                    gradientStyle={styles.approveButton}
                    textStyle={styles.actionText}
                    colorsOverride={['#1D8F69', '#157556']}
                  />
                </View>
              ) : null}
            </View>
          );
        })}

      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: '#E8F5FD', paddingTop: Platform.OS === 'android' ? StatusBar.currentHeight : 0 },
  content: { paddingHorizontal: 18, paddingTop: 8, paddingBottom: 48, gap: 12 },
  stateText: {
    color: 'rgba(255,255,255,0.72)',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    fontSize: 14,
    marginTop: 8,
  },
  errorText: {
    color: '#ff9a8b',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    fontSize: 14,
    marginTop: 8,
  },
  successText: {
    color: '#9ef3c8',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    fontSize: 14,
    marginTop: 8,
  },
  card: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.16)',
    backgroundColor: 'rgba(255,255,255,0.07)',
    paddingHorizontal: 12,
    paddingVertical: 10,
    marginBottom: 8,
  },
  title: {
    color: '#8aeaff',
    fontSize: 12,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 4,
  },
  body: {
    color: 'rgba(255,255,255,0.86)',
    fontSize: 14,
    lineHeight: 21,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  meta: {
    marginTop: 6,
    color: 'rgba(255,255,255,0.58)',
    fontSize: 12,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  actionsRow: {
    marginTop: 10,
    flexDirection: Platform.OS === 'ios' ? 'row-reverse' : 'row',
    gap: 10,
  },
  actionShell: {
    flex: 1,
  },
  approveButton: {
    paddingVertical: 10,
  },
  rejectButton: {
    paddingVertical: 10,
  },
  actionText: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '700',
  },
});

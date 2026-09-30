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
    year: '2-digit',
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
      year: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }
};

const CHAT_TITLE_PREFIX = 'הודעה חדשה מ-';

const extractSenderNameFromChatTitle = (rawTitle) => {
  const title = String(rawTitle || '').trim();
  if (!title.startsWith(CHAT_TITLE_PREFIX)) {
    return '';
  }

  return String(title.slice(CHAT_TITLE_PREFIX.length) || '').trim();
};

const collectPayloadChildNames = (payload) => {
  const childNames = [];

  if (Array.isArray(payload?.childNames)) {
    payload.childNames.forEach((childName) => {
      const normalized = String(childName || '').trim();
      if (normalized && !childNames.includes(normalized)) {
        childNames.push(normalized);
      }
    });
  }

  const singleChildName = String(payload?.childName || '').trim();
  if (singleChildName && !childNames.includes(singleChildName)) {
    childNames.push(singleChildName);
  }

  return childNames;
};

const buildChatNotificationTitle = (item, payload) => {
  const senderName = String(payload?.parentName || '').trim()
    || extractSenderNameFromChatTitle(item?.title)
    || 'הורה';

  const childNames = collectPayloadChildNames(payload);
  if (childNames.length === 0) {
    return `${CHAT_TITLE_PREFIX}${senderName}`;
  }

  const childrenText = childNames.join(', ');
  return `${CHAT_TITLE_PREFIX}${senderName} (הורה של: ${childrenText})`;
};

const parseNotificationPayload = (rawPayloadJson) => {
  if (!rawPayloadJson || typeof rawPayloadJson !== 'string') {
    return null;
  }

  try {
    const parsed = JSON.parse(rawPayloadJson);
    return parsed && typeof parsed === 'object' ? parsed : null;
  } catch (_error) {
    return null;
  }
};

export default function InstructorNotificationsInbox({ navigation, route }) {
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);

  const [items, setItems] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorText, setErrorText] = useState('');

  const loadNotifications = useCallback(async (options = {}) => {
    const { markAsRead = false } = options;

    if (!instructorId) {
      setErrorText('לא זוהה מדריך מחובר. התחברו מחדש.');
      setItems([]);
      return;
    }

    try {
      setIsLoading(true);
      setErrorText('');

      if (markAsRead) {
        await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/notifications/mark-read`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
        }).catch(() => null);
      }

      const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/notifications`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון התראות כרגע.');
      }

      setItems(Array.isArray(payload) ? payload : []);
    } catch (error) {
      setItems([]);
      setErrorText(error?.message || 'לא ניתן לטעון התראות כרגע.');
    } finally {
      setIsLoading(false);
    }
  }, [instructorId]);

  const openChatFromNotification = useCallback((item) => {
    const payload = parseNotificationPayload(item?.payloadJson);
    const conversationId = Number(payload?.conversationId || 0);
    const parentId = Number(payload?.parentId || 0);
    const childId = Number(payload?.childId || 0);

    if (!instructorId || parentId <= 0 || childId <= 0) {
      setErrorText('לא ניתן לפתוח את הצ׳אט מההתראה הזו.');
      return;
    }

    navigation.navigate('ChatPage', {
      fromInstructor: true,
      conversationId: conversationId > 0 ? conversationId : undefined,
      parentId,
      childId,
      instructorId,
      parentName: 'הורה',
      childName: String(payload?.childName || '').trim() || 'ילד',
      instructorName: authUser?.fullName || 'מדריך',
    });
  }, [authUser?.fullName, instructorId, navigation]);

  useEffect(() => {
    loadNotifications({ markAsRead: true });
  }, [loadNotifications]);

  useFocusEffect(
    useCallback(() => {
      loadNotifications({ markAsRead: true });
    }, [loadNotifications]),
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="dark-content" />
      <AquaticBackground variant="instructor" showWaves={false} />

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

        {!isLoading && !errorText && items.length === 0 ? (
          <Text style={styles.stateText}>עדיין אין התראות חדשות.</Text>
        ) : null}

        {items.map((item) => {
          const notificationType = String(item.notificationType || '').trim().toLowerCase();
          const isChatNotification = notificationType === 'chatmessage';
          const payload = parseNotificationPayload(item?.payloadJson);
          const titleText = isChatNotification
            ? buildChatNotificationTitle(item, payload)
            : (item.title || 'התראה');

          return (
            <View key={String(item.notificationId)} style={styles.card}>
              <Text style={styles.title}>{titleText}</Text>
              <Text style={styles.body}>{item.body || ''}</Text>
              <Text style={styles.meta}>נשלח: {formatDateTime(item.createdAt)}</Text>

              {isChatNotification ? (
                <PrimaryButton
                  label="פתח צ׳אט"
                  onPress={() => openChatFromNotification(item)}
                  style={styles.actionButtonShell}
                  gradientStyle={styles.chatActionButton}
                  textStyle={styles.actionButtonText}
                  colorsOverride={['#2E77BC', '#255E97']}
                />
              ) : null}
            </View>
          );
        })}

      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: '#E8F5FD' },
  content: { paddingHorizontal: 18, paddingTop: 18, paddingBottom: 24, gap: 12 },
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
  actionButtonShell: { marginTop: 10 },
  actionButton: { paddingVertical: 14 },
  chatActionButton: { paddingVertical: 12 },
  actionButtonText: { color: '#FFFFFF', fontSize: 14, fontWeight: '700' },
});

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Platform,
  SafeAreaView,
  ScrollView,
  StatusBar,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
  I18nManager,
} from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import AquaticBackground from '../components/ui/AquaticBackground';
import RoleHeader from '../components/ui/RoleHeader';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';

const lessonTypeMap = {
  Private: 'שיעור פרטי',
  Group: 'שיעור קבוצתי',
};

const statusMap = {
  Pending: 'ממתין לתגובת הורים',
  Confirmed: 'אושר ונקבע',
  Cancelled: 'בוטל',
};

const parseIsoDate = (rawDate) => {
  const normalized = String(rawDate || '').trim();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) {
    return null;
  }

  const parsed = new Date(`${normalized}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
};

const formatDateLabel = (rawDate) => {
  const parsed = parseIsoDate(rawDate);
  if (!parsed) {
    return String(rawDate || '').trim();
  }

  return parsed.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
};

const getIsraelTodayIsoDate = () => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jerusalem',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());

  const find = (type) => parts.find((part) => part.type === type)?.value || '';
  const year = find('year');
  const month = find('month');
  const day = find('day');

  if (!year || !month || !day) {
    return '';
  }

  return `${year}-${month}-${day}`;
};

const normalizeStatus = (status) => String(status || '').trim().toLowerCase();

const isLessonExpired = (meetingDateStr, startTimeStr) => {
  if (!meetingDateStr || !startTimeStr) {
    return false;
  }
  try {
    const [year, month, day] = meetingDateStr.split('-').map(Number);
    const [hour, minute] = startTimeStr.split(':').map(Number);
    if (isNaN(year) || isNaN(month) || isNaN(day) || isNaN(hour) || isNaN(minute)) {
      return false;
    }
    const lessonDate = new Date(year, month - 1, day, hour, minute);
    const now = new Date();
    return now >= lessonDate;
  } catch {
    return false;
  }
};

const sortByMeetingDateAndTimeAsc = (a, b) => {
  const aDate = String(a?.meetingDate || '');
  const bDate = String(b?.meetingDate || '');
  if (aDate !== bDate) {
    return aDate.localeCompare(bDate);
  }

  const aStart = String(a?.startTime || '');
  const bStart = String(b?.startTime || '');
  if (aStart !== bStart) {
    return aStart.localeCompare(bStart);
  }

  return Number(a?.invitationId || 0) - Number(b?.invitationId || 0);
};

const sortByMeetingDateAndTimeDesc = (a, b) => sortByMeetingDateAndTimeAsc(b, a);

const getStatusChipStyle = (status) => {
  const normalized = normalizeStatus(status);
  if (normalized === 'confirmed') {
    return {
      backgroundColor: 'rgba(66, 184, 120, 0.2)',
      borderColor: 'rgba(108, 237, 168, 0.45)',
      textColor: '#b9ffd8',
    };
  }

  if (normalized === 'cancelled') {
    return {
      backgroundColor: 'rgba(194, 74, 90, 0.2)',
      borderColor: 'rgba(255, 148, 164, 0.45)',
      textColor: '#ffd0d7',
    };
  }

  return {
    backgroundColor: 'rgba(58, 158, 230, 0.22)',
    borderColor: 'rgba(123, 214, 255, 0.48)',
    textColor: '#d3f4ff',
  };
};

const InvitationCard = ({ item, onCancel, isCancelling }) => {
  const expired = normalizeStatus(item?.status) === 'pending' && isLessonExpired(item?.meetingDate, item?.startTime);
  const displayStatus = expired ? 'Cancelled' : item?.status;
  const statusChip = getStatusChipStyle(displayStatus);
  const isPending = normalizeStatus(item?.status) === 'pending' && !expired;

  return (
    <View style={styles.invitationCard}>
      <View style={styles.invitationHeaderRow}>
        <View
          style={[
            styles.statusChip,
            {
              backgroundColor: statusChip.backgroundColor,
              borderColor: statusChip.borderColor,
            },
          ]}
        >
          <Text style={[styles.statusChipText, { color: statusChip.textColor }]}>
            {statusMap[displayStatus] || displayStatus || 'לא ידוע'}
          </Text>
        </View>

        <View style={styles.invitationTitleWrap}>
          <Text style={styles.invitationTitle}>
            {lessonTypeMap[item?.lessonType] || item?.lessonType || 'שיעור'}
          </Text>
          <Text style={styles.invitationSubtitle}>הזמנה #{item?.invitationId}</Text>
        </View>
      </View>

      <Text style={styles.invitationMeta}>
        תאריך: {formatDateLabel(item?.meetingDate)} | שעות: {item?.startTime || '--:--'}-{item?.endTime || '--:--'}
      </Text>
      <Text style={styles.invitationMeta}>
        מצב הורים: {item?.approvedCount || 0} אישרו | {item?.rejectedCount || 0} דחו | {item?.pendingCount || 0} ממתינים
      </Text>
      {String(item?.targetMetric || '').trim() ? (
        <Text style={styles.invitationMeta}>
          מדד מטרה לשיעור: <Text style={{ fontWeight: '800', color: '#ffd685' }}>{item.targetMetric}</Text>
        </Text>
      ) : null}

      {String(item?.generalNote || '').trim() ? (
        <Text style={styles.invitationNote}>הערת מדריך: {String(item.generalNote).trim()}</Text>
      ) : null}

      {isPending ? (
        <PrimaryButton
          label={isCancelling ? 'מבטל הזמנה...' : 'ביטול הזמנה'}
          onPress={onCancel}
          disabled={isCancelling}
          style={styles.cancelShell}
          gradientStyle={styles.cancelButton}
          textStyle={styles.cancelText}
          colorsOverride={['#C14A5A', '#A53747']}
        />
      ) : null}
    </View>
  );
};

export default function InstructorLessonBoard({ navigation, route }) {
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);

  const [invitations, setInvitations] = useState([]);
  const [isLoadingInvitations, setIsLoadingInvitations] = useState(false);
  const [cancelingInvitationId, setCancelingInvitationId] = useState(null);
  const [errorText, setErrorText] = useState('');
  const [successText, setSuccessText] = useState('');

  const loadInvitations = useCallback(async () => {
    if (!instructorId) {
      setInvitations([]);
      setErrorText('לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }

    try {
      setIsLoadingInvitations(true);
      setErrorText('');

      const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/invitations`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון את לוח השיעורים כרגע.');
      }

      setInvitations(Array.isArray(payload) ? payload : []);
    } catch (error) {
      setInvitations([]);
      setErrorText(error?.message || 'לא ניתן לטעון את לוח השיעורים כרגע.');
    } finally {
      setIsLoadingInvitations(false);
    }
  }, [instructorId]);

  useEffect(() => {
    loadInvitations();
  }, [loadInvitations]);

  useFocusEffect(
    useCallback(() => {
      loadInvitations();
    }, [loadInvitations]),
  );

  const categorized = useMemo(() => {
    const todayIsoDate = getIsraelTodayIsoDate();

    const pending = invitations
      .filter((item) => {
        const isPending = normalizeStatus(item?.status) === 'pending';
        return isPending && !isLessonExpired(item?.meetingDate, item?.startTime);
      })
      .sort(sortByMeetingDateAndTimeAsc);

    const upcoming = invitations
      .filter((item) => normalizeStatus(item?.status) === 'confirmed')
      .filter((item) => {
        const meetingDate = String(item?.meetingDate || '').trim();
        if (!todayIsoDate || !meetingDate) {
          return true;
        }

        return meetingDate >= todayIsoDate;
      })
      .sort(sortByMeetingDateAndTimeAsc);

    const cancelled = invitations
      .filter((item) => {
        const isCancelled = normalizeStatus(item?.status) === 'cancelled';
        if (isCancelled) {
          return true;
        }
        const isPending = normalizeStatus(item?.status) === 'pending';
        return isPending && isLessonExpired(item?.meetingDate, item?.startTime);
      })
      .sort(sortByMeetingDateAndTimeDesc);

    return { pending, upcoming, cancelled };
  }, [invitations]);

  const cancelInvitation = useCallback(async (invitationId) => {
    if (!instructorId || !invitationId) {
      return;
    }

    try {
      setCancelingInvitationId(invitationId);
      setErrorText('');
      setSuccessText('');

      const response = await fetch(
        `${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/invitations/${invitationId}/cancel`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ reason: 'בוטל מתוך לוח השיעורים.' }),
        },
      );

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'ביטול ההזמנה נכשל.');
      }

      setSuccessText(`הזמנה #${invitationId} בוטלה בהצלחה.`);
      await loadInvitations();
    } catch (error) {
      setErrorText(error?.message || 'ביטול ההזמנה נכשל.');
    } finally {
      setCancelingInvitationId((current) => (current === invitationId ? null : current));
    }
  }, [instructorId, loadInvitations]);

  const requestCancelInvitation = useCallback((invitation) => {
    const invitationId = Number(invitation?.invitationId || 0);
    if (!invitationId) {
      return;
    }

    const dateLabel = formatDateLabel(invitation?.meetingDate);
    const timeLabel = `${invitation?.startTime || '--:--'}-${invitation?.endTime || '--:--'}`;

    if (Platform.OS === 'web') {
      const shouldCancel = typeof globalThis.confirm === 'function'
        ? globalThis.confirm(`לבטל את הזמנה #${invitationId}?\n${dateLabel} | ${timeLabel}`)
        : true;

      if (shouldCancel) {
        cancelInvitation(invitationId);
      }

      return;
    }

    Alert.alert(
      'אישור ביטול הזמנה',
      `לבטל את הזמנה #${invitationId}?\n${dateLabel} | ${timeLabel}`,
      [
        { text: 'חזרה', style: 'cancel' },
        {
          text: 'כן, לבטל',
          style: 'destructive',
          onPress: () => cancelInvitation(invitationId),
        },
      ],
      { cancelable: true },
    );
  }, [cancelInvitation]);

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />
      <AquaticBackground variant="instructor" showWaves={false} />

      <RoleHeader
        title="לוח שיעורים"
        onMenuPress={() => navigation.goBack()}
        leftIcon="›"
        rightIcon="↻"
        rightLabel="רענון"
        onRightPress={loadInvitations}
        theme="dark"
      />

      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={styles.summaryPanel}>
          <Text style={styles.summaryTitle}>סטטוס שיעורים והזמנות</Text>
          <Text style={styles.summaryText}>
            מרוכזים כאן כל השיעורים הקרובים וכל ההזמנות במצב ממתין/בוטל.
          </Text>
          <Text style={styles.summaryCounters}>
            קרובים: {categorized.upcoming.length} | ממתינים: {categorized.pending.length} | בוטלו: {categorized.cancelled.length}
          </Text>
        </View>

        {errorText ? <Text style={styles.errorText}>{errorText}</Text> : null}
        {successText ? <Text style={styles.successText}>{successText}</Text> : null}

        <View style={styles.sectionPanel}>
          <Text style={styles.sectionTitle}>שיעורים קרובים (מאושרים)</Text>
          {isLoadingInvitations ? <Text style={styles.stateText}>טוען נתונים...</Text> : null}
          {!isLoadingInvitations && categorized.upcoming.length === 0 ? (
            <Text style={styles.stateText}>אין שיעורים קרובים שאושרו.</Text>
          ) : null}

          {categorized.upcoming.map((item) => (
            <InvitationCard
              key={`upcoming-${item.invitationId}`}
              item={item}
              onCancel={() => requestCancelInvitation(item)}
              isCancelling={cancelingInvitationId === item.invitationId}
            />
          ))}
        </View>

        <View style={styles.sectionPanel}>
          <Text style={styles.sectionTitle}>הזמנות ממתינות</Text>
          {!isLoadingInvitations && categorized.pending.length === 0 ? (
            <Text style={styles.stateText}>אין הזמנות ממתינות כרגע.</Text>
          ) : null}

          {categorized.pending.map((item) => (
            <InvitationCard
              key={`pending-${item.invitationId}`}
              item={item}
              onCancel={() => requestCancelInvitation(item)}
              isCancelling={cancelingInvitationId === item.invitationId}
            />
          ))}
        </View>

        <View style={styles.sectionPanel}>
          <Text style={styles.sectionTitle}>הזמנות שבוטלו</Text>
          {!isLoadingInvitations && categorized.cancelled.length === 0 ? (
            <Text style={styles.stateText}>אין הזמנות שבוטלו להצגה.</Text>
          ) : null}

          {categorized.cancelled.map((item) => (
            <InvitationCard
              key={`cancelled-${item.invitationId}`}
              item={item}
              onCancel={() => requestCancelInvitation(item)}
              isCancelling={cancelingInvitationId === item.invitationId}
            />
          ))}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: '#001529', paddingTop: Platform.OS === 'ios' ? 44 : (Platform.OS === 'android' ? StatusBar.currentHeight : 0) },
  content: { paddingHorizontal: 16, paddingTop: 8, paddingBottom: 48, gap: 12 },
  summaryPanel: {
    backgroundColor: 'rgba(8, 60, 97, 0.66)',
    borderRadius: 16,
    padding: 16,
    borderWidth: 1,
    borderColor: 'rgba(130, 219, 255, 0.35)',
    gap: 8,
  },
  summaryTitle: {
    color: '#e9f9ff',
    fontSize: 17,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  summaryText: {
    color: 'rgba(222, 242, 255, 0.88)',
    fontSize: 13,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  summaryCounters: {
    color: '#8ddfff',
    fontSize: 13,
    fontWeight: '700',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  sectionPanel: {
    backgroundColor: 'rgba(5, 54, 90, 0.68)',
    borderRadius: 16,
    padding: 16,
    borderWidth: 1,
    borderColor: 'rgba(125, 212, 255, 0.28)',
    gap: 8,
  },
  sectionTitle: {
    color: '#e3f8ff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  stateText: {
    color: 'rgba(219, 240, 255, 0.84)',
    fontSize: 13,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  invitationCard: {
    backgroundColor: 'rgba(23, 99, 151, 0.6)',
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(144, 223, 255, 0.28)',
    padding: 12,
    gap: 6,
  },
  invitationHeaderRow: {
    flexDirection: I18nManager.isRTL ? 'row' : 'row-reverse',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 10,
  },
  invitationTitleWrap: {
    flex: 1,
    alignItems: I18nManager.isRTL ? 'flex-end' : 'flex-start',
  },
  invitationTitle: {
    color: '#f2fbff',
    fontSize: 14,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  invitationSubtitle: {
    color: 'rgba(225, 245, 255, 0.78)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  statusChip: {
    borderRadius: 999,
    borderWidth: 1,
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  statusChipText: {
    fontSize: 11,
    fontWeight: '800',
    writingDirection: 'rtl',
  },
  invitationMeta: {
    color: 'rgba(224, 244, 255, 0.9)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  invitationNote: {
    color: '#d7f3ff',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  cancelShell: { marginTop: 6 },
  cancelButton: { paddingVertical: 10 },
  cancelText: { color: '#FFFFFF', fontSize: 14, fontWeight: '700' },
  errorText: {
    color: '#ffc6d0',
    fontSize: 14,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  successText: {
    color: '#95f2cc',
    fontSize: 14,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
});

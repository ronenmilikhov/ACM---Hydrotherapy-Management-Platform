import React, { useRef, useEffect, useState, useMemo, useCallback } from 'react';
import {
  Alert, StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  TextInput, StatusBar, Easing, Platform, ScrollView, Modal,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import PrimaryButton from '../components/ui/PrimaryButton';
import AppCard from '../components/ui/AppCard';
import { colors, shadows } from '../theme/tokens';
import { API_BASE_URL } from '../apiConfig';
import { registerPushNotificationsForUser, unregisterPushNotificationsForUser } from '../notifications/pushNotifications';

const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);

function clamp(min, val, max) {
  return Math.min(Math.max(val, min), max);
}

// ========================================
// 1. SEAMLESS ANIMATED WAVES
// ========================================
const Wave = ({ color, duration, startPosition, d }) => {
  const translateX = useRef(new Animated.Value(startPosition)).current;

  useEffect(() => {
    const toPosition = startPosition === 0 ? -600 : 0;
    Animated.loop(
      Animated.sequence([
        Animated.timing(translateX, {
          toValue: toPosition,
          duration: duration / 2,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
        Animated.timing(translateX, {
          toValue: startPosition,
          duration: duration / 2,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
      ]),
    ).start();
  }, [duration, startPosition]);

  return (
    <Animated.View style={[styles.waveContainer, { transform: [{ translateX }] }]}>
      <Svg width={2400} height={150} viewBox="0 0 2400 150" preserveAspectRatio="none">
        <Path fill={color} d={d} />
      </Svg>
    </Animated.View>
  );
};

// ========================================
// 2. TRULY INFINITE REINCARNATING BUBBLES
// ========================================
const Bubble = () => {
  const progress = useRef(new Animated.Value(0)).current;
  const sway = useRef(new Animated.Value(0)).current;
  const [config, setConfig] = useState({
    size: Math.random() * 30 + 10,
    left: Math.random() * width,
  });

  const durationRef = useRef(Math.random() * 16000 + 14000);
  const isFirstRun = useRef(true);

  const startFloat = () => {
    progress.setValue(0);
    const delay = isFirstRun.current ? Math.random() * 30000 : 0;
    isFirstRun.current = false;
    Animated.sequence([
      Animated.delay(delay),
      Animated.timing(progress, {
        toValue: 1,
        duration: durationRef.current,
        easing: Easing.linear,
        useNativeDriver: true,
      }),
    ]).start(({ finished }) => {
      if (finished) {
        durationRef.current = Math.random() * 16000 + 14000;
        setConfig({
          size: Math.random() * 30 + 10,
          left: Math.random() * width,
        });
        startFloat();
      }
    });
  };

  useEffect(() => {
    startFloat();
    const swayAnim = Animated.loop(
      Animated.sequence([
        Animated.timing(sway, { toValue: 1, duration: 2000, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(sway, { toValue: -1, duration: 4000, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(sway, { toValue: 0, duration: 2000, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      ]),
    );
    swayAnim.start();
    return () => { progress.stopAnimation(); sway.stopAnimation(); };
  }, []);

  const translateY = progress.interpolate({ inputRange: [0, 1], outputRange: [height + 50, -150] });
  const opacity = progress.interpolate({ inputRange: [0, 0.1, 0.8, 1], outputRange: [0, 1, 1, 0] });
  const translateX = sway.interpolate({ inputRange: [-1, 1], outputRange: [-20, 20] });

  return (
    <Animated.View
      style={[
        styles.bubbleWrapper,
        {
          width: config.size,
          height: config.size,
          borderRadius: config.size / 2,
          left: config.left,
          transform: [{ translateY }, { translateX }],
          opacity,
        },
      ]}
    >
      <LinearGradient
        colors={["rgba(255, 255, 255, 0.95)", "rgba(255, 255, 255, 0.1)"]}
        style={{
          flex: 1,
          borderRadius: config.size / 2,
          borderWidth: 1,
          borderColor: "rgba(255, 255, 255, 0.4)",
        }}
      />
    </Animated.View>
  );
};

// ========================================
// 3. SUB-COMPONENTS
// ========================================
const GroupRow = ({ title, time, onCancel, onStartChat, canStartChat, canCancel, isCancelling }) => (
  <AppCard useBlur blurIntensity={20} blurTint="dark" style={styles.groupRow}>
    <View style={styles.groupRowActions}>
      <TouchableOpacity
        style={[
          styles.chatButton,
          !canStartChat && styles.chatButtonDisabled,
        ]}
        activeOpacity={0.8}
        onPress={onStartChat}
        disabled={!canStartChat}
      >
        <Text
          style={[
            styles.chatButtonText,
            !canStartChat && styles.chatButtonTextDisabled,
          ]}
        >
          צ׳אט
        </Text>
      </TouchableOpacity>

      <TouchableOpacity
        style={[
          styles.cancelButton,
          (!canCancel || isCancelling) && styles.cancelButtonDisabled,
        ]}
        activeOpacity={0.8}
        onPress={onCancel}
        disabled={!canCancel || isCancelling}
      >
        <Text
          style={[
            styles.cancelButtonText,
            (!canCancel || isCancelling) && styles.cancelButtonTextDisabled,
          ]}
        >
          {isCancelling ? 'מבטל...' : 'ביטול'}
        </Text>
      </TouchableOpacity>
    </View>

    <View style={styles.groupTextWrap}>
      <Text style={styles.groupTitle}>{title}</Text>
      <Text style={styles.groupSubtitle}>{time}</Text>
    </View>
  </AppCard>
);

// ========================================
// 4. MAIN COMPONENT
// ========================================
const DAY_LABELS = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת'];

const formatMeetingDateLabel = (rawDate) => {
  const normalized = String(rawDate || '').trim();
  if (!normalized) {
    return '';
  }

  const parsedDate = new Date(`${normalized}T00:00:00`);
  if (Number.isNaN(parsedDate.getTime())) {
    return normalized;
  }

  return parsedDate.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
};

export default function InstructorHomepage({ route }) {
  const navigation = useNavigation();
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);
  const instructorDisplayName = String(
    authUser?.fullName
      || `${authUser?.firstName || ''} ${authUser?.lastName || ''}`,
  ).trim() || 'מדריך';
  const scrollY = useRef(new Animated.Value(0)).current;
  const [classGroups, setClassGroups] = useState([]);
  const [showCreateGroupModal, setShowCreateGroupModal] = useState(false);
  const [showBroadcastModal, setShowBroadcastModal] = useState(false);
  const [isLoadingClassGroups, setIsLoadingClassGroups] = useState(false);
  const [classGroupsError, setClassGroupsError] = useState('');
  const [broadcastMessage, setBroadcastMessage] = useState('');
  const [isSendingBroadcast, setIsSendingBroadcast] = useState(false);
  const [isCheckingBroadcastRecipients, setIsCheckingBroadcastRecipients] = useState(false);
  const [newGroupName, setNewGroupName] = useState('קבוצת דגים');
  const [newLessonTime, setNewLessonTime] = useState('12:00 - 12:30');
  const [newAge, setNewAge] = useState('10');
  const [newParticipants, setNewParticipants] = useState('5');
  const [cancelingSessionId, setCancelingSessionId] = useState(null);
  const [unreadNotificationCount, setUnreadNotificationCount] = useState(0);

  const bubblePool = useMemo(() => Array.from({ length: 6 }).map((_, i) => i), []);

  const surfaceOpacity = scrollY.interpolate({
    inputRange: [0, height * 0.8],
    outputRange: [1, 0],
    extrapolate: 'clamp',
  });

  const navBg = scrollY.interpolate({
    inputRange: [0, 50],
    outputRange: ['rgba(0, 21, 41, 0.4)', 'rgba(0, 21, 41, 0.85)'],
    extrapolate: 'clamp',
  });

  useEffect(() => {
    if (!instructorId) {
      return;
    }

    registerPushNotificationsForUser({
      userType: 'Instructor',
      userId: instructorId,
    }).then((result) => {
      if (!result?.ok) {
        console.log('[push] instructor registration skipped', {
          userId: instructorId,
          reason: result?.reason || 'unknown',
        });
      }
    }).catch((error) => {
      console.warn('[push] instructor registration failed', {
        userId: instructorId,
        error: String(error?.message || error),
      });
    });
  }, [instructorId]);

  const loadUnreadNotifications = useCallback(async () => {
    if (!instructorId) {
      setUnreadNotificationCount(0);
      return;
    }

    try {
      const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/notifications`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון התראות כרגע.');
      }

      const notifications = Array.isArray(payload) ? payload : [];
      const unreadCount = notifications.reduce(
        (total, item) => total + (item?.isRead ? 0 : 1),
        0,
      );

      setUnreadNotificationCount(unreadCount);
    } catch (_error) {
      setUnreadNotificationCount(0);
    }
  }, [instructorId]);

  useEffect(() => {
    loadUnreadNotifications();
  }, [loadUnreadNotifications]);

  useFocusEffect(
    useCallback(() => {
      loadUnreadNotifications();
    }, [loadUnreadNotifications]),
  );

  useEffect(() => {
    if (!instructorId) {
      return undefined;
    }

    const intervalId = setInterval(() => {
      loadUnreadNotifications();
    }, 10000);

    return () => {
      clearInterval(intervalId);
    };
  }, [instructorId, loadUnreadNotifications]);

  const createGroup = () => {
    if (!newGroupName.trim()) { alert('נא להזין שם קבוצה'); return; }
    setClassGroups((prev) => [...prev, { name: newGroupName, time: newLessonTime }]);
    setShowCreateGroupModal(false);
  };

  const openSessionChat = useCallback((session) => {
    const parentId = Number(session?.parentId || 0);
    const childId = Number(session?.childId || 0);

    if (!instructorId || !parentId || !childId) {
      alert('לא ניתן לפתוח צ׳אט עבור אימון זה כרגע.');
      return;
    }

    navigation.navigate('ChatPage', {
      fromInstructor: true,
      instructorId,
      parentId,
      childId,
      instructorName: instructorDisplayName,
      parentName: String(session?.parentName || '').trim() || 'הורה',
      childName: String(session?.name || '').trim(),
    });
  }, [navigation, instructorId, instructorDisplayName]);

  const openNotificationsInbox = useCallback(() => {
    navigation.navigate('InstructorNotificationsInbox', { authUser });
  }, [navigation, authUser]);

  const loadWeeklyTrainingSessions = useCallback(async () => {
    if (!instructorId) {
      setClassGroups([]);
      setClassGroupsError('לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }

    try {
      setIsLoadingClassGroups(true);
      setClassGroupsError('');

      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/training-sessions`);
      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון שיעורים שנקבעו כרגע.');
      }

      const groupsFromSessions = (Array.isArray(payload) ? payload : [])
        .map((session) => {
          const childId = Number(session?.childId || 0);
          const parentId = Number(session?.parentId || 0);
          const startTime = String(session?.startTime || '').slice(0, 5);
          const endTime = String(session?.endTime || '').slice(0, 5);
          const weekday = String(session?.weekday || '').trim();
          const dateLabel = formatMeetingDateLabel(session?.meetingDate);
          const childName = String(session?.childName || '').trim() || `ילד #${childId || ''}`;

          return {
            id: Number(session?.sessionId || 0),
            name: childName,
            childId,
            parentId,
            parentName: String(session?.parentName || '').trim(),
            time: `${weekday} ${dateLabel} ${startTime} - ${endTime}`.trim(),
            weekdayOrder: Number(session?.weekdayOrder || 0),
            meetingDate: String(session?.meetingDate || ''),
            startTime,
          };
        })
        .sort((a, b) => {
          if (a.meetingDate !== b.meetingDate) return a.meetingDate.localeCompare(b.meetingDate);
          return a.startTime.localeCompare(b.startTime);
        });

      setClassGroups(groupsFromSessions);
    } catch (error) {
      setClassGroups([]);
      setClassGroupsError(error?.message || 'לא ניתן לטעון שיעורים שנקבעו כרגע.');
    } finally {
      setIsLoadingClassGroups(false);
    }
  }, [instructorId]);

  useFocusEffect(
    useCallback(() => {
      loadWeeklyTrainingSessions();
    }, [loadWeeklyTrainingSessions]),
  );

  const cancelTrainingSession = useCallback(async (session) => {
    const sessionId = Number(session?.id || 0);
    if (!instructorId || !sessionId) {
      alert('לא ניתן לבטל את האימון כרגע.');
      return;
    }

    try {
      setCancelingSessionId(sessionId);

      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/training-sessions/${sessionId}/cancel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: '' }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לבטל את האימון כרגע.');
      }

      Alert.alert('האימון בוטל', 'האימון בוטל בהצלחה וההורה עודכן בהתראות.');
      await loadWeeklyTrainingSessions();
    } catch (error) {
      Alert.alert('שגיאה', String(error?.message || 'לא ניתן לבטל את האימון כרגע.'));
    } finally {
      setCancelingSessionId((currentId) => (currentId === sessionId ? null : currentId));
    }
  }, [instructorId, loadWeeklyTrainingSessions]);

  const requestTrainingCancellation = useCallback((session) => {
    const sessionId = Number(session?.id || 0);
    if (!sessionId) {
      Alert.alert('לא ניתן לבטל', 'לא ניתן לזהות את האימון לביטול.');
      return;
    }

    if (cancelingSessionId === sessionId) {
      return;
    }

    const childName = String(session?.name || '').trim() || 'הילד';
    const timeLabel = String(session?.time || '').trim();

    Alert.alert(
      'אישור ביטול אימון',
      `האם לבטל את האימון של ${childName}?${timeLabel ? `\n${timeLabel}` : ''}\n\nההורה יקבל התראה על הביטול.`,
      [
        { text: 'חזרה', style: 'cancel' },
        {
          text: 'כן, בטל',
          style: 'destructive',
          onPress: () => {
            cancelTrainingSession(session);
          },
        },
      ],
      { cancelable: true },
    );
  }, [cancelTrainingSession, cancelingSessionId]);

  const getBroadcastRecipientParentCount = useCallback(async () => {
    const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/recipients`);
    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      throw new Error(payload?.message || 'לא ניתן לבדוק הורים פעילים כרגע.');
    }

    const recipients = Array.isArray(payload) ? payload : [];
    const uniqueParentIds = new Set(
      recipients
        .map((item) => Number(item?.parentId || 0))
        .filter((parentId) => parentId > 0),
    );

    return uniqueParentIds.size;
  }, [instructorId]);

  const openBroadcastModal = useCallback(async () => {
    if (!instructorId) {
      Alert.alert('שגיאה', 'לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }

    try {
      setIsCheckingBroadcastRecipients(true);

      const parentRecipientsCount = await getBroadcastRecipientParentCount();
      if (parentRecipientsCount <= 0) {
        Alert.alert('לא ניתן לשדר הודעה', 'לא נמצאו הורים פעילים בקבוצות המשויכות אליך כרגע.');
        return;
      }

      setShowBroadcastModal(true);
    } catch (error) {
      Alert.alert('שגיאה', String(error?.message || 'לא ניתן לבדוק הורים פעילים כרגע.'));
    } finally {
      setIsCheckingBroadcastRecipients(false);
    }
  }, [getBroadcastRecipientParentCount, instructorId]);

  const sendBroadcastToAssignedParents = useCallback(async () => {
    const message = String(broadcastMessage || '').trim();
    if (!message) {
      Alert.alert('שגיאה', 'נא להזין הודעה לשליחה.');
      return;
    }

    if (!instructorId) {
      Alert.alert('שגיאה', 'לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }

    try {
      const parentRecipientsCount = await getBroadcastRecipientParentCount();
      if (parentRecipientsCount <= 0) {
        Alert.alert('לא ניתן לשלוח שידור', 'לא נמצאו הורים פעילים בקבוצות המשויכות אליך כרגע.');
        setShowBroadcastModal(false);
        setBroadcastMessage('');
        return;
      }

      setIsSendingBroadcast(true);

      const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/broadcast`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          instructorName: instructorDisplayName,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לשלוח את ההודעה כרגע.');
      }

      const sentCount = Number(payload?.sentCount || 0);
      if (sentCount <= 0) {
        Alert.alert('לא ניתן לשלוח שידור', 'לא נמצאו הורים פעילים בקבוצות המשויכות אליך כרגע.');
        setShowBroadcastModal(false);
        setBroadcastMessage('');
        return;
      }

      Alert.alert('השידור נשלח', `ההודעה נשלחה ל-${sentCount} הורים של הקבוצות שלך.`);

      setBroadcastMessage('');
      setShowBroadcastModal(false);
    } catch (error) {
      Alert.alert('שגיאה', String(error?.message || 'לא ניתן לשלוח את ההודעה כרגע.'));
    } finally {
      setIsSendingBroadcast(false);
    }
  }, [broadcastMessage, getBroadcastRecipientParentCount, instructorDisplayName, instructorId]);

  const performDisconnect = useCallback(async () => {
    if (instructorId) {
      await unregisterPushNotificationsForUser({
        userType: 'Instructor',
        userId: instructorId,
      });
    }

    navigation.reset({
      index: 0,
      routes: [{ name: 'Login' }],
    });
  }, [navigation, instructorId]);

  const handleDisconnect = useCallback(() => {
    if (Platform.OS === 'web') {
      const shouldDisconnect = typeof globalThis.confirm === 'function'
        ? globalThis.confirm('האם אתה בטוח שברצונך להתנתק?')
        : true;

      if (shouldDisconnect) {
        performDisconnect();
      }

      return;
    }

    Alert.alert(
      'התנתקות',
      'האם אתה בטוח שברצונך להתנתק?',
      [
        { text: 'ביטול', style: 'cancel' },
        {
          text: 'התנתקות',
          style: 'destructive',
          onPress: performDisconnect,
        },
      ],
      { cancelable: true },
    );
  }, [performDisconnect]);

  const todayDate = new Date().toLocaleDateString('he-IL', {
    day: '2-digit', month: '2-digit', year: 'numeric',
  });

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent={true} />

      {/* Deep Ocean Base */}
      <LinearGradient colors={['#005a80', '#00456a', colors.bgDeep]} style={StyleSheet.absoluteFill} />

      {/* Surface Water */}
      <Animated.View style={[StyleSheet.absoluteFill, { opacity: surfaceOpacity }]}>
        <LinearGradient colors={['#00aed8', '#007fa7', '#004d73']} style={StyleSheet.absoluteFill} />
      </Animated.View>

      {/* Bubbles */}
      {bubblePool.map((id) => <Bubble key={id} />)}

      {/* Bottom Waves */}
      <Wave
        color="rgba(0, 153, 204, 0.3)"
        duration={8000}
        startPosition={0}
        d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z"
      />
      <Wave
        color="rgba(0, 212, 255, 0.25)"
        duration={6000}
        startPosition={-600}
        d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z"
      />

      {/* NAVBAR */}
      <AnimatedBlurView intensity={20} tint="dark" style={[styles.navbar, { backgroundColor: navBg }]}>
        <TouchableOpacity onPress={handleDisconnect} style={styles.disconnectBtn} activeOpacity={0.8}>
          <Text style={styles.disconnectBtnText}>התנתקות</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.navBellWrap}
          activeOpacity={0.7}
          onPress={openNotificationsInbox}
        >
          <View style={styles.navBellContent}>
            <Text style={styles.navBellLabel}>התראות</Text>

            <View style={styles.navBellIconWrap}>
              <Text style={styles.navBell}>🔔</Text>

              {unreadNotificationCount > 0 ? (
                <View style={styles.navBadge}>
                  <Text style={styles.navBadgeText}>{unreadNotificationCount > 99 ? '99+' : unreadNotificationCount}</Text>
                </View>
              ) : null}
            </View>
          </View>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* MAIN SCROLL CONTENT */}
      <Animated.ScrollView
        onScroll={Animated.event(
          [{ nativeEvent: { contentOffset: { y: scrollY } } }],
          { useNativeDriver: false },
        )}
        scrollEventThrottle={16}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* HERO SECTION */}
        <View style={styles.hero}>
          <Text style={styles.heroTitle}>{`שלום, ${instructorDisplayName} 👋`}</Text>
          <Text style={styles.dateText}>📅 {todayDate}</Text>
        </View>

        {/* GROUPS & LESSONS SECTION */}
        <View style={styles.section}>
          <View style={styles.sectionHeader}>
            <Text style={styles.sectionTitle}>שיעורים שנקבעו</Text>
            <Text style={styles.sectionSubtitle}>מוצגים רק שיעורים שנקבעו בפועל</Text>
          </View>

          {isLoadingClassGroups ? (
            <Text style={styles.listStateText}>טוען שיעורים שנקבעו...</Text>
          ) : classGroupsError ? (
            <Text style={styles.listStateText}>{classGroupsError}</Text>
          ) : classGroups.length === 0 ? (
            <Text style={styles.listStateText}>אין שיעורים שנקבעו לשבוע הקרוב.</Text>
          ) : (
            classGroups.map((group) => (
              <GroupRow
                key={String(group.id || `${group.name}-${group.time}`)}
                title={group.name}
                time={group.time}
                onCancel={() => requestTrainingCancellation(group)}
                onStartChat={() => openSessionChat(group)}
                canStartChat={group.parentId > 0 && group.childId > 0}
                canCancel={group.id !== 0}
                isCancelling={cancelingSessionId === group.id}
              />
            ))
          )}
        </View>

        {/* ACTIONS */}
        <View style={styles.section}>
          <PrimaryButton
            label="תיבת צ׳אטים"
            style={styles.actionBtn}
            gradientStyle={styles.btnGradient}
            textStyle={styles.actionBtnText}
            onPress={() => navigation.navigate('InstructorConversationInbox', { authUser })}
          />
          <PrimaryButton
            label="קביעת שיעור"
            style={styles.actionBtn}
            gradientStyle={styles.btnGradient}
            textStyle={styles.actionBtnText}
            onPress={() => navigation.navigate('InstructorLessonScheduler', { authUser })}
          />
          <PrimaryButton
            label="לוח שיעורים"
            style={styles.actionBtn}
            gradientStyle={styles.btnGradient}
            textStyle={styles.actionBtnText}
            onPress={() => navigation.navigate('InstructorLessonBoard', { authUser })}
          />
          <PrimaryButton
            label="הצג את כלל הקבוצות"
            style={styles.actionBtn}
            gradientStyle={styles.btnGradient}
            textStyle={styles.actionBtnText}
            onPress={() => navigation.navigate('SelectGroup', { authUser })}
          />
          <PrimaryButton
            label={isCheckingBroadcastRecipients ? 'בודק הורים...' : 'שדר הודעה'}
            style={styles.actionBtn}
            gradientStyle={styles.btnGradient}
            textStyle={styles.actionBtnText}
            onPress={openBroadcastModal}
            disabled={isCheckingBroadcastRecipients || isSendingBroadcast}
          />
        </View>

        {/* FOOTER */}
        <View style={styles.footer}>
          <View style={styles.footerBrand}>
            <Text style={styles.footerEmoji}>🐚</Text>
            <Text style={styles.footerBrandText}>ACM</Text>
          </View>
          <Text style={styles.footerText}>לוח מדריך — ניהול שיעורים בקלות</Text>
          <Text style={styles.copyright}>כל הזכויות שמורות ל-ACM 2026 ©</Text>
        </View>
      </Animated.ScrollView>

      {/* CREATE GROUP MODAL */}
      <Modal visible={showCreateGroupModal} transparent animationType="slide">
        <View style={styles.modalOverlay}>
          <AppCard useBlur blurIntensity={40} blurTint="dark" style={styles.modalCard}>
            <Text style={styles.modalTitle}>יצירת קבוצה חדשה</Text>

            <Text style={styles.modalLabel}>שם קבוצה</Text>
            <TextInput
              value={newGroupName}
              onChangeText={setNewGroupName}
              style={styles.modalInput}
              placeholderTextColor="rgba(255,255,255,0.4)"
            />

            <Text style={styles.modalLabel}>זמן שיעור</Text>
            <TextInput
              value={newLessonTime}
              onChangeText={setNewLessonTime}
              placeholder="12:00 - 12:30"
              placeholderTextColor="rgba(255,255,255,0.4)"
              style={styles.modalInput}
            />

            <Text style={styles.modalLabel}>גיל</Text>
            <TextInput
              value={newAge}
              onChangeText={(v) => setNewAge(v.replace(/[^0-9]/g, ''))}
              keyboardType="numeric"
              style={styles.modalInput}
              placeholderTextColor="rgba(255,255,255,0.4)"
            />

            <Text style={styles.modalLabel}>מספר משתתפים</Text>
            <View style={styles.participantsRow}>
              {['5','6','7','8','9','10','11','12'].map((n) => (
                <TouchableOpacity
                  key={n}
                  onPress={() => setNewParticipants(n)}
                  style={[
                    styles.participantChip,
                    newParticipants === n && styles.participantChipActive,
                  ]}
                >
                  <Text style={[
                    styles.participantText,
                    newParticipants === n && styles.participantTextActive,
                  ]}>{n}</Text>
                </TouchableOpacity>
              ))}
            </View>

            <View style={styles.modalActions}>
              <TouchableOpacity
                onPress={() => setShowCreateGroupModal(false)}
                style={styles.modalCancelBtn}
              >
                <Text style={styles.modalCancelText}>ביטול</Text>
              </TouchableOpacity>
              <TouchableOpacity onPress={createGroup} style={styles.modalSaveBtn}>
                <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.modalSaveBtnGradient}>
                  <Text style={styles.modalSaveText}>שמור</Text>
                </LinearGradient>
              </TouchableOpacity>
            </View>
          </AppCard>
        </View>
      </Modal>

      {/* BROADCAST MODAL */}
      <Modal visible={showBroadcastModal} transparent animationType="fade">
        <View style={styles.modalOverlay}>
          <AppCard useBlur blurIntensity={40} blurTint="dark" style={styles.modalCard}>
            <Text style={styles.modalTitle}>שידור הודעה לכל הקבוצות</Text>
            <TextInput
              value={broadcastMessage}
              onChangeText={setBroadcastMessage}
              placeholder="הקלד הודעה..."
              placeholderTextColor="rgba(255,255,255,0.4)"
              multiline
              style={[styles.modalInput, { minHeight: 100, textAlignVertical: 'top' }]}
            />
            <View style={styles.modalActions}>
              <TouchableOpacity
                onPress={() => setShowBroadcastModal(false)}
                style={[styles.modalCancelBtn, isSendingBroadcast && styles.modalButtonDisabled]}
                disabled={isSendingBroadcast}
              >
                <Text style={styles.modalCancelText}>ביטול</Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={sendBroadcastToAssignedParents}
                style={[styles.modalSaveBtn, isSendingBroadcast && styles.modalButtonDisabled]}
                disabled={isSendingBroadcast}
              >
                <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.modalSaveBtnGradient}>
                  <Text style={styles.modalSaveText}>{isSendingBroadcast ? 'שולח...' : 'שלח'}</Text>
                </LinearGradient>
              </TouchableOpacity>
            </View>
          </AppCard>
        </View>
      </Modal>
    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  scrollContent: { paddingTop: 110, paddingBottom: 60 },
  waveContainer: {
    position: 'absolute', bottom: 0, left: 0,
    width: 2400, height: 150, zIndex: 0,
  },
  bubbleWrapper: { position: 'absolute', zIndex: 1 },

  // --- NAVBAR ---
  navbar: {
    position: 'absolute', top: 0, left: 0, right: 0,
    paddingTop: Platform.OS === 'ios' ? 50 : (StatusBar.currentHeight || 20) + 15,
    paddingBottom: 15, paddingHorizontal: 20,
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    zIndex: 100,
    borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  navBrand: { flexDirection: 'row', alignItems: 'center' },
  logoIcon: {
    width: 34, height: 34, borderRadius: 17,
    justifyContent: 'center', alignItems: 'center', marginLeft: 10,
  },
  logoEmoji: { fontSize: 18, includeFontPadding: false },
  logoText: {
    color: '#fff', fontSize: 22, fontWeight: '900',
    includeFontPadding: false, letterSpacing: 1,
  },
  disconnectBtn: {
    backgroundColor: 'rgba(255, 255, 255, 0.1)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.28)',
    borderRadius: 14,
    paddingHorizontal: 12,
    paddingVertical: 8,
  },
  disconnectBtnText: {
    color: '#ffffff',
    fontWeight: '700',
    fontSize: 13,
    writingDirection: 'rtl',
  },
  navBellWrap: {
    minWidth: 88,
    height: 40,
    paddingHorizontal: 6,
    justifyContent: 'center',
    alignItems: 'center',
    position: 'relative',
    overflow: 'visible',
  },
  navBellContent: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  navBellLabel: {
    color: 'rgba(255,255,255,0.9)',
    fontSize: 13,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  navBellIconWrap: {
    width: 24,
    height: 24,
    alignItems: 'center',
    justifyContent: 'center',
    position: 'relative',
    overflow: 'visible',
  },
  navBell: { fontSize: 20 },
  navBadge: {
    position: 'absolute',
    top: -7,
    right: -9,
    minWidth: 18,
    height: 18,
    borderRadius: 999,
    paddingHorizontal: 4,
    backgroundColor: '#ef4444',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.45)',
    justifyContent: 'center',
    alignItems: 'center',
    zIndex: 3,
    elevation: 3,
  },
  navBadgeText: {
    color: '#fff',
    fontSize: 10,
    fontWeight: '800',
    lineHeight: 12,
    textAlign: 'center',
  },

  // --- HERO ---
  hero: {
    alignItems: 'center', justifyContent: 'center',
    paddingHorizontal: '5%', paddingVertical: 30, zIndex: 10,
  },
  heroTitle: {
    fontSize: clamp(28, width * 0.08, 38),
    fontWeight: '800', color: '#fff',
    textAlign: 'center', writingDirection: 'rtl',
  },
  gradientText: {
    color: '#00d4ff',
    textShadowColor: 'rgba(0, 212, 255, 0.5)',
    textShadowOffset: { width: 0, height: 0 },
    textShadowRadius: 20,
  },
  heroSubtitle: {
    fontSize: 16,
    fontWeight: '300',
    textAlign: 'center',
    marginTop: 8,
    writingDirection: 'rtl',
  },
  dateText: {
    color: 'rgba(255,255,255,0.7)', fontSize: 15,
    marginTop: 14, fontWeight: '500', writingDirection: 'rtl',
  },

  // --- SECTIONS ---
  section: { marginTop: 30, paddingHorizontal: '5%', zIndex: 10 },
  sectionHeader: { alignItems: 'center', marginBottom: 20 },
  sectionTitle: {
    fontSize: 26, fontWeight: '700', color: '#fff',
    marginBottom: 6, writingDirection: 'rtl', textAlign: 'center',
  },
  sectionSubtitle: {
    color: 'rgba(255,255,255,0.6)', fontSize: 13,
    textAlign: 'center', writingDirection: 'rtl',
  },
  listStateText: {
    color: 'rgba(255,255,255,0.76)',
    textAlign: 'center',
    writingDirection: 'rtl',
    marginBottom: 12,
    fontSize: 14,
  },

  // --- GROUP ROWS ---
  groupRow: {
    borderRadius: 20, padding: 16, marginBottom: 12,
    backgroundColor: 'rgba(255, 255, 255, 0.08)',
    borderWidth: 1, borderColor: 'rgba(255, 255, 255, 0.15)',
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    overflow: 'hidden',
  },
  groupRowActions: {
    width: 92,
  },
  chatButton: {
    backgroundColor: 'rgba(0, 212, 255, 0.24)',
    borderRadius: 14,
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.5)',
    marginBottom: 8,
  },
  chatButtonDisabled: {
    backgroundColor: 'rgba(255, 255, 255, 0.12)',
    borderColor: 'rgba(255, 255, 255, 0.2)',
  },
  chatButtonText: {
    color: '#8ceaff',
    fontWeight: '700',
    fontSize: 13,
    writingDirection: 'rtl',
    textAlign: 'center',
  },
  chatButtonTextDisabled: {
    color: 'rgba(255,255,255,0.45)',
  },
  cancelButton: {
    backgroundColor: 'rgba(231, 76, 60, 0.25)',
    borderRadius: 14, paddingHorizontal: 14, paddingVertical: 8,
    borderWidth: 1, borderColor: 'rgba(231, 76, 60, 0.4)',
  },
  cancelButtonDisabled: {
    backgroundColor: 'rgba(255, 255, 255, 0.12)',
    borderColor: 'rgba(255, 255, 255, 0.2)',
  },
  cancelButtonText: {
    color: '#ff6b6b', fontWeight: '700', fontSize: 13, writingDirection: 'rtl', textAlign: 'center',
  },
  cancelButtonTextDisabled: {
    color: 'rgba(255,255,255,0.45)',
  },
  groupTextWrap: { alignItems: 'flex-end', flex: 1, marginRight: 12 },
  groupTitle: { color: '#fff', fontSize: 17, fontWeight: '700', writingDirection: 'rtl' },
  groupSubtitle: { color: 'rgba(255,255,255,0.55)', fontSize: 13, marginTop: 3, writingDirection: 'rtl' },

  // --- ACTION BUTTONS ---
  actionBtn: {
    width: '100%', borderRadius: 50, overflow: 'hidden',
    marginBottom: 12, ...shadows.glowPrimary,
  },
  btnGradient: { paddingVertical: 16, alignItems: 'center', justifyContent: 'center' },
  actionBtnText: { color: '#001529', fontWeight: 'bold', fontSize: 16 },

  // --- FOOTER ---
  footer: {
    marginTop: 60, paddingTop: 30, paddingBottom: 20,
    borderTopWidth: 1, borderTopColor: 'rgba(255,255,255,0.1)',
    alignItems: 'center', zIndex: 10,
  },
  footerBrand: { flexDirection: 'row', alignItems: 'center', gap: 10, marginBottom: 12 },
  footerEmoji: { fontSize: 24 },
  footerBrandText: { color: '#fff', fontSize: 22, fontWeight: '600' },
  footerText: { color: 'rgba(255,255,255,0.6)', fontSize: 14, marginBottom: 8, textAlign: 'center', writingDirection: 'rtl' },
  copyright: { color: 'rgba(255,255,255,0.4)', fontSize: 12 },

  // --- MODALS ---
  modalOverlay: {
    flex: 1, backgroundColor: 'rgba(0,15,30,0.75)',
    justifyContent: 'center', padding: 20,
  },
  modalCard: {
    borderRadius: 28, padding: 28,
    backgroundColor: 'rgba(0, 40, 70, 0.85)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
    overflow: 'hidden',
  },
  modalTitle: {
    fontSize: 22, fontWeight: '800', color: '#fff',
    marginBottom: 20, textAlign: 'center', writingDirection: 'rtl',
  },
  modalLabel: {
    color: 'rgba(255,255,255,0.6)', fontSize: 13, fontWeight: '600',
    marginBottom: 8, writingDirection: 'rtl', textAlign: 'right',
  },
  modalInput: {
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderRadius: 16, padding: 14, marginBottom: 16,
    color: '#fff', fontSize: 15,
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.12)',
    writingDirection: 'rtl', textAlign: 'right',
  },
  participantsRow: {
    flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginBottom: 20,
    justifyContent: 'center',
  },
  participantChip: {
    paddingVertical: 10, paddingHorizontal: 16, borderRadius: 14,
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.12)',
  },
  participantChipActive: {
    backgroundColor: 'rgba(0,212,255,0.25)',
    borderColor: '#00d4ff',
  },
  participantText: { color: 'rgba(255,255,255,0.6)', fontWeight: '700', fontSize: 14 },
  participantTextActive: { color: '#00d4ff' },
  modalActions: {
    flexDirection: 'row', justifyContent: 'space-between', gap: 12, marginTop: 8,
  },
  modalButtonDisabled: { opacity: 0.58 },
  modalCancelBtn: {
    flex: 1, borderRadius: 18, paddingVertical: 14,
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
    alignItems: 'center', justifyContent: 'center',
  },
  modalCancelText: { color: 'rgba(255,255,255,0.7)', fontWeight: '600', fontSize: 15 },
  modalSaveBtn: { flex: 1, borderRadius: 18, overflow: 'hidden' },
  modalSaveBtnGradient: { paddingVertical: 14, alignItems: 'center', justifyContent: 'center' },
  modalSaveText: { color: '#001529', fontWeight: '800', fontSize: 15 },
});


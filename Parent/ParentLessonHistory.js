import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  ScrollView,
  StatusBar,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import styles from './ParentLessonHistory.styles';
import { API_BASE_URL } from '../apiConfig';
import { colors } from '../theme/tokens';
import {
  countUnreadNotifications,
  fetchParentNotifications,
} from '../notifications/parentNotifications';

const LESSON_TYPE_LABELS = {
  Private: 'שיעור פרטני',
  Group: 'שיעור קבוצתי',
};

const LESSON_STATUS_LABELS = {
  Scheduled: 'מתוכנן',
  Confirmed: 'מאושר',
  Pending: 'ממתין לאישור',
  Cancelled: 'בוטל',
  Completed: 'בוצע',
};

const DEFAULT_HISTORY_DAYS = 180;

const toIsoDateString = (dateValue) => {
  const parsedDate = dateValue instanceof Date ? dateValue : new Date(dateValue);
  if (Number.isNaN(parsedDate.getTime())) {
    return '';
  }

  const year = parsedDate.getFullYear();
  const month = String(parsedDate.getMonth() + 1).padStart(2, '0');
  const day = String(parsedDate.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const formatLessonDate = (rawDate) => {
  const normalized = String(rawDate || '').trim();
  if (!normalized) {
    return 'תאריך לא זמין';
  }

  const parsed = new Date(`${normalized}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) {
    return normalized;
  }

  return parsed.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
};

const parseSortDate = (meetingDate, startTime) => {
  const normalizedDate = String(meetingDate || '').trim();
  if (!normalizedDate) {
    return 0;
  }

  const normalizedTime = String(startTime || '').trim() || '00:00';
  const parsed = new Date(`${normalizedDate}T${normalizedTime}:00`);
  return Number.isNaN(parsed.getTime()) ? 0 : parsed.getTime();
};

const normalizeChildren = (payload) => {
  return (Array.isArray(payload) ? payload : [])
    .map((child) => {
      const childId = Number(child?.id || 0);
      const fullName = String(child?.fullName || `${child?.firstName || ''} ${child?.lastName || ''}`).trim();

      return {
        id: childId,
        fullName: fullName || `ילד #${childId}`,
      };
    })
    .filter((child) => child.id > 0);
};

const normalizeLessons = (payload) => {
  return (Array.isArray(payload) ? payload : [])
    .map((lesson) => {
      const sessionId = Number(lesson?.sessionId || 0);
      const childId = Number(lesson?.childId || 0);
      const lessonType = String(lesson?.lessonType || 'Private').trim();
      const status = String(lesson?.status || 'Scheduled').trim();

      return {
        sessionId,
        childId,
        childName: String(lesson?.childName || '').trim() || `ילד #${childId}`,
        instructorName: String(lesson?.instructorName || '').trim(),
        groupName: String(lesson?.groupName || '').trim(),
        meetingDate: String(lesson?.meetingDate || '').trim(),
        weekday: String(lesson?.weekday || '').trim(),
        startTime: String(lesson?.startTime || '').trim().slice(0, 5),
        endTime: String(lesson?.endTime || '').trim().slice(0, 5),
        lessonType,
        lessonTypeLabel: LESSON_TYPE_LABELS[lessonType] || lessonType,
        status,
        statusLabel: LESSON_STATUS_LABELS[status] || status,
        notes: String(lesson?.notes || '').trim(),
      };
    })
    .filter((lesson) => lesson.sessionId !== 0 && lesson.childId > 0)
    .sort((first, second) => parseSortDate(second.meetingDate, second.startTime) - parseSortDate(first.meetingDate, first.startTime));
};

export default function ParentLessonHistory({ route }) {
  const navigation = useNavigation();

  const authUser = route?.params?.authUser;
  const parentId = Number(route?.params?.parentId || authUser?.id || 0);

  const [children, setChildren] = useState([]);
  const [isLoadingChildren, setIsLoadingChildren] = useState(false);
  const [historyLessons, setHistoryLessons] = useState([]);
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [selectedChildId, setSelectedChildId] = useState('all');
  const [unreadNotificationCount, setUnreadNotificationCount] = useState(0);

  const loadUnreadNotifications = useCallback(async () => {
    if (!parentId) {
      setUnreadNotificationCount(0);
      return;
    }

    try {
      const notifications = await fetchParentNotifications(parentId);
      setUnreadNotificationCount(countUnreadNotifications(notifications));
    } catch (_error) {
      setUnreadNotificationCount(0);
    }
  }, [parentId]);

  useEffect(() => {
    loadUnreadNotifications();
  }, [loadUnreadNotifications]);

  useFocusEffect(
    useCallback(() => {
      loadUnreadNotifications();
    }, [loadUnreadNotifications]),
  );

  useEffect(() => {
    if (!parentId) {
      return undefined;
    }

    const intervalId = setInterval(() => {
      loadUnreadNotifications();
    }, 10000);

    return () => {
      clearInterval(intervalId);
    };
  }, [parentId, loadUnreadNotifications]);

  useEffect(() => {
    let isMounted = true;

    const loadChildren = async () => {
      if (!parentId) {
        setChildren([]);
        return;
      }

      try {
        setIsLoadingChildren(true);

        const response = await fetch(`${API_BASE_URL}/chat/parent/${parentId}/children`);
        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון את רשימת הילדים.');
        }

        if (!isMounted) {
          return;
        }

        setChildren(normalizeChildren(payload));
      } catch (_error) {
        if (isMounted) {
          setChildren([]);
        }
      } finally {
        if (isMounted) {
          setIsLoadingChildren(false);
        }
      }
    };

    loadChildren();

    return () => {
      isMounted = false;
    };
  }, [parentId]);

  useEffect(() => {
    let isMounted = true;

    const loadLessonHistory = async () => {
      if (!parentId) {
        setHistoryLessons([]);
        setHistoryError('לא זוהה הורה מחובר.');
        return;
      }

      try {
        setIsLoadingHistory(true);
        setHistoryError('');

        const toDate = new Date();
        const fromDate = new Date();
        fromDate.setDate(fromDate.getDate() - (DEFAULT_HISTORY_DAYS - 1));

        const fromDateIso = toIsoDateString(fromDate);
        const toDateIso = toIsoDateString(toDate);

        const response = await fetch(
          `${API_BASE_URL}/parent/${parentId}/lesson-history?fromDate=${encodeURIComponent(fromDateIso)}&toDate=${encodeURIComponent(toDateIso)}`,
        );
        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון את היסטוריית השיעורים כרגע.');
        }

        if (!isMounted) {
          return;
        }

        setHistoryLessons(normalizeLessons(payload));
      } catch (error) {
        if (isMounted) {
          setHistoryLessons([]);
          setHistoryError(error?.message || 'לא ניתן לטעון את היסטוריית השיעורים כרגע.');
        }
      } finally {
        if (isMounted) {
          setIsLoadingHistory(false);
        }
      }
    };

    loadLessonHistory();

    return () => {
      isMounted = false;
    };
  }, [parentId]);

  const childOptions = useMemo(() => {
    const fromChildren = children.map((child) => ({ id: child.id, fullName: child.fullName }));

    const existingIds = new Set(fromChildren.map((child) => child.id));

    const fromLessons = historyLessons
      .filter((lesson) => !existingIds.has(lesson.childId))
      .map((lesson) => ({ id: lesson.childId, fullName: lesson.childName }));

    return [...fromChildren, ...fromLessons].sort((first, second) => first.fullName.localeCompare(second.fullName, 'he'));
  }, [children, historyLessons]);

  useEffect(() => {
    if (selectedChildId === 'all') {
      return;
    }

    const selectedIdNumber = Number(selectedChildId);
    if (!childOptions.some((child) => child.id === selectedIdNumber)) {
      setSelectedChildId('all');
    }
  }, [childOptions, selectedChildId]);

  const filteredLessons = useMemo(() => {
    if (selectedChildId === 'all') {
      return historyLessons;
    }

    const selectedIdNumber = Number(selectedChildId);
    return historyLessons.filter((lesson) => lesson.childId === selectedIdNumber);
  }, [historyLessons, selectedChildId]);

  const lessonSections = useMemo(() => {
    const lessonsByChildId = new Map();

    filteredLessons.forEach((lesson) => {
      if (!lessonsByChildId.has(lesson.childId)) {
        lessonsByChildId.set(lesson.childId, []);
      }

      lessonsByChildId.get(lesson.childId).push(lesson);
    });

    const sectionIds = selectedChildId === 'all'
      ? Array.from(new Set([...childOptions.map((child) => child.id), ...lessonsByChildId.keys()]))
      : [Number(selectedChildId)];

    return sectionIds.map((childId) => {
      const childMeta = childOptions.find((child) => child.id === childId);
      return {
        childId,
        childName: childMeta?.fullName || `ילד #${childId}`,
        lessons: lessonsByChildId.get(childId) || [],
      };
    });
  }, [childOptions, filteredLessons, selectedChildId]);

  const handleBackPress = useCallback(() => {
    if (navigation.canGoBack()) {
      navigation.goBack();
      return;
    }

    navigation.navigate('ParentHomepage', { authUser, parentId });
  }, [navigation, authUser, parentId]);

  const openNotificationsInbox = useCallback(() => {
    navigation.navigate('ParentNotificationsInbox', { authUser, parentId });
  }, [navigation, authUser, parentId]);

  const totalShownLessons = filteredLessons.length;

  return (
    <View style={styles.container}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />

      <LinearGradient
        colors={['#005a80', '#00456a', colors.bgDeep]}
        style={styles.backgroundGradient}
      />

      <View style={styles.backgroundGradient}>
        <LinearGradient
          colors={['#00aed8', '#007fa7', '#004d73']}
          style={{ flex: 1 }}
        />
      </View>

      <BlurView intensity={20} tint="dark" style={styles.navbar}>
        <TouchableOpacity
          style={styles.logoutBtn}
          activeOpacity={0.75}
          onPress={handleBackPress}
        >
          <Text style={styles.logoutBtnText}>›</Text>
        </TouchableOpacity>

        <View style={styles.navCenter}>
          <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.navLogo}>
            <Text style={styles.navLogoEmoji}>📚</Text>
          </LinearGradient>
          <Text style={styles.navTitle}>היסטוריית שיעורים</Text>
        </View>

        <View style={{ width: 42 }} />
      </BlurView>

      <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={false}>
        <View style={styles.hero}>
          <Text style={styles.heroTitle}>היסטוריית שיעורים</Text>
          <Text style={styles.heroSubtitle}>צפייה בשיעורים שבהם השתתף כל ילד ובפרטים הנלווים</Text>
        </View>

        <View style={styles.section}>
          <BlurView intensity={18} tint="dark" style={styles.summaryCard}>
            <Text style={styles.summaryTitle}>שיעורים מצטברים</Text>
            <Text style={styles.summaryLine}>סה"כ שיעורים מוצגים: {totalShownLessons}</Text>
          </BlurView>
        </View>

        <View style={styles.section}>
          <Text style={styles.childFilterLabel}>סינון לפי ילד</Text>

          {isLoadingChildren ? (
            <View style={styles.stateWrap}>
              <ActivityIndicator color="#00d4ff" size="small" />
              <Text style={styles.stateText}>טוען רשימת ילדים...</Text>
            </View>
          ) : (
            <View style={styles.chipsWrap}>
              <TouchableOpacity
                activeOpacity={0.85}
                style={[styles.childChip, selectedChildId === 'all' ? styles.childChipActive : null]}
                onPress={() => setSelectedChildId('all')}
              >
                <Text style={[styles.childChipText, selectedChildId === 'all' ? styles.childChipTextActive : null]}>כל הילדים</Text>
              </TouchableOpacity>

              {childOptions.map((child) => {
                const chipId = String(child.id);
                const isSelected = selectedChildId === chipId;

                return (
                  <TouchableOpacity
                    key={`filter-child-${child.id}`}
                    activeOpacity={0.85}
                    style={[styles.childChip, isSelected ? styles.childChipActive : null]}
                    onPress={() => setSelectedChildId(chipId)}
                  >
                    <Text style={[styles.childChipText, isSelected ? styles.childChipTextActive : null]}>{child.fullName}</Text>
                  </TouchableOpacity>
                );
              })}
            </View>
          )}
        </View>

        <View style={styles.section}>
          {isLoadingHistory ? (
            <View style={styles.stateWrap}>
              <ActivityIndicator color="#00d4ff" size="small" />
              <Text style={styles.stateText}>טוען היסטוריית שיעורים...</Text>
            </View>
          ) : historyError ? (
            <Text style={styles.errorText}>{historyError}</Text>
          ) : lessonSections.length === 0 ? (
            <Text style={styles.stateText}>לא נמצאו שיעורים להצגה בתקופה שנבחרה.</Text>
          ) : (
            lessonSections.map((section) => (
              <BlurView key={`section-${section.childId}`} intensity={16} tint="dark" style={styles.childSectionCard}>
                <View style={styles.childSectionHeader}>
                  <Text style={styles.childSectionTitle}>{section.childName}</Text>
                  <Text style={styles.childSectionCount}>{section.lessons.length} שיעורים</Text>
                </View>

                {section.lessons.length === 0 ? (
                  <Text style={styles.emptyChildText}>לא נמצאו שיעורים לילד זה בטווח הזמן הנבחר.</Text>
                ) : (
                  section.lessons.map((lesson) => (
                    <View key={`lesson-${section.childId}-${lesson.sessionId}`} style={styles.lessonCard}>
                      <View style={styles.lessonHeaderRow}>
                        <Text style={styles.lessonDate}>
                          {lesson.weekday ? `${lesson.weekday}, ` : ''}
                          {formatLessonDate(lesson.meetingDate)}
                        </Text>

                        <View
                          style={[
                            styles.lessonTypeBadge,
                            lesson.lessonType === 'Group' ? styles.lessonTypeBadgeGroup : styles.lessonTypeBadgePrivate,
                          ]}
                        >
                          <Text style={styles.lessonTypeBadgeText}>{lesson.lessonTypeLabel}</Text>
                        </View>
                      </View>

                      <Text style={styles.lessonLine}>שעה: {lesson.startTime || '--:--'} - {lesson.endTime || '--:--'}</Text>
                      <Text style={styles.lessonLine}>מדריך: {lesson.instructorName || 'לא זמין'}</Text>
                      {lesson.groupName ? <Text style={styles.lessonLine}>קבוצה: {lesson.groupName}</Text> : null}
                      <Text style={styles.lessonLine}>סטטוס: {lesson.statusLabel}</Text>
                      {lesson.notes ? <Text style={styles.lessonNotes}>הערות: {lesson.notes}</Text> : null}
                    </View>
                  ))
                )}
              </BlurView>
            ))
          )}
        </View>
      </ScrollView>
    </View>
  );
}

import React, { useRef, useEffect, useState, useMemo, useCallback } from 'react';
import {
  StyleSheet,
  Text,
  View,
  Animated,
  Dimensions,
  TouchableOpacity,
  StatusBar,
  Easing,
  Platform,
  Alert,
  Modal,
  ScrollView,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import { colors } from '../theme/tokens';
import styles from './ParentHomepage.styles';
import { API_BASE_URL } from '../apiConfig';
import { registerPushNotificationsForUser, unregisterPushNotificationsForUser } from '../notifications/pushNotifications';
import {
  buildUnreadChatCountByInstructor,
  countUnreadNotifications,
  fetchParentNotifications,
  markParentChatNotificationsAsRead,
} from '../notifications/parentNotifications';

const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);


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
  }, [duration, startPosition, translateX]);

  return (
    <Animated.View style={[styles.waveContainer, { transform: [{ translateX }] }]}>
      <Svg width={2400} height={150} viewBox="0 0 2400 150" preserveAspectRatio="none">
        <Path fill={color} d={d} />
      </Svg>
    </Animated.View>
  );
};

/* ============================================================
   ANIMATED ACTIVITY CARD
   ============================================================ */
const ActivityCard = ({ date, title, desc, icon, index, onPressLessonReport }) => {
  const fadeAnim = useRef(new Animated.Value(0)).current;
  const slideAnim = useRef(new Animated.Value(40)).current;

  useEffect(() => {
    const delay = 500 + index * 150;
    Animated.parallel([
      Animated.timing(fadeAnim, { toValue: 1, duration: 500, delay, useNativeDriver: true }),
      Animated.timing(slideAnim, {
        toValue: 0, duration: 500, delay,
        easing: Easing.out(Easing.quad), useNativeDriver: true,
      }),
    ]).start();
  }, []);

  return (
    <Animated.View style={{ opacity: fadeAnim, transform: [{ translateY: slideAnim }], marginBottom: 14 }}>
      <BlurView intensity={18} tint="dark" style={styles.activityCard}>
        {/* Gradient accent line on the right */}
        <LinearGradient
          colors={['#00d4ff', '#0077aa']}
          style={styles.activityAccent}
        />

        <View style={styles.activityHeader}>
          <View style={styles.activityHeaderRight}>
            <View style={styles.activityIconBgMini}>
              <Text style={styles.activityIconMini}>{icon}</Text>
            </View>
            <Text style={styles.activityTitle}>{title}</Text>
          </View>
          <View style={styles.activityDateBadge}>
            <Text style={styles.activityDate}>{date}</Text>
          </View>
        </View>

        <View style={styles.activityContent}>
          <Text style={styles.activityDesc}>{desc}</Text>
        </View>

        <View style={styles.activityDivider} />

        <View style={styles.activityFooter}>
          <TouchableOpacity
            activeOpacity={0.75}
            style={styles.activityReportBtn}
            onPress={onPressLessonReport}
          >
            <LinearGradient
              colors={['rgba(0,212,255,0.22)', 'rgba(0,120,180,0.15)']}
              start={{ x: 0, y: 0 }}
              end={{ x: 1, y: 0 }}
              style={styles.activityReportBtnGradient}
            >
              <Text style={styles.activityReportBtnText}>דו"ח הילד בשיעור זה 📊</Text>
            </LinearGradient>
          </TouchableOpacity>
        </View>
      </BlurView>
    </Animated.View>
  );
};

/* ============================================================
  MAIN COMPONENT
  ============================================================ */

const actionButtons = [
  { label: 'הסבר על שיטת ACM', icon: '📘', action: 'openAcmGuide', gradient: ['#ffcf5b', '#ff986a'] },
  { label: 'היסטוריית שיעורים', icon: '📚', screen: 'ParentLessonHistory', gradient: ['#12b886', '#0f9f74'] },
  { label: 'דו"ח התקדמות הילד', icon: '📊', screen: 'ParentProgressReport', gradient: ['#00b8d4', '#0088a0'] },
];

const LESSON_TYPE_LABELS = {
  Private: 'שיעור פרטני',
  Group: 'שיעור קבוצתי',
};

const LESSON_STATUS_LABELS = {
  Scheduled: 'מתוכנן',
  Confirmed: 'מאושר',
  Pending: 'ממתין לאישור סופי',
  Cancelled: 'בוטל',
};

const REPORT_METRIC_LABELS = [
  'הסתגלות וביטחון במים',
  'שליטה בנשימות (הכנסת ראש למים)',
  'תנועתיות וקואורדינציה',
  'יציבה וציפה',
  'תקשורת במים (ושיתוף פעולה)',
  'התמדה ומאמץ',
  'יוזמה',
  'קשב וריכוז',
  'תגובה להוראות',
  'עצמאות בתרגיל',
];

const REPORT_RATING_MIN = 0;
const REPORT_RATING_MAX = 5;

const ACM_PARENT_GUIDE_SECTIONS = [
  {
    title: 'מהי שיטת ACM?',
    points: [
      'ב-Equal Aquatics עובדים בשילוב עקרונות AAM ו-ACM.',
      'AAM מדגישה קשר בטוח בין ילד להורה, ויסות רגשי והסתגלות הדרגתית למים.',
      'ACM מוסיפה למידה קהילתית: שיתוף פעולה, תחושת שייכות וביטחון חברתי במים.',
    ],
  },
  {
    title: 'איך השיטה עוזרת לילד שלכם במים?',
    points: [
      'מפחיתה פחד ממים בעזרת חשיפה עדינה ומותאמת לקצב האישי של הילד.',
      'מחזקת שליטה בנשימה, רוגע וויסות חושי בזמן פעילות במים.',
      'מפתחת ביטחון תנועתי: ציפה, הנעה במים וקואורדינציה.',
      'תומכת ברכישת הרגלי בטיחות ומיומנויות הישרדות בסיסיות במים.',
    ],
  },
  {
    title: 'איך נראה שיעור טיפוסי?',
    points: [
      'פתיחה רגועה עם נשימה משותפת והכנה רגשית קצרה למים.',
      'תרגול מדורג של ביטחון, נשימה ותנועה לפי רמת הילד.',
      'בפעילויות קבוצתיות: עבודה על תקשורת, תורנות ועזרה הדדית.',
      'סיום עם חיזוק הצלחות קטנות כדי לבנות תחושת מסוגלות לאורך זמן.',
    ],
  },
  {
    title: 'מה התפקיד שלכם כהורים?',
    points: [
      'להשתמש בשפה רגועה וקצרה, ולהראות לילד שאתם איתו לאורך כל התהליך.',
      'לא לדחוף בכוח ברגעי פחד; המטרה היא לבנות אמון, לא להאיץ ביצועים.',
      'לעדכן את המדריך ברגישויות, חששות או שינויים רגשיים/בריאותיים.',
      'לחזק בבית את ההתקדמות שהילד עשה במים, גם אם היא קטנה.',
    ],
  },
  {
    title: 'עקרונות בטיחות מובילים בשיטה',
    points: [
      'מגע בטוח, צפוי ומוסכם - הילד יודע מראש מה עומד לקרות.',
      'התקדמות מדורגת: עוברים שלב רק אחרי שהילד מרגיש יציב ובטוח.',
      'נשימה רגועה היא חלק מרכזי בכל שיעור ולא רק טכניקה לשחייה.',
      'לעולם לא משאירים ילד לבד במים, גם לא לזמן קצר.',
    ],
  },
];

const formatLessonDate = (rawDate) => {
  const normalized = String(rawDate || '').trim();
  if (!normalized) {
    return '';
  }

  // Format "yyyy-MM-dd" to "dd/MM/yyyy"
  const parts = normalized.split('-');
  if (parts.length === 3) {
    const year = parts[0];
    const month = parts[1];
    const day = parts[2];
    return `${day}/${month}/${year}`;
  }

  return normalized;
};

const normalizeMetricValue = (rawValue) => {
  if (!Number.isFinite(rawValue)) {
    return REPORT_RATING_MIN;
  }

  const clamped = Math.max(REPORT_RATING_MIN, Math.min(REPORT_RATING_MAX, rawValue));
  return Math.round(clamped);
};

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

const parseLessonDateTime = (meetingDate, startTime) => {
  const normalizedDate = String(meetingDate || '').trim();
  if (!normalizedDate) {
    return null;
  }

  const normalizedTime = String(startTime || '').trim() || '00:00';
  const parsed = new Date(`${normalizedDate}T${normalizedTime}:00`);

  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  return parsed;
};

const parseReportNotes = (report) => {
  if (!report || typeof report !== 'object') {
    return { metrics: [], comment: 'המדריך לא הוסיף הערה בדיווח זה.' };
  }

  let rawMetrics = [];
  let rawComment = '';

  if (Array.isArray(report.metrics)) {
    rawMetrics = report.metrics;
  } else if (typeof report.metrics === 'string' && report.metrics.trim()) {
    try {
      rawMetrics = JSON.parse(report.metrics);
      if (!Array.isArray(rawMetrics)) rawMetrics = [];
    } catch (e) { }
  }
  if (typeof report.comment === 'string' && report.comment.trim()) {
    rawComment = report.comment.trim();
  }

  const rawNotes = report.notes || report.Notes;
  let parsedNotes = null;
  if (rawNotes && typeof rawNotes === 'object') {
    parsedNotes = rawNotes;
  } else if (typeof rawNotes === 'string' && rawNotes.trim()) {
    try {
      parsedNotes = JSON.parse(rawNotes);
    } catch (_error) {
      parsedNotes = null;
    }
  }

  if (parsedNotes && typeof parsedNotes === 'object') {
    if (rawMetrics.length === 0 && Array.isArray(parsedNotes.metrics)) {
      rawMetrics = parsedNotes.metrics;
    }
    if (!rawComment) {
      rawComment = String(parsedNotes.comment || parsedNotes.generalNotes || '').trim();
    }
  }

  const normalizedMetrics = rawMetrics
    .map((metric, index) => {
      const label = String(metric?.label || '').trim() || REPORT_METRIC_LABELS[index] || `מדד ${index + 1}`;
      const value = normalizeMetricValue(Number(metric?.value));
      return { label, value };
    })
    .filter((metric) => Boolean(metric.label));

  const comment = rawComment || 'המדריך לא הוסיף הערה בדיווח זה.';

  return {
    metrics: normalizedMetrics,
    comment,
  };
};

const didLessonAlreadyHappen = (lesson, nowDate) => {
  const lessonDateTime = parseLessonDateTime(lesson.meetingDate, lesson.startTime);
  if (!lessonDateTime) {
    return false;
  }

  return lessonDateTime.getTime() <= nowDate.getTime();
};

export default function ParentHomepage({ route }) {
  const navigation = useNavigation();
  const authUser = route?.params?.authUser;
  const parentId = Number(authUser?.id ?? 0);
  const parentFullName = (authUser?.fullName || '').trim() || 'הורה';
  const parentFirstName = parentFullName.split(' ')[0] || 'הורה';

  const [children, setChildren] = useState([]);
  const [isLoadingChildren, setIsLoadingChildren] = useState(false);
  const [instructorChildLinks, setInstructorChildLinks] = useState([]);
  const [isLoadingInstructorLinks, setIsLoadingInstructorLinks] = useState(false);
  const [scheduledLessons, setScheduledLessons] = useState([]);
  const [isLoadingLessons, setIsLoadingLessons] = useState(false);
  const [lessonsError, setLessonsError] = useState('');
  const [reportsByChildId, setReportsByChildId] = useState({});
  const [isLoadingReportsByChild, setIsLoadingReportsByChild] = useState(false);
  const [selectedLessonReport, setSelectedLessonReport] = useState(null);
  const [isLessonReportModalVisible, setIsLessonReportModalVisible] = useState(false);
  const [isAcmGuideModalVisible, setIsAcmGuideModalVisible] = useState(false);
  const [unreadNotificationCount, setUnreadNotificationCount] = useState(0);
  const [unreadChatByInstructorId, setUnreadChatByInstructorId] = useState({});
  const [selectedChildIdFilter, setSelectedChildIdFilter] = useState(null);

  const scrollY = useRef(new Animated.Value(0)).current;

  // Hero animations
  const heroFade = useRef(new Animated.Value(0)).current;
  const heroSlide = useRef(new Animated.Value(50)).current;

  useEffect(() => {
    Animated.parallel([
      Animated.timing(heroFade, { toValue: 1, duration: 800, useNativeDriver: true }),
      Animated.timing(heroSlide, {
        toValue: 0, duration: 800,
        easing: Easing.out(Easing.quad), useNativeDriver: true,
      }),
    ]).start();
  }, []);

  useEffect(() => {
    if (!parentId) {
      return;
    }

    registerPushNotificationsForUser({
      userType: 'Parent',
      userId: parentId,
    }).then((result) => {
      if (!result?.ok) {
        console.log('[push] parent registration skipped', {
          userId: parentId,
          reason: result?.reason || 'unknown',
        });
      }
    }).catch((error) => {
      console.warn('[push] parent registration failed', {
        userId: parentId,
        error: String(error?.message || error),
      });
    });
  }, [parentId]);

  const loadUnreadIndicators = useCallback(async () => {
    if (!parentId) {
      setUnreadNotificationCount(0);
      setUnreadChatByInstructorId({});
      return;
    }

    try {
      const notifications = await fetchParentNotifications(parentId);
      setUnreadNotificationCount(countUnreadNotifications(notifications));
      setUnreadChatByInstructorId(buildUnreadChatCountByInstructor(notifications));
    } catch (_error) {
      setUnreadNotificationCount(0);
      setUnreadChatByInstructorId({});
    }
  }, [parentId]);

  useEffect(() => {
    loadUnreadIndicators();
  }, [loadUnreadIndicators]);

  useFocusEffect(
    useCallback(() => {
      loadUnreadIndicators();
    }, [loadUnreadIndicators]),
  );

  useEffect(() => {
    if (!parentId) {
      return undefined;
    }

    const intervalId = setInterval(() => {
      loadUnreadIndicators();
    }, 10000);

    return () => {
      clearInterval(intervalId);
    };
  }, [parentId, loadUnreadIndicators]);

  const performLogout = useCallback(async () => {
    if (parentId) {
      await unregisterPushNotificationsForUser({
        userType: 'Parent',
        userId: parentId,
      });
    }

    navigation.reset({
      index: 0,
      routes: [{ name: 'Login' }],
    });
  }, [navigation, parentId]);

  const handleTemporaryLogout = useCallback(() => {
    if (Platform.OS === 'web') {
      const shouldLogout = typeof globalThis.confirm === 'function'
        ? globalThis.confirm('רוצים לצאת כרגע מהחשבון?')
        : true;

      if (shouldLogout) {
        performLogout();
      }

      return;
    }

    Alert.alert(
      'התנתקות זמנית',
      'רוצים לצאת כרגע מהחשבון?',
      [
        { text: 'ביטול', style: 'cancel' },
        {
          text: 'התנתקות',
          style: 'destructive',
          onPress: performLogout,
        },
      ],
    );
  }, [performLogout]);

  const openNotificationsInbox = useCallback(() => {
    navigation.navigate('ParentNotificationsInbox', { authUser, parentId });
  }, [navigation, authUser, parentId]);

  const surfaceOpacity = scrollY.interpolate({
    inputRange: [0, height * 0.6],
    outputRange: [1, 0],
    extrapolate: 'clamp',
  });

  const navBg = scrollY.interpolate({
    inputRange: [0, 60],
    outputRange: ['rgba(0, 21, 41, 0.35)', 'rgba(0, 21, 41, 0.88)'],
    extrapolate: 'clamp',
  });

  useEffect(() => {
    let isMounted = true;

    const loadChildren = async () => {
      if (!parentId) {
        return;
      }

      try {
        setIsLoadingChildren(true);

        const response = await fetch(`${API_BASE_URL}/chat/parent/${parentId}/children`);
        const payload = await response.json().catch(() => null);

        if (!response.ok || !payload) {
          throw new Error(payload?.message || 'Failed to load children.');
        }

        if (!isMounted) {
          return;
        }

        const normalizedChildren = Array.isArray(payload) ? payload : [];
        setChildren(normalizedChildren);
      } catch (error) {
        if (isMounted) {
          Alert.alert('שגיאת טעינה', 'לא ניתן לטעון את רשימת הילדים כרגע.');
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

    const loadInstructorChildLinks = async () => {
      if (!parentId || children.length === 0) {
        setInstructorChildLinks([]);
        return;
      }

      try {
        setIsLoadingInstructorLinks(true);

        const linksByChild = await Promise.all(
          children.map(async (child) => {
            const childId = Number(child?.id || 0);
            if (childId <= 0) {
              return [];
            }

            const childFullName = String(
              child?.fullName || `${child?.firstName || ''} ${child?.lastName || ''}`
            ).trim() || `ילד #${childId}`;

            const response = await fetch(
              `${API_BASE_URL}/chat/parent/${parentId}/children/${childId}/instructors`
            );
            const payload = await response.json().catch(() => null);

            if (!response.ok) {
              return [];
            }

            return (Array.isArray(payload) ? payload : [])
              .map((instructor) => {
                const instructorId = Number(instructor?.id || 0);
                const instructorName = String(
                  instructor?.fullName || `${instructor?.firstName || ''} ${instructor?.lastName || ''}`
                ).trim();

                return {
                  instructorId,
                  instructorName: instructorName || `מדריך #${instructorId}`,
                  instructorEmail: String(instructor?.email || '').trim(),
                  childId,
                  childName: childFullName,
                };
              })
              .filter((link) => link.instructorId > 0);
          }),
        );

        if (!isMounted) {
          return;
        }

        setInstructorChildLinks(linksByChild.flat());
      } catch (error) {
        if (isMounted) {
          Alert.alert('שגיאת טעינה', 'לא ניתן לטעון את רשימת המדריכים כרגע.');
          setInstructorChildLinks([]);
        }
      } finally {
        if (isMounted) {
          setIsLoadingInstructorLinks(false);
        }
      }
    };

    loadInstructorChildLinks();

    return () => {
      isMounted = false;
    };
  }, [parentId, children]);

  useEffect(() => {
    let isMounted = true;

    const loadScheduledLessons = async () => {
      if (!parentId) {
        setScheduledLessons([]);
        setLessonsError('');
        return;
      }

      try {
        setIsLoadingLessons(true);
        setLessonsError('');

        const windowStartDate = new Date();
        windowStartDate.setDate(windowStartDate.getDate() - 30);

        const fromDate = toIsoDateString(windowStartDate);
        const response = await fetch(`${API_BASE_URL}/parent/${parentId}/scheduled-lessons?fromDate=${fromDate}&days=52`);
        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון שיעורים קרובים כרגע.');
        }

        if (!isMounted) {
          return;
        }

        const normalizedLessons = (Array.isArray(payload) ? payload : [])
          .map((lesson) => {
            const sessionId = Number(lesson?.sessionId || 0);
            const childId = Number(lesson?.childId || 0);
            const startTime = String(lesson?.startTime || '').slice(0, 5);
            const endTime = String(lesson?.endTime || '').slice(0, 5);
            const lessonType = String(lesson?.lessonType || 'Private').trim();
            const status = String(lesson?.status || 'Scheduled').trim();

            return {
              sessionId,
              childId,
              instructorId: Number(lesson?.instructorId || 0),
              lessonType,
              lessonTypeLabel: LESSON_TYPE_LABELS[lessonType] || lessonType,
              status,
              statusLabel: LESSON_STATUS_LABELS[status] || status,
              childName: String(lesson?.childName || '').trim() || `ילד #${childId}`,
              instructorName: String(lesson?.instructorName || '').trim(),
              groupName: String(lesson?.groupName || '').trim(),
              weekday: String(lesson?.weekday || '').trim(),
              meetingDate: String(lesson?.meetingDate || ''),
              startTime,
              endTime,
              notes: String(lesson?.notes || '').trim(),
              targetMetric: String(lesson?.targetMetric || '').trim(),
            };
          })
          .filter((lesson) => Number(lesson.sessionId) !== 0)
          .sort((a, b) => {
            if (a.meetingDate !== b.meetingDate) {
              return a.meetingDate < b.meetingDate ? -1 : 1;
            }

            if (a.startTime !== b.startTime) {
              return a.startTime < b.startTime ? -1 : 1;
            }

            if (a.childName !== b.childName) {
              return a.childName < b.childName ? -1 : 1;
            }
            return 0;
          });

        setScheduledLessons(normalizedLessons);
      } catch (error) {
        if (isMounted) {
          setScheduledLessons([]);
          setLessonsError(error?.message || 'לא ניתן לטעון שיעורים קרובים כרגע.');
        }
      } finally {
        if (isMounted) {
          setIsLoadingLessons(false);
        }
      }
    };

    loadScheduledLessons();

    return () => {
      isMounted = false;
    };
  }, [parentId]);

  useEffect(() => {
    let isMounted = true;

    const loadReportsForPastLessons = async () => {
      if (!parentId || scheduledLessons.length === 0) {
        setReportsByChildId({});
        return;
      }

      const nowDate = new Date();
      const childIds = [...new Set(
        scheduledLessons
          .filter((lesson) => didLessonAlreadyHappen(lesson, nowDate))
          .map((lesson) => Number(lesson.childId || 0))
          .filter((childId) => childId > 0),
      )];

      if (childIds.length === 0) {
        setReportsByChildId({});
        return;
      }

      try {
        setIsLoadingReportsByChild(true);

        const reportEntries = await Promise.all(
          childIds.map(async (childId) => {
            const response = await fetch(`${API_BASE_URL}/parent/${parentId}/children/${childId}/reports`);
            const payload = await response.json().catch(() => null);

            if (!response.ok) {
              return [childId, []];
            }

            const reports = Array.isArray(payload) ? payload : [];
            return [childId, reports];
          }),
        );

        if (!isMounted) {
          return;
        }

        const mapped = reportEntries.reduce((acc, entry) => {
          const childId = Number(entry[0] || 0);
          const reports = Array.isArray(entry[1]) ? entry[1] : [];

          acc[childId] = reports;
          return acc;
        }, {});

        setReportsByChildId(mapped);
      } catch (_error) {
        if (isMounted) {
          setReportsByChildId({});
        }
      } finally {
        if (isMounted) {
          setIsLoadingReportsByChild(false);
        }
      }
    };

    loadReportsForPastLessons();

    return () => {
      isMounted = false;
    };
  }, [parentId, scheduledLessons]);

  const childNameById = useMemo(() => {
    const map = new Map();

    children.forEach((child) => {
      const childId = Number(child?.id || 0);
      if (childId <= 0) {
        return;
      }

      const fullName = String(
        child?.fullName || `${child?.firstName || ''} ${child?.lastName || ''}`
      ).trim() || `ילד #${childId}`;

      map.set(childId, fullName);
    });

    return map;
  }, [children]);

  const trackedChildrenLabel = useMemo(() => {
    if (children.length === 0) {
      return 'אין ילדים פעילים';
    }

    if (children.length === 1) {
      const onlyChild = children[0];
      return String(
        onlyChild?.fullName || `${onlyChild?.firstName || ''} ${onlyChild?.lastName || ''}`
      ).trim() || 'ילד לא זמין';
    }

    return 'הילדים שלכם';
  }, [children]);

  const instructorCards = useMemo(() => {
    const grouped = new Map();

    instructorChildLinks.forEach((link) => {
      const instructorId = Number(link?.instructorId || 0);
      if (instructorId <= 0) {
        return;
      }

      if (!grouped.has(instructorId)) {
        grouped.set(instructorId, {
          instructorId,
          instructorName: String(link?.instructorName || '').trim() || `מדריך #${instructorId}`,
          instructorEmail: String(link?.instructorEmail || '').trim(),
          childIds: [],
          childNames: [],
        });
      }

      const entry = grouped.get(instructorId);
      const childId = Number(link?.childId || 0);
      const childName = String(link?.childName || '').trim() || childNameById.get(childId) || `ילד #${childId}`;

      if (childId > 0 && !entry.childIds.includes(childId)) {
        entry.childIds.push(childId);
      }

      if (childName && !entry.childNames.includes(childName)) {
        entry.childNames.push(childName);
      }
    });

    return Array.from(grouped.values()).sort((a, b) => {
      if (a.instructorName < b.instructorName) return -1;
      if (a.instructorName > b.instructorName) return 1;
      return 0;
    });
  }, [childNameById, instructorChildLinks]);

  const getReportForLesson = (lesson) => {
    const childId = Number(lesson?.childId || 0);
    if (childId <= 0) {
      return null;
    }

    const reports = reportsByChildId[childId];
    if (!Array.isArray(reports) || reports.length === 0) {
      return null;
    }

    const targetDate = String(lesson?.meetingDate || '').trim();
    if (!targetDate) {
      return null;
    }

    const exactMatches = reports.filter((report) => {
      const reportDate = toIsoDateString(report?.reportDate || report?.ReportDate);
      const reportInstructorId = Number(report?.instructorId || report?.InstructorId || 0);
      const lessonInstructorId = Number(lesson?.instructorId || 0);

      if (reportDate !== targetDate) {
        return false;
      }

      if (lessonInstructorId > 0 && reportInstructorId > 0) {
        return lessonInstructorId === reportInstructorId;
      }

      return true;
    });

    if (exactMatches.length === 0) {
      return null;
    }

    return exactMatches[exactMatches.length - 1];
  };

  const nowDate = new Date();

  const pastLessons = useMemo(
    () => scheduledLessons
      .filter((lesson) => didLessonAlreadyHappen(lesson, nowDate))
      .sort((a, b) => {
        const aDate = parseLessonDateTime(a.meetingDate, a.startTime)?.getTime() || 0;
        const bDate = parseLessonDateTime(b.meetingDate, b.startTime)?.getTime() || 0;
        return bDate - aDate;
      }),
    [scheduledLessons, nowDate],
  );

  const upcomingLessons = useMemo(
    () => {
      let filtered = scheduledLessons.filter((lesson) => !didLessonAlreadyHappen(lesson, nowDate));
      if (selectedChildIdFilter !== null) {
        filtered = filtered.filter(lesson => Number(lesson.childId) === selectedChildIdFilter);
      }
      return filtered.sort((a, b) => {
        const aDate = parseLessonDateTime(a.meetingDate, a.startTime)?.getTime() || 0;
        const bDate = parseLessonDateTime(b.meetingDate, b.startTime)?.getTime() || 0;
        return aDate - bDate;
      });
    },
    [scheduledLessons, nowDate, selectedChildIdFilter],
  );

  const recentActivities = useMemo(
    () => {
      let filtered = pastLessons;
      if (selectedChildIdFilter !== null) {
        filtered = pastLessons.filter(lesson => Number(lesson.childId) === selectedChildIdFilter);
      }
      return filtered.slice(0, 3).map((lesson) => {
        const icon = lesson.lessonType === 'Group' ? '👥' : '🏊';
        const date = formatLessonDate(lesson.meetingDate).slice(0, 5);
        const title = lesson.lessonTypeLabel || 'פעילות';
        const childDisplayName = String(lesson?.childName || '').trim() || 'ילד לא זמין';
        const instructorDisplayName = String(lesson?.instructorName || '').trim() || 'מדריך לא זמין';
        const groupDisplayName = String(lesson?.groupName || '').trim();
        let desc = groupDisplayName
          ? `שם הילד: ${childDisplayName}\nקבוצה: ${groupDisplayName}`
          : `שם הילד: ${childDisplayName}\nשם המדריך: ${instructorDisplayName}`;

        const matchedReport = getReportForLesson(lesson);
        if (matchedReport && matchedReport.exerciseTitle) {
          desc += `\nתרגיל מרכזי: ${matchedReport.exerciseTitle}`;
        } else {
          desc += `\nמדד מטרה: ${lesson.targetMetric || 'ביטחון במים'}`;
        }

        return {
          key: `${lesson.lessonType}-${lesson.sessionId}-${lesson.childId}`,
          date,
          title,
          desc,
          icon,
          lesson,
        };
      });
    },
    [pastLessons, selectedChildIdFilter, reportsByChildId],
  );

  const openLessonReport = (lesson) => {
    const matchedReport = getReportForLesson(lesson);
    if (!matchedReport) {
      Alert.alert('דו"ח לא זמין', 'לשיעור הזה עדיין לא נוסף דו"ח מפורט על ידי המדריך.');
      return;
    }

    const parsed = parseReportNotes(matchedReport);
    const displayDate = formatLessonDate(lesson.meetingDate);
    setSelectedLessonReport({
      childName: lesson.childName,
      instructorName: lesson.instructorName,
      date: displayDate,
      time: `${lesson.startTime} - ${lesson.endTime}`,
      metrics: parsed.metrics,
      comment: parsed.comment,
    });
    setIsLessonReportModalVisible(true);
  };

  const openChatWithInstructor = async (instructorCard) => {
    if (!parentId || !instructorCard?.instructorId) {
      Alert.alert('לא ניתן לפתוח צ׳אט', 'חסרים פרטי מדריך תקינים לפתיחת השיחה.');
      return;
    }

    const childIds = Array.isArray(instructorCard?.childIds) ? instructorCard.childIds : [];
    const primaryChildId = Number(childIds[0] || 0);

    if (primaryChildId <= 0) {
      Alert.alert('לא ניתן לפתוח צ׳אט', 'לא נמצא ילד משויך למדריך שנבחר.');
      return;
    }

    const primaryChildName = childNameById.get(primaryChildId) || instructorCard?.childNames?.[0] || 'ילד לא זמין';
    const headerChildName = Array.isArray(instructorCard?.childNames) && instructorCard.childNames.length > 0
      ? instructorCard.childNames.join(', ')
      : primaryChildName;

    await markParentChatNotificationsAsRead(parentId, instructorCard.instructorId).catch(() => {
      // Do not block opening chat if notification marking fails.
    });

    setUnreadChatByInstructorId((prev) => ({
      ...prev,
      [instructorCard.instructorId]: 0,
    }));

    navigation.navigate('ChatPage', {
      parentId,
      childId: primaryChildId,
      instructorId: instructorCard.instructorId,
      parentName: parentFullName,
      childName: headerChildName,
      instructorName: instructorCard.instructorName,
    });
  };

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent />

      {/* ── Deep ocean base ── */}
      <LinearGradient
        colors={['#004466', '#002e4a', colors.bgDeep]}
        style={StyleSheet.absoluteFill}
      />

      {/* ── Surface water shimmer ── */}
      <Animated.View style={[StyleSheet.absoluteFill, { opacity: surfaceOpacity }]}>
        <LinearGradient
          colors={['#0099bb', '#006e8a', '#004466']}
          style={{ flex: 1 }}
        />
      </Animated.View>

      {/* ── Waves ── */}
      <Wave
        color="rgba(0,153,204,0.25)"
        duration={9000}
        startPosition={0}
        d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z"
      />
      <Wave
        color="rgba(0,212,255,0.18)"
        duration={7000}
        startPosition={-600}
        d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z"
      />

      {/* ── NAVBAR ── */}
      <AnimatedBlurView
        intensity={20}
        tint="dark"
        style={[styles.navbar, { backgroundColor: navBg }]}
      >
        <TouchableOpacity
          style={styles.navBellWrap}
          activeOpacity={0.7}
          onPress={openNotificationsInbox}
        >
          <View style={styles.navBellContent}>
            <View style={styles.navBellIconWrap}>
              <Text style={styles.navBell}>🔔</Text>

              {unreadNotificationCount > 0 ? (
                <View style={styles.navBadge}>
                  <Text style={styles.navBadgeText}>{unreadNotificationCount > 99 ? '99+' : unreadNotificationCount}</Text>
                </View>
              ) : null}
            </View>

            <Text style={styles.navBellLabel}>התראות</Text>
          </View>
        </TouchableOpacity>

        <View style={styles.navCenter}>
          <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.navLogo}>
            <Text style={styles.navLogoEmoji}>🐚</Text>
          </LinearGradient>
          <Text style={styles.navTitle}>לוח הורה</Text>
        </View>

        <TouchableOpacity
          style={styles.logoutBtn}
          activeOpacity={0.7}
          onPress={handleTemporaryLogout}
        >
          <Text style={styles.logoutBtnText}>התנתקות</Text>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* ── SCROLL CONTENT ── */}
      <Animated.ScrollView
        onScroll={Animated.event(
          [{ nativeEvent: { contentOffset: { y: scrollY } } }],
          { useNativeDriver: false },
        )}
        scrollEventThrottle={16}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* ── HERO GREETING ── */}
        <Animated.View
          style={[
            styles.hero,
            { opacity: heroFade, transform: [{ translateY: heroSlide }] },
          ]}
        >
          <Text style={styles.heroWelcome}>שלום {parentFirstName}! 👋</Text>
          <View style={styles.heroChildRow}>
            <Text style={styles.heroChildName}>
              <Text style={styles.heroChildLabel}>{"מעקב אחרי "}</Text>
              {trackedChildrenLabel}
            </Text>
          </View>

          {/* ── CHILDREN FILTER CHIPS ── */}
          {children.length > 0 ? (
            <View style={styles.childFilterContainer}>
              <TouchableOpacity
                activeOpacity={0.8}
                style={[
                  styles.childFilterChip,
                  selectedChildIdFilter === null && styles.childFilterChipActive
                ]}
                onPress={() => setSelectedChildIdFilter(null)}
              >
                <Text style={[
                  styles.childFilterText,
                  selectedChildIdFilter === null && styles.childFilterTextActive
                ]}>
                  הכל 🌟
                </Text>
              </TouchableOpacity>

              {children.map((child) => {
                const childId = Number(child?.id || 0);
                const childName = String(child?.fullName || `${child?.firstName || ''} ${child?.lastName || ''}`).trim() || `ילד #${childId}`;
                const isSelected = selectedChildIdFilter === childId;

                return (
                  <TouchableOpacity
                    key={childId}
                    activeOpacity={0.8}
                    style={[
                      styles.childFilterChip,
                      isSelected && styles.childFilterChipActive
                    ]}
                    onPress={() => setSelectedChildIdFilter(isSelected ? null : childId)}
                  >
                    <Text style={[
                      styles.childFilterText,
                      isSelected && styles.childFilterTextActive
                    ]}>
                      👶 {childName}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </View>
          ) : null}
        </Animated.View>

        {/* ── RECENT ACTIVITIES ── */}
        <View style={styles.section}>
          <View style={styles.sectionHeaderRow}>

            <Text style={styles.sectionTitle}>פעילויות אחרונות</Text>
          </View>

          {isLoadingLessons ? (
            <Text style={styles.selectorHelperText}>טוען פעילויות אחרונות...</Text>
          ) : recentActivities.length === 0 ? (
            <Text style={styles.selectorHelperText}>אין פעילויות רלוונטיות להצגה כרגע.</Text>
          ) : (
            recentActivities.map(({ key, lesson, ...activityProps }, i) => (
              <ActivityCard
                key={key}
                {...activityProps}
                index={i}
                onPressLessonReport={() => openLessonReport(lesson)}
              />
            ))
          )}

          {isLoadingReportsByChild ? (
            <Text style={styles.selectorHelperText}>טוען דוחות שיעור אחרונים...</Text>
          ) : null}
        </View>

        <View style={styles.section}>
          <View style={styles.sectionHeaderRow}>

            <Text style={styles.sectionTitle}>השיעורים הקרובים</Text>
          </View>

          {isLoadingLessons ? (
            <Text style={styles.selectorHelperText}>טוען שיעורים קרובים...</Text>
          ) : lessonsError ? (
            <Text style={styles.selectorHelperText}>{lessonsError}</Text>
          ) : upcomingLessons.length === 0 ? (
            <Text style={styles.selectorHelperText}>אין שיעורים מתוכננים ב-21 הימים הקרובים.</Text>
          ) : (
            upcomingLessons.map((lesson) => (
              <BlurView
                key={`${lesson.lessonType}-${lesson.sessionId}-${lesson.childId}`}
                intensity={18}
                tint="dark"
                style={styles.lessonCard}
              >
                <View style={styles.lessonHeaderRow}>
                  <View
                    style={[
                      styles.lessonTypeBadge,
                      lesson.lessonType === 'Group' ? styles.lessonTypeBadgeGroup : styles.lessonTypeBadgePrivate,
                    ]}
                  >
                    <Text style={styles.lessonTypeBadgeText}>{lesson.lessonTypeLabel}</Text>
                  </View>

                  <View style={styles.lessonStatusBadge}>
                    <Text style={styles.lessonStatusBadgeText}>{lesson.statusLabel}</Text>
                  </View>
                </View>

                <Text style={styles.lessonPrimaryText}>{lesson.childName}</Text>
                <Text style={styles.lessonSecondaryText}>מדריך: {lesson.instructorName || 'לא זמין'}</Text>
                {lesson.groupName ? (
                  <Text style={styles.lessonSecondaryText}>קבוצה: {lesson.groupName}</Text>
                ) : null}
                <Text style={styles.lessonDateLine}>
                  {lesson.weekday} | {formatLessonDate(lesson.meetingDate)} | {lesson.startTime} - {lesson.endTime}
                </Text>
                {lesson.notes ? <Text style={styles.lessonNotes}>הערות: {lesson.notes}</Text> : null}
                <Text style={styles.lessonMetricText}>מדד מטרה: {lesson.targetMetric || 'ביטחון במים'}</Text>
              </BlurView>
            ))
          )}
        </View>

        {/* ── INSTRUCTORS LIST ── */}
        <View style={styles.section}>
          <View style={styles.sectionHeaderRow}>

            <Text style={styles.sectionTitle}>המדריכים שלכם</Text>
          </View>

          {isLoadingChildren || isLoadingInstructorLinks ? (
            <Text style={styles.selectorHelperText}>טוען מדריכים...</Text>
          ) : instructorCards.length === 0 ? (
            <Text style={styles.selectorHelperText}>לא נמצאו מדריכים משויכים לילדים שלכם כרגע.</Text>
          ) : (
            <View style={styles.instructorsListWrap}>
              {instructorCards.map((instructorCard) => {
                const unreadChatCount = Number(unreadChatByInstructorId[instructorCard.instructorId] || 0);

                return (
                  <BlurView
                    key={`instructor-${instructorCard.instructorId}`}
                    intensity={22}
                    tint="dark"
                    style={styles.instructorCard}
                  >
                    <View style={styles.instructorInfo}>
                      <Text style={styles.instructorName}>{instructorCard.instructorName}</Text>
                      <Text style={styles.instructorRole}>{instructorCard.instructorEmail || 'אימייל לא זמין'}</Text>
                      <Text style={styles.instructorChildrenLabel}>
                        המדריך של: {instructorCard.childNames.join(', ')}
                      </Text>
                    </View>

                    <TouchableOpacity
                      style={styles.chatBtn}
                      activeOpacity={0.85}
                      onPress={() => openChatWithInstructor(instructorCard)}
                    >
                      <LinearGradient
                        colors={['#00d4ff', '#0099cc']}
                        style={styles.chatBtnGradient}
                      >
                        <Text style={styles.chatBtnIcon}>💬</Text>
                      </LinearGradient>

                      {unreadChatCount > 0 ? (
                        <View style={styles.chatBtnBadge}>
                          <Text style={styles.chatBtnBadgeText}>{unreadChatCount > 99 ? '99+' : unreadChatCount}</Text>
                        </View>
                      ) : null}
                    </TouchableOpacity>
                  </BlurView>
                );
              })}
            </View>
          )}

          <Text style={styles.scheduleHintText}>
            לתיאום שיעור לילד מסוים, התחילו בצ׳אט עם המדריך של אותו ילד. לאחר התיאום, הזמנת השיעור תופיע במרכז ההתראות (🔔), ושיעורים שבוצעו יוצגו בהיסטוריית השיעורים.
          </Text>
        </View>

        {/* ── ACTION BUTTONS ── */}
        <View style={styles.actionsSection}>
          {actionButtons.map((action, i) => (
            <TouchableOpacity
              key={i}
              style={styles.actionBtnOuter}
              activeOpacity={0.85}
              onPress={() => {
                if (action.action === 'openAcmGuide') {
                  setIsAcmGuideModalVisible(true);
                  return;
                }

                if (!action.screen) {
                  return;
                }

                navigation.navigate(action.screen, {
                  authUser,
                  parentId,
                  selectedChildId: null,
                  selectedInstructorId: null,
                  child: undefined,
                  instructor: undefined,
                });
              }}
            >
              <LinearGradient
                colors={action.gradient}
                start={{ x: 0, y: 0 }}
                end={{ x: 1, y: 0 }}
                style={styles.actionBtnGradient}
              >
                <Text style={styles.actionBtnIcon}>{action.icon}</Text>
                <Text style={styles.actionBtnLabel}>{action.label}</Text>
                <Text style={styles.actionBtnArrow}>←</Text>
              </LinearGradient>
            </TouchableOpacity>
          ))}
        </View>

        {/* ── FOOTER ── */}
        <View style={styles.footer}>
          <View style={styles.footerBrand}>
            <Text style={styles.footerEmoji}>🐚</Text>
            <Text style={styles.footerBrandText}>ACM</Text>
          </View>
          <Text style={styles.footerCopy}>כל הזכויות שמורות ל-ACM 2026 ©</Text>
        </View>
      </Animated.ScrollView>

      <Modal
        visible={isLessonReportModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setIsLessonReportModalVisible(false)}
      >
        <View style={styles.reportModalOverlay}>
          <BlurView intensity={24} tint="dark" style={styles.reportModalCard}>
            <Text style={styles.reportModalTitle}>דו"ח הילד בשיעור זה</Text>

            {selectedLessonReport ? (
              <>
                <Text style={styles.reportModalMetaLine}>ילד: {selectedLessonReport.childName}</Text>
                <Text style={styles.reportModalMetaLine}>מדריך: {selectedLessonReport.instructorName || 'לא זמין'}</Text>
                <Text style={styles.reportModalMetaLine}>תאריך: {selectedLessonReport.date} | שעה: {selectedLessonReport.time}</Text>

                <Text style={styles.reportModalSectionTitle}>מדדים מהשיעור</Text>
                {selectedLessonReport.metrics.length === 0 ? (
                  <Text style={styles.reportModalEmptyText}>לא הוזנו מדדים לשיעור זה.</Text>
                ) : (
                  selectedLessonReport.metrics.map((metric) => {
                    const fillPercent = Math.round((metric.value / REPORT_RATING_MAX) * 100);

                    return (
                      <View key={`${metric.label}-${metric.value}`} style={styles.reportMetricRow}>
                        <View style={styles.reportMetricHeader}>
                          <Text style={styles.reportMetricLabel}>{metric.label}</Text>
                          <Text style={styles.reportMetricValue}>{metric.value}/5</Text>
                        </View>
                        <View style={styles.reportMetricTrack}>
                          <View style={[styles.reportMetricFill, { width: `${fillPercent}%` }]} />
                        </View>
                      </View>
                    );
                  })
                )}

                <Text style={styles.reportModalSectionTitle}>הערת המדריך</Text>
                <View style={styles.reportCommentBox}>
                  <Text style={styles.reportCommentText}>{selectedLessonReport.comment}</Text>
                </View>
              </>
            ) : null}

            <TouchableOpacity
              activeOpacity={0.82}
              style={styles.reportModalCloseButton}
              onPress={() => setIsLessonReportModalVisible(false)}
            >
              <Text style={styles.reportModalCloseButtonText}>סגירה</Text>
            </TouchableOpacity>
          </BlurView>
        </View>
      </Modal>

      <Modal
        visible={isAcmGuideModalVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setIsAcmGuideModalVisible(false)}
      >
        <View style={styles.reportModalOverlay}>
          <BlurView intensity={24} tint="dark" style={styles.acmGuideModalCard}>
            <Text style={styles.acmGuideTitle}>הסבר על שיטת ACM</Text>
            <Text style={styles.acmGuideIntro}>
              מדריך קצר להורים: איך שיטת ACM, בשילוב עקרונות AAM, מחזקת ביטחון, ויסות רגשי
              ויכולת תנועה בטוחה של ילדים במים.
            </Text>

            <ScrollView
              style={styles.acmGuideScroll}
              contentContainerStyle={styles.acmGuideScrollContent}
              showsVerticalScrollIndicator={false}
            >
              {ACM_PARENT_GUIDE_SECTIONS.map((section) => (
                <View key={section.title} style={styles.acmGuideSection}>
                  <Text style={styles.acmGuideSectionTitle}>{section.title}</Text>
                  {section.points.map((point) => (
                    <Text key={`${section.title}-${point}`} style={styles.acmGuideBullet}>
                      • {point}
                    </Text>
                  ))}
                </View>
              ))}

              <View style={styles.acmGuideSourceNote}>
                <Text style={styles.acmGuideSourceText}>
                  המידע מבוסס על המסמך "מאגר מאמרים שחייה ACM AAM" מתוך פרויקט Equal Aquatics,
                  ונוסח כאן בשפה ידידותית להורים.
                </Text>
              </View>
            </ScrollView>

            <TouchableOpacity
              activeOpacity={0.82}
              style={styles.acmGuideCloseButton}
              onPress={() => setIsAcmGuideModalVisible(false)}
            >
              <Text style={styles.acmGuideCloseButtonText}>סגירה</Text>
            </TouchableOpacity>
          </BlurView>
        </View>
      </Modal>
    </View>
  );
}

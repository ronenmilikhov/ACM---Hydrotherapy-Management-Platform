import React, { useRef, useEffect, useState, useMemo, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, ScrollView, Alert,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useFocusEffect, useNavigation, useRoute } from '@react-navigation/native';
import AppCard from '../components/ui/AppCard';
import { colors, shadows } from '../theme/tokens';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';

const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);

const ATTENDANCE_ACHIEVEMENT_THRESHOLD = 80;
const METRIC_ACHIEVEMENT_THRESHOLD = 8;

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

const parseReportNotes = (rawNotes) => {
  if (rawNotes && typeof rawNotes === 'object') {
    return rawNotes;
  }

  if (typeof rawNotes === 'string' && rawNotes.trim()) {
    try {
      return JSON.parse(rawNotes);
    } catch (_error) {
      return null;
    }
  }

  return null;
};

const getReportTimestamp = (report) => {
  const rawDate = report?.reportDate || report?.date || 0;
  const timestamp = new Date(rawDate).getTime();
  return Number.isNaN(timestamp) ? 0 : timestamp;
};

const formatShortReportDate = (rawDate) => {
  const parsed = new Date(rawDate || 0);
  if (Number.isNaN(parsed.getTime())) {
    return '';
  }

  return parsed.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: '2-digit',
  });
};

const extractMetricsFromNotes = (notes) => {
  if (!notes || typeof notes !== 'object') {
    return [];
  }

  const metrics = Array.isArray(notes?.metrics) ? notes.metrics : [];
  return metrics
    .map((metric) => {
      const label = String(metric?.label || '').trim();
      const rawValue = Number(metric?.value);
      if (!label || !Number.isFinite(rawValue)) {
        return null;
      }

      return {
        label,
        value: Math.max(0, Math.min(10, Math.round(rawValue))),
      };
    })
    .filter(Boolean);
};

const resolveReportPresence = (notes) => {
  if (typeof notes?.isPresent === 'boolean') {
    return notes.isPresent;
  }

  if (typeof notes?.isPresent === 'number') {
    return notes.isPresent > 0;
  }

  // Historical reports might not include the attendance flag; treat them as present.
  return true;
};

const buildChildAchievementsFromReports = (reports) => {
  const orderedReports = [...(Array.isArray(reports) ? reports : [])].sort((first, second) => {
    const byDate = getReportTimestamp(second) - getReportTimestamp(first);
    if (byDate !== 0) {
      return byDate;
    }

    return Number(second?.reportId || 0) - Number(first?.reportId || 0);
  });

  if (orderedReports.length === 0) {
    return {
      attendanceText: 'אין מידע זמין כרגע',
      achievements: [],
    };
  }

  const totalReportsCount = orderedReports.length;
  const presentReportsCount = orderedReports.reduce((count, report) => {
    const notes = parseReportNotes(report?.notes);
    return count + (resolveReportPresence(notes) ? 1 : 0);
  }, 0);

  const attendancePercent = Math.round((presentReportsCount / totalReportsCount) * 100);
  const attendanceText = `${attendancePercent}% (${presentReportsCount}/${totalReportsCount})`;
  const latestThreeReports = orderedReports.slice(0, 3);

  const achievements = [];

  if (attendancePercent >= ATTENDANCE_ACHIEVEMENT_THRESHOLD) {
    achievements.push({
      id: 'attendance',
      label: 'נוכחות גבוהה במיוחד',
      info: `${attendancePercent}% נוכחות מצטברת`,
      icon: '✅',
    });
  }

  const metricCandidates = [];
  latestThreeReports.forEach((report) => {
    const notes = parseReportNotes(report?.notes);
    const dateText = formatShortReportDate(report?.reportDate || report?.date);

    extractMetricsFromNotes(notes).forEach((metric) => {
      metricCandidates.push({
        ...metric,
        reportTimestamp: getReportTimestamp(report),
        dateText,
      });
    });
  });

  metricCandidates.sort((first, second) => {
    const byValue = second.value - first.value;
    if (byValue !== 0) {
      return byValue;
    }

    return second.reportTimestamp - first.reportTimestamp;
  });

  const bestMetric = metricCandidates.find((metric) => metric.value >= METRIC_ACHIEVEMENT_THRESHOLD);
  if (bestMetric) {
    achievements.push({
      id: 'metric',
      label: 'הישג במדדי התקדמות',
      info: `${bestMetric.label}: ${bestMetric.value}/10${bestMetric.dateText ? ` • ${bestMetric.dateText}` : ''}`,
      icon: '📈',
    });
  }

  return {
    attendanceText,
    achievements: achievements.slice(0, 2),
  };
};

// ========================================
// 3. MAIN COMPONENT
// ========================================
export default function ChildProfile() {
  const navigation = useNavigation();
  const route = useRoute();
  const child = route.params?.child || {};
  const groupName = route.params?.groupName || 'לא נבחרה קבוצה';
  const childId = Number(route.params?.childId || child.childId || child.id || 0);
  const parentId = Number(route.params?.parentId || child.parentId || child.parentid || 0);
  const groupId = Number(route.params?.groupId || child.groupId || 0);
  const instructorId = Number(route.params?.instructorId || route.params?.authUser?.id || child.instructorId || 0);
  const instructorName = route.params?.authUser?.fullName || 'לא ידוע';
  const childName = child.name || 'ילד לא ידוע';
  const parentName = child.parentFullName || 'לא ידוע';
  const initial = childName[0] || 'א';

  const scrollY = useRef(new Animated.Value(0)).current;
  const [attendanceValue, setAttendanceValue] = useState('אין מידע זמין כרגע');
  const [achievements, setAchievements] = useState([]);
  const [isLoadingInsights, setIsLoadingInsights] = useState(false);

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

  useFocusEffect(
    useCallback(() => {
      let isCancelled = false;

      const loadChildInsights = async () => {
        if (!childId || !instructorId) {
          setAttendanceValue('אין מידע זמין כרגע');
          setAchievements([]);
          return;
        }

        try {
          setIsLoadingInsights(true);

          const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports`);
          const payload = await response.json().catch(() => null);

          if (!response.ok) {
            throw new Error(payload?.message || 'לא ניתן לטעון דיווחים כרגע.');
          }

          const reports = Array.isArray(payload) ? payload : [];
          const nextInsights = buildChildAchievementsFromReports(reports);

          if (!isCancelled) {
            setAttendanceValue(nextInsights.attendanceText);
            setAchievements(Array.isArray(nextInsights.achievements) ? nextInsights.achievements.slice(0, 2) : []);
          }
        } catch (_error) {
          if (!isCancelled) {
            setAttendanceValue('אין מידע זמין כרגע');
            setAchievements([]);
          }
        } finally {
          if (!isCancelled) {
            setIsLoadingInsights(false);
          }
        }
      };

      loadChildInsights();

      return () => {
        isCancelled = true;
      };
    }, [childId, instructorId]),
  );

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
      <Wave color="rgba(0, 153, 204, 0.3)" duration={8000} startPosition={0} d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z" />
      <Wave color="rgba(0, 212, 255, 0.25)" duration={6000} startPosition={-600} d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z" />

      {/* NAVBAR */}
      <AnimatedBlurView intensity={20} tint="dark" style={[styles.navbar, { backgroundColor: navBg }]}>
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backButton}>
          <Text style={styles.backButtonIcon}>{'\u2039'}</Text>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* MAIN CONTENT */}
      <Animated.ScrollView
        onScroll={Animated.event([{ nativeEvent: { contentOffset: { y: scrollY } } }], { useNativeDriver: false })}
        scrollEventThrottle={16}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* Profile Header */}
        <View style={styles.profileHeader}>
          <View style={styles.avatarGlow}>
            <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.avatar}>
              <Text style={styles.avatarText}>{initial}</Text>
            </LinearGradient>
          </View>
          <Text style={styles.profileName}>{childName}</Text>
          <View style={styles.metaBadge}>
            <Text style={[styles.metaText, { textAlign: 'center' }]}>קבוצה: {groupName} • מדריך: {instructorName}</Text>
            <Text style={[styles.metaText, { textAlign: 'center', marginTop: 4, color: 'rgba(0, 212, 255, 0.8)' }]}>הורה: {parentName}</Text>
          </View>
        </View>

        {/* Dynamic Stats Overlay Placeholder */}
          <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.statsCard}>
            <View style={styles.statsRow}>
              <View style={styles.statBoxSingle}>
                <Text style={styles.statLabel}>נוכחות</Text>
                <Text style={styles.statValue}>{isLoadingInsights ? 'טוען...' : attendanceValue}</Text>
              </View>
            </View>
          </AppCard>

        {/* Events Card */}
        {achievements.length > 0 ? (
          <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.eventsCard}>
            <Text style={styles.sectionTitle}>הישגים אחרונים</Text>

            {achievements.map((item, idx) => (
              <View
                key={item.id}
                style={[
                  styles.eventRow,
                  idx === achievements.length - 1 && { borderBottomWidth: 0, paddingBottom: 0 }
                ]}
              >
                <View style={styles.eventInfoNoActions}>
                  <Text style={styles.eventLabel}>{item.label}</Text>
                  <Text style={styles.eventDesc}>{item.info}</Text>
                </View>

                <View style={styles.eventIconBg}>
                  <Text style={styles.eventIcon}>{item.icon}</Text>
                </View>
              </View>
            ))}
          </AppCard>
        ) : null}

        {/* Action Buttons */}
        <View style={styles.actionsContainer}>
          <PrimaryButton
            label="צ׳אט עם ההורה"
            onPress={() => {
              if (!parentId || !childId || !instructorId) {
                Alert.alert('שגיאת צ׳אט', 'לא ניתן לפתוח צ׳אט ללא מזהי הורה, ילד ומדריך.');
                return;
              }

              navigation.navigate('ChatPage', {
                fromInstructor: true,
                parentId,
                childId,
                instructorId,
                parentName,
                childName,
                instructorName,
              });
            }}
            style={styles.actionButton}
            textStyle={styles.actionBtnText}
            gradientStyle={styles.btnGradient}
          />
          <PrimaryButton
            label="דיווח התקדמות הילד"
            onPress={() => navigation.navigate('InstructorProgressReport', {
              child,
              childId: Number(child.childId || child.id || 0),
              groupId: groupId > 0 ? groupId : undefined,
              instructorId: instructorId > 0 ? instructorId : undefined,
              authUser: route?.params?.authUser,
            })}
            style={styles.actionButton}
            textStyle={styles.actionBtnText}
            gradientStyle={styles.btnGradient}
          />
        </View>

      </Animated.ScrollView>
    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  scrollContent: { paddingTop: 100, paddingBottom: 60, paddingHorizontal: 20 },
  waveContainer: { position: 'absolute', bottom: 0, left: 0, width: 2400, height: 150, zIndex: 0 },
  bubbleWrapper: { position: 'absolute', zIndex: 1 },

  navbar: {
    position: 'absolute', top: 0, left: 0, right: 0,
    paddingTop: Platform.OS === 'ios' ? 50 : (StatusBar.currentHeight || 20) + 15,
    paddingBottom: 15, paddingHorizontal: 20,
    flexDirection: 'row', justifyContent: 'flex-start', alignItems: 'center',
    zIndex: 100,
    borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  backButton: {
    width: 52,
    height: 52,
    borderRadius: 26,
    backgroundColor: 'rgba(255,255,255,0.14)',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.24)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  backButtonIcon: {
    color: '#FFFFFF',
    fontSize: 34,
    fontWeight: '700',
    lineHeight: 34,
  },

  // Profile Header
  profileHeader: { alignItems: 'center', marginTop: 20, marginBottom: 30, zIndex: 10 },
  avatarGlow: {
    width: 108,
    height: 108,
    borderRadius: 54,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(0, 130, 176, 0.2)',
    borderWidth: 1,
    borderColor: 'rgba(188, 244, 255, 0.34)',
    shadowColor: '#00d4ff', shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.6, shadowRadius: 20, elevation: 15,
    marginBottom: 16,
  },
  avatar: {
    width: 96, height: 96, borderRadius: 48,
    justifyContent: 'center', alignItems: 'center',
    overflow: 'hidden',
    borderWidth: 2, borderColor: 'rgba(255,255,255,0.6)',
  },
  avatarText: { color: '#001529', fontSize: 44, fontWeight: '900' },
  profileName: { color: '#fff', fontSize: 28, fontWeight: '800', writingDirection: 'rtl', marginBottom: 8 },
  metaBadge: {
    backgroundColor: 'rgba(0, 212, 255, 0.15)',
    paddingHorizontal: 16, paddingVertical: 6,
    borderRadius: 20, borderWidth: 1, borderColor: '#00d4ff',
  },
  metaText: { color: '#00d4ff', fontSize: 13, fontWeight: '700', writingDirection: 'rtl' },

  // Stats
  statsCard: {
    padding: 20, borderRadius: 24, marginBottom: 20, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  statsRow: { flexDirection: 'row', justifyContent: 'center' },
  statBox: { alignItems: 'center', flex: 1 },
  statBoxSingle: { alignItems: 'center', flex: 1 },
  statLabel: { color: 'rgba(255,255,255,0.7)', fontSize: 13, fontWeight: '600', writingDirection: 'rtl', marginBottom: 4, textAlign: 'center' },
  statValue: { color: '#00d4ff', fontSize: 20, fontWeight: '900', textAlign: 'center', writingDirection: 'rtl' },

  // Events Card
  eventsCard: {
    padding: 20, borderRadius: 24, marginBottom: 25, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  sectionTitle: { color: '#fff', fontSize: 20, fontWeight: '800', writingDirection: 'rtl', textAlign: 'right', marginBottom: 15 },
  emptyText: { color: 'rgba(255,255,255,0.6)', textAlign: 'right', writingDirection: 'rtl', fontStyle: 'italic' },
  eventRow: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    paddingVertical: 14, borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  eventInfoNoActions: { flex: 1, alignItems: 'flex-end', marginRight: 15 },
  actionButtons: { flexDirection: 'row', gap: 8 },
  smallBtn: {
    paddingHorizontal: 12, paddingVertical: 6, borderRadius: 12,
    borderWidth: 1, justifyContent: 'center', alignItems: 'center',
  },
  smallBtnText: { fontSize: 12, fontWeight: '800', writingDirection: 'rtl' },
  eventInfo: { flex: 1, alignItems: 'flex-end', marginRight: 15 },
  eventLabel: { color: '#fff', fontSize: 16, fontWeight: '700', writingDirection: 'rtl' },
  eventDesc: { color: 'rgba(255,255,255,0.6)', fontSize: 13, marginTop: 4, writingDirection: 'rtl' },
  eventIconBg: {
    width: 40, height: 40, borderRadius: 20,
    backgroundColor: 'rgba(255,255,255,0.1)',
    justifyContent: 'center', alignItems: 'center',
  },
  eventIcon: { fontSize: 18 },

  // Actions
  actionsContainer: { gap: 12, zIndex: 10 },
  actionButton: { borderRadius: 20, ...shadows.glowPrimary },
  actionButtonSecondary: {
    borderRadius: 20, borderWidth: 1, borderColor: 'rgba(255,255,255,0.2)',
  },
  actionBtnText: { color: '#001529', fontSize: 17, fontWeight: '800', writingDirection: 'rtl' },
  btnGradient: { paddingVertical: 16, alignItems: 'center', justifyContent: 'center' },
});








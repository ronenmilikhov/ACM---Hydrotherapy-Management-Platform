import React, { useRef, useEffect, useState, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, ScrollView, Alert, I18nManager,
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

const extractMetricsFromReport = (report) => {
  // New format: metrics is a direct array or JSON string on the report object
  let metrics = [];
  if (Array.isArray(report?.metrics)) {
    metrics = report.metrics;
  } else if (typeof report?.metrics === 'string' && report.metrics.trim()) {
    try {
      metrics = JSON.parse(report.metrics);
    } catch (e) { }
  }

  if (!Array.isArray(metrics)) {
    metrics = [];
  }

  if (metrics.length > 0) {
    return metrics
      .map((metric) => {
        const label = String(metric?.label || '').trim();
        const rawValue = Number(metric?.value);
        if (!label || !Number.isFinite(rawValue)) return null;
        // Normalize value out of 5 since front-end components treat it as out of 5
        let val = rawValue;
        if (val > 5) val = Math.round(val / 2);
        return { label, value: Math.max(0, Math.min(5, val)) };
      })
      .filter(Boolean);
  }

  // Backward-compatible: try parsing from notes JSON
  const notes = parseReportNotes(report?.notes);
  if (!notes || typeof notes !== 'object') return [];
  const noteMetrics = Array.isArray(notes?.metrics) ? notes.metrics : [];
  return noteMetrics
    .map((metric) => {
      const label = String(metric?.label || '').trim();
      const rawValue = Number(metric?.value);
      if (!label || !Number.isFinite(rawValue)) return null;
      let val = rawValue;
      if (val > 5) val = Math.round(val / 2);
      return { label, value: Math.max(0, Math.min(5, val)) };
    })
    .filter(Boolean);
};

const resolveReportPresence = (report) => {
  // New format: isPresent is a direct boolean on the report
  if (typeof report?.isPresent === 'boolean') return report.isPresent;
  if (typeof report?.isPresent === 'number') return report.isPresent > 0;

  // Backward-compatible: parse from notes
  const notes = parseReportNotes(report?.notes);
  if (typeof notes?.isPresent === 'boolean') return notes.isPresent;
  if (typeof notes?.isPresent === 'number') return notes.isPresent > 0;
  return true;
};

const resolveReportExercise = (report) => {
  // New format: exerciseKey and exerciseTitle are direct fields
  const exerciseKey = String(report?.exerciseKey || '').trim();
  const exerciseTitle = String(report?.exerciseTitle || '').trim();
  if (exerciseKey) return { exerciseKey, exerciseTitle: exerciseTitle || exerciseKey };

  // Backward-compatible: parse from notes
  const notes = parseReportNotes(report?.notes);
  const key = String(notes?.exerciseKey || '').trim();
  const title = String(notes?.exerciseTitle || '').trim();
  return { exerciseKey: key, exerciseTitle: title || key || '' };
};

const buildExerciseCompletionFromReports = (reports) => {
  const exerciseMap = {};

  (Array.isArray(reports) ? reports : []).forEach((report) => {
    if (!resolveReportPresence(report)) return;

    const { exerciseKey, exerciseTitle } = resolveReportExercise(report);
    if (!exerciseKey) return;

    const metrics = extractMetricsFromReport(report);
    if (metrics.length === 0) return;

    if (!exerciseMap[exerciseKey]) {
      exerciseMap[exerciseKey] = { exerciseKey, exerciseTitle, allValues: [] };
    }

    const maxMetricsPerExercise = 10;
    const sumOfMetrics = metrics.reduce((sum, m) => sum + m.value, 0);
    const avgForReport = sumOfMetrics / maxMetricsPerExercise;

    exerciseMap[exerciseKey].allValues.push(avgForReport);
  });

  return Object.values(exerciseMap)
    .map((entry) => {
      const overallAvg = entry.allValues.reduce((s, v) => s + v, 0) / entry.allValues.length;
      // Ratings are out of 5, so we divide by 5 and multiply by 100 to get a percentage
      const percent = Math.max(0, Math.min(100, Math.round((overallAvg / 5) * 100)));
      return {
        exerciseKey: entry.exerciseKey,
        exerciseTitle: entry.exerciseTitle,
        percent,
        reportsCount: entry.allValues.length,
      };
    })
    .sort((a, b) => b.percent - a.percent);
};

const buildChildAchievementsFromReports = (reports) => {
  const orderedReports = [...(Array.isArray(reports) ? reports : [])].sort((first, second) => {
    const byDate = getReportTimestamp(second) - getReportTimestamp(first);
    if (byDate !== 0) return byDate;
    return Number(second?.reportId || 0) - Number(first?.reportId || 0);
  });

  if (orderedReports.length === 0) {
    return {
      attendanceText: 'אין מידע זמין כרגע',
      achievements: [],
      exerciseCompletion: [],
    };
  }

  const totalReportsCount = orderedReports.length;
  const presentReportsCount = orderedReports.reduce((count, report) => {
    return count + (resolveReportPresence(report) ? 1 : 0);
  }, 0);

  const attendancePercent = Math.round((presentReportsCount / totalReportsCount) * 100);
  const attendanceText = `${attendancePercent}% (${presentReportsCount}/${totalReportsCount})`;
  const achievements = [];

  // 1. Attendance Achievement
  if (attendancePercent >= 80 && totalReportsCount >= 3) {
    achievements.push({
      id: 'attendance_gold',
      label: 'נוכחות רציפה ומצוינת',
      info: `הגיע/ה ל-${attendancePercent}% מהשיעורים!`,
      icon: '🏆',
      bgType: 'gold'
    });
  } else if (attendancePercent >= 60 && totalReportsCount >= 2) {
    achievements.push({
      id: 'attendance_silver',
      label: 'נוכחות עקבית',
      info: `מקפיד/ה להגיע בקביעות לשיעורים.`,
      icon: '✨',
      bgType: 'silver'
    });
  }

  // 2. Metrics Achievement (Top distinct metrics across reports)
  const metricCandidates = [];
  // סורקים עד 5 דיווחים אחרונים (ללא הגבלת ימים נוקשה כדי למנוע הישגים ריקים)
  const recentReportsForAchievements = orderedReports.slice(0, 5);
  recentReportsForAchievements.forEach((report) => {
    const reportTs = getReportTimestamp(report);
    const dateText = formatShortReportDate(report?.reportDate || report?.date);
    extractMetricsFromReport(report).forEach((metric) => {
      metricCandidates.push({
        ...metric,
        reportTimestamp: reportTs,
        dateText
      });
    });
  });

  // מיון קודם כל לפי ציון (ככל שיותר גבוה יותר טוב, עדיפות ל-5), ואז לפי תאריך (הכי חדש קודם)
  metricCandidates.sort((first, second) => {
    const byValue = second.value - first.value;
    if (byValue !== 0) return byValue;
    return second.reportTimestamp - first.reportTimestamp;
  });

  const highMetrics = metricCandidates.filter((metric) => metric.value >= 4); // רק ציונים של 4 ומעלה (מתוך 5)
  let metricsAdded = 0;
  const seenMetricLabels = new Set();

  for (const metric of highMetrics) {
    // מוודא שיש שוני מוחלט בין המדדים הנבחרים (השוואה של הסטרינג) – עד 2 מדדים שונים בלבד
    if (!seenMetricLabels.has(metric.label) && metricsAdded < 2) {
      seenMetricLabels.add(metric.label);
      achievements.push({
        id: `metric_achievement_${metricsAdded}_${metric.reportTimestamp}`,
        label: metricsAdded === 0 ? 'הצטיינות במדד' : 'ציון לשבח!',
        info: `${metric.label} (${Math.round(metric.value)}/5)${metric.dateText ? ` ב-${metric.dateText}` : ''}`,
        icon: metricsAdded === 0 ? '🌟' : '⭐',
        bgType: 'accent'
      });
      metricsAdded++;
    }
  }

  // 3. Exercise Completion / Dedication
  const exerciseCompletion = buildExerciseCompletionFromReports(reports);
  // בודקים ספציפית כמה תרגילים *שונים* תורגלו, לפי אורך המערך שמכיל תרגילים ייחודיים
  const distinctExercisesCount = exerciseCompletion.length;

  if (distinctExercisesCount >= 8) {
    achievements.push({
      id: 'exercise_master',
      label: 'חלוץ אימונים',
      info: `התנסה/תה בלא פחות מ-${distinctExercisesCount} תרגילים שונים!`,
      icon: '🔥',
      bgType: 'fire'
    });
  } else if (distinctExercisesCount >= 5) {
    achievements.push({
      id: 'exercise_active',
      label: 'מתרגל/ת פעיל/ה',
      info: `התנסה/תה ב-${distinctExercisesCount} תרגילים שונים עד כה.`,
      icon: '💪',
      bgType: 'blue'
    });
  }

  return {
    attendanceText,
    achievements: achievements.slice(0, 5), // Show up to 5 achievements to leave room for multiple metrics
    exerciseCompletion,
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
  const [exerciseCompletion, setExerciseCompletion] = useState([]);
  const [isLoadingInsights, setIsLoadingInsights] = useState(false);

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
            setAchievements(Array.isArray(nextInsights.achievements) ? nextInsights.achievements : []);
            setExerciseCompletion(Array.isArray(nextInsights.exerciseCompletion) ? nextInsights.exerciseCompletion : []);
          }
        } catch (_error) {
          if (!isCancelled) {
            setAttendanceValue('אין מידע זמין כרגע');
            setAchievements([]);
            setExerciseCompletion([]);
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
        <LinearGradient colors={['#00aed8', '#007fa7', '#004d73']} style={{ flex: 1 }} />
      </Animated.View>

      {/* Bottom Waves */}
      <Wave color="rgba(0, 153, 204, 0.3)" duration={8000} startPosition={0} d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z" />
      <Wave color="rgba(0, 212, 255, 0.25)" duration={6000} startPosition={-600} d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z" />

      {/* NAVBAR */}
      <AnimatedBlurView intensity={20} tint="dark" style={[styles.navbar, { backgroundColor: navBg }]}>
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backButton}>
          <Text style={styles.backButtonIcon}>{'\u203a'}</Text>
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

            {achievements.map((item, idx) => {
              const bgColors = item.bgType === 'gold' ? ['#FFD700', '#DAA520'] :
                item.bgType === 'silver' ? ['#C0C0C0', '#A9A9A9'] :
                  item.bgType === 'fire' ? ['#ff4e50', '#f9d423'] :
                    ['#00d4ff', '#0099cc'];
              return (
                <View
                  key={item.id}
                  style={[
                    styles.eventRow,
                    { 
                      backgroundColor: 'rgba(255,255,255,0.04)', 
                      borderRadius: 12, 
                      paddingHorizontal: 12, 
                      marginBottom: 8, 
                      borderWidth: 1, 
                      borderColor: 'rgba(255,255,255,0.05)',
                      flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row',
                    },
                    idx === achievements.length - 1 && { marginBottom: 0 }
                  ]}
                >
                  {Platform.OS === 'web' ? (
                    <>
                      <View style={[styles.eventInfoNoActions, { marginRight: 15 }]}>
                        <Text style={[styles.eventLabel, { textAlign: 'right' }]}>{item.label}</Text>
                        <Text style={[styles.eventDesc, { textAlign: 'right' }]}>{item.info}</Text>
                      </View>
                      <LinearGradient colors={bgColors} style={styles.eventIconBg}>
                        <Text style={styles.eventIcon}>{item.icon}</Text>
                      </LinearGradient>
                    </>
                  ) : (
                    <>
                      <LinearGradient colors={bgColors} style={styles.eventIconBg}>
                        <Text style={styles.eventIcon}>{item.icon}</Text>
                      </LinearGradient>
                      <View style={[styles.eventInfoNoActions, { marginLeft: 15, marginRight: 0, alignItems: 'flex-start', flex: 1 }]}>
                        <Text style={[styles.eventLabel, { textAlign: 'left' }]}>{item.label}</Text>
                        <Text style={[styles.eventDesc, { textAlign: 'left' }]}>{item.info}</Text>
                      </View>
                    </>
                  )}
                </View>
              )
            })}
          </AppCard>
        ) : null}

        {/* Personal Goals Card */}
        {child.personalGoals ? (() => {
          let goals = [];
          try {
            goals = typeof child.personalGoals === 'string' ? JSON.parse(child.personalGoals) : child.personalGoals;
          } catch (_e) { /* ignore */ }
          if (!Array.isArray(goals) || goals.length === 0) return null;
          return (
            <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.eventsCard}>
              <Text style={styles.sectionTitle}>יעדים אישיים</Text>
              {goals.map((goal, idx) => (
                <View
                  key={`pg-${idx}`}
                  style={[
                    styles.eventRow,
                    { 
                      backgroundColor: 'rgba(255,255,255,0.04)', 
                      borderRadius: 12, 
                      paddingHorizontal: 12, 
                      marginBottom: 8, 
                      borderWidth: 1, 
                      borderColor: 'rgba(255,255,255,0.05)',
                      flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row',
                    },
                    idx === goals.length - 1 && { marginBottom: 0 }
                  ]}
                >
                  {Platform.OS === 'web' ? (
                    <>
                      <View style={[styles.eventInfoNoActions, { marginRight: 15 }]}>
                        <Text style={[styles.eventLabel, { textAlign: 'right' }]}>
                          {goal.areaLabel}
                        </Text>
                        <Text style={[styles.eventDesc, { color: goal.type === 'weakness' ? '#f87171' : '#4ade80', textAlign: 'right' }]}>
                          {goal.typeLabel}
                        </Text>
                        <Text style={[styles.eventDesc, { textAlign: 'right' }]}>{goal.goal}</Text>
                      </View>
                      <LinearGradient
                        colors={goal.type === 'weakness' ? ['#ef4444', '#dc2626'] : ['#22c55e', '#16a34a']}
                        style={styles.eventIconBg}
                      >
                        <Text style={styles.eventIcon}>{goal.type === 'weakness' ? '🎯' : '⭐'}</Text>
                      </LinearGradient>
                    </>
                  ) : (
                    <>
                      <LinearGradient
                        colors={goal.type === 'weakness' ? ['#ef4444', '#dc2626'] : ['#22c55e', '#16a34a']}
                        style={styles.eventIconBg}
                      >
                        <Text style={styles.eventIcon}>{goal.type === 'weakness' ? '🎯' : '⭐'}</Text>
                      </LinearGradient>
                      <View style={[styles.eventInfoNoActions, { marginLeft: 15, marginRight: 0, alignItems: 'flex-start', flex: 1 }]}>
                        <Text style={[styles.eventLabel, { textAlign: 'left' }]}>
                          {goal.areaLabel}
                        </Text>
                        <Text style={[styles.eventDesc, { color: goal.type === 'weakness' ? '#f87171' : '#4ade80', textAlign: 'left' }]}>
                          {goal.typeLabel}
                        </Text>
                        <Text style={[styles.eventDesc, { textAlign: 'left' }]}>{goal.goal}</Text>
                      </View>
                    </>
                  )}
                </View>
              ))}
            </AppCard>
          );
        })() : null}

        {/* Exercise Completion Card */}
        {exerciseCompletion.length > 0 ? (
          <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.eventsCard}>
            <Text style={styles.sectionTitle}>התקדמות לפי תרגיל</Text>

            {exerciseCompletion.map((ex) => (
              <View key={ex.exerciseKey} style={styles.exerciseRow}>
                <View style={[styles.exerciseHeader, { flexDirection: 'row' }]}>
                  {Platform.OS === 'web' ? (
                    <>
                      <Text style={styles.exercisePercent}>{ex.percent}%</Text>
                      <Text style={styles.exerciseTitle}>{ex.exerciseTitle}</Text>
                    </>
                  ) : (
                    <>
                      <Text style={styles.exerciseTitle}>{ex.exerciseTitle}</Text>
                      <Text style={styles.exercisePercent}>{ex.percent}%</Text>
                    </>
                  )}
                </View>
                <View style={[styles.exerciseBarOuter, { flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row' }]}>
                  <LinearGradient
                    colors={ex.percent >= 70 ? ['#00d4ff', '#0099cc'] : ex.percent >= 40 ? ['#ffb347', '#ff8c00'] : ['#ff6b6b', '#ee5a5a']}
                    start={{ x: 0, y: 0 }} end={{ x: 1, y: 0 }}
                    style={[styles.exerciseBarFill, { width: `${ex.percent}%` }]}
                  />
                </View>
                <Text style={[styles.exerciseReportCount, { textAlign: Platform.OS === 'web' ? 'right' : 'left' }]}>
                  {ex.reportsCount} דיווחים
                </Text>
              </View>
            ))}
          </AppCard>
        ) : !isLoadingInsights ? (
          <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.eventsCard}>
            <Text style={styles.sectionTitle}>התקדמות לפי תרגיל</Text>
            <Text style={styles.emptyText}>אין עדיין דיווחי תרגילים עבור הילד</Text>
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
  waveContainer: { position: 'absolute', bottom: 0, left: (width - 2400) / 2, width: 2400, height: 150, zIndex: 0 },
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
  sectionTitle: { color: '#fff', fontSize: 20, fontWeight: '800', writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left', marginBottom: 15 },
  emptyText: { color: 'rgba(255,255,255,0.6)', textAlign: Platform.OS === 'web' ? 'right' : 'left', writingDirection: 'rtl', fontStyle: 'italic' },
  eventRow: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    paddingVertical: 14,
  },
  eventInfoNoActions: { flex: 1, alignItems: 'flex-end', marginRight: 15 },
  actionButtons: { flexDirection: 'row', gap: 8 },
  smallBtn: {
    paddingHorizontal: 12, paddingVertical: 6, borderRadius: 12,
    borderWidth: 1, justifyContent: 'center', alignItems: 'center',
  },
  smallBtnText: { fontSize: 12, fontWeight: '800', writingDirection: 'rtl' },
  eventInfo: { flex: 1, alignItems: 'flex-end', marginRight: 15 },
  eventLabel: { color: '#fff', fontSize: 16, fontWeight: '700', writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left' },
  eventDesc: { color: 'rgba(255,255,255,0.6)', fontSize: 13, marginTop: 4, writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left' },
  eventIconBg: {
    width: 40, height: 40, borderRadius: 20,
    backgroundColor: 'rgba(255,255,255,0.1)',
    justifyContent: 'center', alignItems: 'center',
  },
  eventIcon: { fontSize: 18 },
  // Exercise Progress
  exerciseRow: {
    marginBottom: 16,
  },
  exerciseHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  exercisePercent: {
    color: '#00d4ff',
    fontSize: 16,
    fontWeight: '800',
  },
  exerciseTitle: {
    color: '#fff',
    fontSize: 15,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  exerciseBarOuter: {
    height: 8,
    backgroundColor: 'rgba(255,255,255,0.1)',
    borderRadius: 4,
    overflow: 'hidden',
    flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row',
  },
  exerciseBarFill: {
    height: '100%',
    borderRadius: 4,
  },
  exerciseReportCount: {
    color: 'rgba(255,255,255,0.5)',
    fontSize: 12,
    marginTop: 4,
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  // Actions
  actionsContainer: { gap: 12, zIndex: 10 },
  actionButton: { borderRadius: 20, ...shadows.glowPrimary },
  actionButtonSecondary: {
    borderRadius: 20, borderWidth: 1, borderColor: 'rgba(255,255,255,0.2)',
  },
  actionBtnText: { color: '#001529', fontSize: 17, fontWeight: '800', writingDirection: 'rtl' },
  btnGradient: { paddingVertical: 16, alignItems: 'center', justifyContent: 'center' },
});








import React, { useRef, useEffect, useState, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, ScrollView, I18nManager,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useNavigation, useRoute } from '@react-navigation/native';
import AppCard from '../components/ui/AppCard';
import { colors, shadows } from '../theme/tokens';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';

const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);

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

const resolveReportPresence = (notes) => {
  if (typeof notes?.isPresent === 'boolean') {
    return notes.isPresent;
  }

  if (typeof notes?.isPresent === 'number') {
    return notes.isPresent > 0;
  }

  // Historical reports may not include isPresent; treat them as present.
  return true;
};

// ========================================
// 3. MAIN COMPONENT
// ========================================
export default function GroupDetails() {
  const navigation = useNavigation();
  const route = useRoute();

  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);
  const { group } = route.params || { group: { title: 'קבוצה', subtitle: '' } };
  const groupId = Number(group?.groupId || 0);

  const scrollY = useRef(new Animated.Value(0)).current;
  const [groupChildren, setGroupChildren] = useState([]);
  const [groupAttendanceText, setGroupAttendanceText] = useState('אין נתונים');
  const [upcomingSessionsCount, setUpcomingSessionsCount] = useState(0);
  const [isLoadingChildren, setIsLoadingChildren] = useState(false);
  const [loadError, setLoadError] = useState('');

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

  const loadChildren = useCallback(async () => {
    if (!instructorId || !groupId) {
      setGroupChildren([]);
      setGroupAttendanceText('אין נתונים');
      setUpcomingSessionsCount(0);
      setLoadError('לא זוהתה קבוצה או מדריך מחובר.');
      return;
    }
    try {
      setIsLoadingChildren(true);
      setLoadError('');
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/groups/${groupId}/children`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון את חברי הקבוצה כרגע.');
      }
      const childrenPayload = Array.isArray(payload?.children) ? payload.children : Array.isArray(payload) ? payload : [];
      const normalizedChildren = childrenPayload
        .map((child) => {
          const childId = Number(child?.childId || child?.id || 0);
          const parentId = Number(child?.parentId || child?.parentid || 0);
          const firstName = String(child?.firstName || '').trim();
          const lastName = String(child?.lastName || '').trim();
          const fullName = String(child?.fullName || `${firstName} ${lastName}`).trim();
          const parentEmail = String(child?.parentEmail || '').trim();
          const parentFullName = String(child?.parentFullName || '').trim();
          return {
            childId,
            parentId,
            name: fullName || `ילד ${childId}`,
            note: `פרטי ההורה: ${parentEmail || 'אין אימייל'} - ${parentFullName || 'שם לא ידוע'}`,
            parentFullName: parentFullName || 'לא ידוע',
          };
        })
        .filter((child) => child.childId > 0);

      const childIds = normalizedChildren
        .map((child) => Number(child.childId || 0))
        .filter((childId) => childId > 0);

      let nextAttendanceText = 'אין נתונים';
      let nextUpcomingSessionsCount = 0;

      if (childIds.length > 0) {
        try {
          const reportsByChildren = await Promise.all(
            childIds.map(async (childId) => {
              try {
                const reportsResponse = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports`);
                const reportsPayload = await reportsResponse.json().catch(() => null);

                if (!reportsResponse.ok || !Array.isArray(reportsPayload)) {
                  return [];
                }

                return reportsPayload;
              } catch (_error) {
                return [];
              }
            }),
          );

          let totalReports = 0;
          let presentReports = 0;

          reportsByChildren.forEach((reports) => {
            reports.forEach((report) => {
              totalReports += 1;
              if (resolveReportPresence(report)) {
                presentReports += 1;
              }
            });
          });

          if (totalReports > 0) {
            const attendancePercent = Math.round((presentReports / totalReports) * 100);
            nextAttendanceText = `${attendancePercent}%`;
          }

          const sessionsResponse = await fetch(`${API_BASE_URL}/instructor/${instructorId}/training-sessions`);
          const sessionsPayload = await sessionsResponse.json().catch(() => null);
          const sessions = sessionsResponse.ok && Array.isArray(sessionsPayload) ? sessionsPayload : [];
          const childIdsSet = new Set(childIds);

          nextUpcomingSessionsCount = sessions.filter((session) => childIdsSet.has(Number(session?.childId || 0))).length;
        } catch (_error) {
          nextAttendanceText = 'אין נתונים';
          nextUpcomingSessionsCount = 0;
        }
      }

      setGroupAttendanceText(nextAttendanceText);
      setUpcomingSessionsCount(nextUpcomingSessionsCount);
      setGroupChildren(normalizedChildren);
    } catch (error) {
      setGroupChildren([]);
      setGroupAttendanceText('אין נתונים');
      setUpcomingSessionsCount(0);
      setLoadError(error?.message || 'לא ניתן לטעון את חברי הקבוצה כרגע.');
    } finally {
      setIsLoadingChildren(false);
    }
  }, [groupId, instructorId]);

  useEffect(() => {
    loadChildren();
  }, [loadChildren]);

  const attendanceBarPercent = Math.max(
    0,
    Math.min(
      100,
      Number.parseInt(String(groupAttendanceText || '').replace(/[^0-9]/g, ''), 10) || 0,
    ),
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
          <Text style={styles.backIcon}>›</Text>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* MAIN CONTENT */}
      <Animated.ScrollView
        onScroll={Animated.event([{ nativeEvent: { contentOffset: { y: scrollY } } }], { useNativeDriver: false })}
        scrollEventThrottle={16}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.headerArea}>
          <Text style={styles.pageTitle}>{group.title}</Text>
          {group.subtitle ? <Text style={styles.pageSubtitle}>{group.subtitle}</Text> : null}
        </View>

        {/* Dashboard Stats */}
        <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.statsCard}>
          <View style={styles.statsRow}>
            <View style={styles.statBox}>
              <Text style={styles.statLabel}>נוכחות ממוצעת</Text>
              <Text style={[styles.statValue, groupAttendanceText === 'אין נתונים' && styles.statValueSmall]}>
                {isLoadingChildren ? '...' : groupAttendanceText}
              </Text>
            </View>
            <View style={styles.statBox}>
              <Text style={styles.statLabel}>שיעורים בקרוב</Text>
              <Text style={styles.statValue}>{isLoadingChildren ? '...' : String(upcomingSessionsCount)}</Text>
            </View>
          </View>

          <View style={styles.performanceBox}>
            <Text style={styles.statLabel}>נוכחות ממוצעת</Text>
            <View style={styles.progressBarBg}>
              <View style={[styles.progressBarFill, { width: `${attendanceBarPercent}%` }]} />
            </View>
            <Text style={[styles.performanceText, { textAlign: Platform.OS === 'ios' ? 'left' : 'right' }]}>
              {isLoadingChildren
                ? 'מחשב נוכחות ממוצעת...'
                : groupAttendanceText === 'אין נתונים'
                  ? 'אין כרגע נתוני נוכחות להצגה.'
                  : `${groupAttendanceText} נוכחות ממוצעת בקבוצה`}
            </Text>
          </View>
        </AppCard>

        <PrimaryButton
          label="הצג את פאנל ילדים כללי"
          style={styles.actionBtn}
          gradientStyle={styles.btnGradient}
          textStyle={styles.actionBtnText}
          onPress={() => navigation.navigate('ChildList', { authUser })}
        />

        {/* Member List */}
        <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.listCard}>
          <Text style={{ color: '#00d4ff', fontSize: 16, fontWeight: '700', writingDirection: 'rtl', textAlign: Platform.OS === 'ios' ? 'left' : 'right', marginBottom: 5 }}>
            שם המדריך: {authUser?.fullName || 'לא ידוע'}
          </Text>
          <Text style={[styles.sectionTitle, { textAlign: Platform.OS === 'ios' ? 'left' : 'right' }]}>ילדי הקבוצה</Text>

          {isLoadingChildren && (
            <Text style={styles.emptyText}>טוען חברי קבוצה...</Text>
          )}

          {!isLoadingChildren && loadError ? (
            <Text style={styles.emptyText}>{loadError}</Text>
          ) : null}
          {!isLoadingChildren && !loadError && groupChildren.map((member, index) => (
            <View key={String(member.childId)} style={[
              styles.memberRow,
              index === groupChildren.length - 1 && { borderBottomWidth: 0, paddingBottom: 0 }
            ]}>
              {/* Avatar on the far right in RTL */}
              <View style={styles.memberAvatarBg}>
                 <Text style={styles.memberAvatarText}>{member.name[0]}</Text>
              </View>

              {/* Text Wrapper in the middle */}
              <View style={styles.memberTextWrap}>
                <Text style={[
                  styles.memberName,
                  { textAlign: Platform.OS === 'ios' ? 'left' : 'right', alignSelf: 'stretch' }
                ]}>
                  {member.name}
                </Text>
                <Text style={[
                  styles.memberNote,
                  { textAlign: Platform.OS === 'ios' ? 'left' : 'right', alignSelf: 'stretch' }
                ]}>
                  {member.note}
                </Text>
              </View>

              {/* Profile button on the far left in RTL */}
              <TouchableOpacity
                style={styles.profileBtn}
                onPress={() => navigation.navigate('ChildProfile', {
                  child: member,
                  parentId: Number(member.parentId || 0),
                  groupName: group.title,
                  groupId,
                  instructorId,
                  authUser,
                })}
              >
                <Text style={styles.profileBtnText}>פרופיל ילד</Text>
              </TouchableOpacity>
            </View>
          ))}
        </AppCard>
      </Animated.ScrollView>
    </View>
  );
}

// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  scrollContent: { 
    paddingTop: Platform.OS === 'ios' ? 120 : (StatusBar.currentHeight || 20) + 80, 
    paddingBottom: 60, 
    paddingHorizontal: 20 
  },
  waveContainer: { position: 'absolute', bottom: 0, left: (width - 2400) / 2, width: 2400, height: 150, zIndex: 0 },

  navbar: {
    position: 'absolute', top: 0, left: 0, right: 0,
    paddingTop: Platform.OS === 'ios' ? 42 : (StatusBar.currentHeight || 20) + 6,
    paddingBottom: 8, paddingHorizontal: 16,
    flexDirection: 'row', justifyContent: 'flex-start', alignItems: 'center',
    zIndex: 100,
    borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  backButton: {
    width: 42,
    height: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.18)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  backIcon: {
    color: '#def6ff',
    fontSize: 22,
    textAlign: 'center',
  },

  headerArea: { alignItems: 'center', marginBottom: 50, marginTop: 10 },
  pageTitle: {
    fontSize: 32, fontWeight: '800', color: '#00d4ff',
    textShadowColor: 'rgba(0, 212, 255, 0.4)', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 15,
    writingDirection: 'rtl', textAlign: 'center',
  },
  pageSubtitle: {
    fontSize: 16, color: 'rgba(255,255,255,0.7)',
    marginTop: 8, writingDirection: 'rtl', textAlign: 'center',
  },

  statsCard: {
    padding: 20, borderRadius: 24, marginBottom: 20, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  statsRow: {
    flexDirection: 'row',
    justifyContent: 'space-around',
    marginBottom: 16,
    paddingHorizontal: 20,
    alignSelf: 'center',
    width: '100%',
    maxWidth: 320,
  },
  statBox: { alignItems: 'center', flex: 1 },
  statLabel: { color: 'rgba(255,255,255,0.7)', fontSize: 13, fontWeight: '600', writingDirection: 'rtl', marginBottom: 4, textAlign: 'center' },
  statValue: { color: '#00d4ff', fontSize: 32, fontWeight: '900', textAlign: 'center' },
  statValueSmall: { fontSize: 20, fontWeight: '700', textAlign: 'center' },
  performanceBox: { borderTopWidth: 1, borderTopColor: 'rgba(255,255,255,0.1)', paddingTop: 14 },
  progressBarBg: { height: 8, backgroundColor: 'rgba(255,255,255,0.1)', borderRadius: 10, overflow: 'hidden', marginVertical: 8 },
  progressBarFill: { height: '100%', backgroundColor: '#00d4ff', borderRadius: 10 },
  performanceText: { color: 'rgba(255,255,255,0.6)', fontSize: 13, writingDirection: 'rtl', textAlign: 'right' },

  actionBtn: { borderRadius: 20, overflow: 'hidden', marginBottom: 20, zIndex: 10, ...shadows.glowPrimary },
  btnGradient: { paddingVertical: 16, alignItems: 'center', justifyContent: 'center' },
  actionBtnText: { color: '#001529', fontWeight: '800', fontSize: 16, writingDirection: 'rtl' },

  listCard: {
    padding: 20, borderRadius: 24, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  sectionTitle: { color: '#fff', fontSize: 20, fontWeight: '800', writingDirection: 'rtl', textAlign: 'right', marginBottom: 15 },
  emptyText: { color: 'rgba(255,255,255,0.6)', textAlign: 'right', writingDirection: 'rtl', fontStyle: 'italic', marginBottom: 10 },
  memberRow: {
    flexDirection: 'row',
    gap: 15,
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: 14,
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(255,255,255,0.08)',
  },
  memberTextWrap: {
    flex: 1,
  },
  memberName: {
    color: '#fff',
    fontSize: 17,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  memberNote: {
    color: 'rgba(255,255,255,0.5)',
    fontSize: 13,
    marginTop: 4,
    writingDirection: 'rtl',
  },
  memberAvatarBg: {
    width: 44, height: 44, borderRadius: 22,
    backgroundColor: 'rgba(0,212,255,0.15)',
    borderWidth: 1, borderColor: 'rgba(0,212,255,0.3)',
    justifyContent: 'center', alignItems: 'center',
  },
  memberAvatarText: { color: '#00d4ff', fontSize: 20, fontWeight: '800' },
  profileBtn: {
    backgroundColor: 'rgba(0, 212, 255, 0.15)',
    paddingVertical: 6, paddingHorizontal: 12, borderRadius: 12,
    borderWidth: 1, borderColor: '#00d4ff',
  },
  profileBtnText: { color: '#00d4ff', fontSize: 13, fontWeight: '700', writingDirection: 'rtl' },
});

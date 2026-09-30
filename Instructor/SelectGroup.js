import React, { useRef, useEffect, useState, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, ScrollView, I18nManager,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useNavigation } from '@react-navigation/native';
import AppCard from '../components/ui/AppCard';
import { colors, shadows } from '../theme/tokens';
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


// ========================================
// 3. MAIN COMPONENT
// ========================================
export default function SelectGroup({ navigation, route }) {
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);

  const scrollY = useRef(new Animated.Value(0)).current;
  const [groups, setGroups] = useState([]);
  const [isLoadingGroups, setIsLoadingGroups] = useState(false);
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

  const loadGroups = useCallback(async () => {
    if (!instructorId) {
      setGroups([]);
      setLoadError('לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }
    try {
      setIsLoadingGroups(true);
      setLoadError('');
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/groups`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון קבוצות כרגע.');
      }
      const normalizedGroups = (Array.isArray(payload) ? payload : [])
        .map((group) => {
          const groupId = Number(group?.groupId || 0);
          const name = String(group?.name || `קבוצה ${groupId}`).trim();
          const description = String(group?.description || '').trim();
          const activeChildrenCount = Number(group?.activeChildrenCount || 0);
          return {
            groupId,
            title: name,
            subtitle: description || `ילדים פעילים: ${activeChildrenCount}`,
            activeChildrenCount,
          };
        })
        .filter((group) => group.groupId > 0);
      setGroups(normalizedGroups);
    } catch (error) {
      setGroups([]);
      setLoadError(error?.message || 'לא ניתן לטעון קבוצות כרגע.');
    } finally {
      setIsLoadingGroups(false);
    }
  }, [instructorId]);

  useEffect(() => {
    loadGroups();
  }, [loadGroups]);

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
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.navBackButton}>
          <Text style={styles.navBackIcon}>›</Text>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* MAIN CONTENT */}
      <Animated.ScrollView
        onScroll={Animated.event(
          [{ nativeEvent: { contentOffset: { y: scrollY } } }],
          { useNativeDriver: false },
        )}
        scrollEventThrottle={16}
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.headerArea}>
          <Text style={styles.pageTitle}>בחר קבוצה</Text>
          <Text style={styles.pageSubtitle}>בחר קבוצה לצפייה בפרטי השיעור</Text>
        </View>

        <View style={styles.listContainer}>
          {isLoadingGroups && (
            <AppCard useBlur blurIntensity={15} blurTint="dark" style={styles.helperCard}>
              <Text style={styles.helperTextMsg}>טוען קבוצות...</Text>
            </AppCard>
          )}

          {!isLoadingGroups && loadError ? (
            <AppCard useBlur blurIntensity={15} blurTint="dark" style={styles.helperCard}>
              <Text style={styles.helperTextMsg}>{loadError}</Text>
            </AppCard>
          ) : null}

          {!isLoadingGroups && !loadError && groups.length === 0 ? (
            <AppCard useBlur blurIntensity={15} blurTint="dark" style={styles.helperCard}>
              <Text style={styles.helperTextMsg}>לא נמצאו קבוצות פעילות למדריך זה.</Text>
            </AppCard>
          ) : null}

          {groups.map((g) => (
            <TouchableOpacity
              key={String(g.groupId)}
              activeOpacity={0.85}
              onPress={() => navigation.navigate('GroupDetails', { group: g, authUser })}
              style={{ marginBottom: 12 }}
            >
              <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.groupCard}>
                <View style={styles.groupTextWrap}>
                  <Text style={styles.groupTitle}>{g.title}</Text>
                  <Text style={styles.groupSubtitle}>{g.subtitle}</Text>
                </View>
                <View style={styles.groupArrowBg}>
                  <Text style={styles.groupArrow}>{I18nManager.isRTL ? '‹' : '›'}</Text>
                </View>
              </AppCard>
            </TouchableOpacity>
          ))}
        </View>

        <AppCard useBlur blurIntensity={15} blurTint="dark" style={[styles.helperCard, { marginTop: 20 }]}>
          <Text style={styles.helperTitle}>איך לבחור?</Text>
          <Text style={styles.helperDesc}>לחץ על קבוצה כדי לראות את כל התלמידים והשיעורים המתוזמנים. תועבר מיד למסך הקבוצה המפורט.</Text>
        </AppCard>
      </Animated.ScrollView>

    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  scrollContent: { 
    paddingTop: Platform.OS === 'ios' ? 120 : (StatusBar.currentHeight || 20) + 80, 
    paddingBottom: 60, 
    paddingHorizontal: 20 
  },
  waveContainer: {
    position: 'absolute', bottom: 0, left: (width - 2400) / 2,
    width: 2400, height: 150, zIndex: 0,
  },

  navbar: {
    position: 'absolute', top: 0, left: 0, right: 0,
    paddingTop: Platform.OS === 'ios' ? 42 : (StatusBar.currentHeight || 20) + 6,
    paddingBottom: 8, paddingHorizontal: 16,
    flexDirection: 'row', justifyContent: 'flex-start', alignItems: 'center',
    zIndex: 100,
    borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  navBackButton: {
    width: 42,
    height: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.18)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  navBackIcon: {
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

  listContainer: { zIndex: 10 },

  groupCard: {
    padding: 18,
    flexDirection: I18nManager.isRTL ? 'row' : 'row-reverse',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.18)',
    borderRadius: 20,
  },
  groupTextWrap: {
    flex: 1,
    alignItems: 'stretch',
    marginHorizontal: 24,
  },
  groupTitle: {
    color: '#fff',
    fontSize: 20,
    fontWeight: '700',
    writingDirection: 'rtl',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
  },
  groupSubtitle: {
    color: 'rgba(255,255,255,0.5)',
    fontSize: 14,
    marginTop: 4,
    writingDirection: 'rtl',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
  },
  groupArrowBg: {
    width: 36, height: 36, borderRadius: 18,
    backgroundColor: 'rgba(0,212,255,0.15)',
    justifyContent: 'center', alignItems: 'center',
  },
  groupArrow: { color: '#00d4ff', fontSize: 22, fontWeight: 'bold' },

  helperCard: {
    padding: 18, borderRadius: 20,
    backgroundColor: 'rgba(255,255,255,0.05)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.1)',
  },
  helperTextMsg: {
    color: 'rgba(255,255,255,0.7)', fontSize: 16, textAlign: 'center', writingDirection: 'rtl',
  },
  helperTitle: { color: '#00d4ff', fontSize: 16, fontWeight: '700', marginBottom: 6, textAlign: Platform.OS === 'ios' ? 'left' : 'right', writingDirection: 'rtl' },
  helperDesc: { color: 'rgba(255,255,255,0.6)', fontSize: 14, textAlign: Platform.OS === 'ios' ? 'left' : 'right', writingDirection: 'rtl', lineHeight: 22 },
});


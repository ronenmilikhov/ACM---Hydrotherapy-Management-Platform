import React, { useRef, useEffect, useState, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, ScrollView,
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
export default function ChildList({ route }) {
  const navigation = useNavigation();
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);

  const scrollY = useRef(new Animated.Value(0)).current;
  const [groupSections, setGroupSections] = useState([]);
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

  const loadChildrenByGroups = useCallback(async () => {
    if (!instructorId) {
      setGroupSections([]);
      setLoadError('לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }
    try {
      setIsLoadingChildren(true);
      setLoadError('');
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children-by-groups`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון את רשימת הילדים כרגע.');
      }
      const normalizedGroups = (Array.isArray(payload) ? payload : [])
        .map((group) => {
          const groupId = Number(group?.groupId || 0);
          const groupName = String(group?.groupName || group?.name || `קבוצה ${groupId}`).trim();
          const groupDescription = String(group?.groupDescription || group?.description || '').trim();
          const children = (Array.isArray(group?.children) ? group.children : [])
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
                parentLine: parentEmail
                  ? `הורה: ${parentEmail}${parentFullName ? ` - ${parentFullName}` : ''}`
                  : 'ללא פרטי הורה',
              };
            })
            .filter((child) => child.childId > 0);
          return { groupId, groupName, groupDescription, children };
        })
        .filter((group) => group.groupId > 0);
      setGroupSections(normalizedGroups);
    } catch (error) {
      setGroupSections([]);
      setLoadError(error?.message || 'לא ניתן לטעון את רשימת הילדים כרגע.');
    } finally {
      setIsLoadingChildren(false);
    }
  }, [instructorId]);

  useEffect(() => {
    loadChildrenByGroups();
  }, [loadChildrenByGroups]);

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
          <Text style={styles.pageTitle}>בחר ילד</Text>
          <Text style={styles.pageSubtitle}>בחר את הילד כדי לצפות בפרטיו המלאים</Text>
        </View>

        <View style={styles.listContainer}>
          {isLoadingChildren && (
            <AppCard useBlur blurIntensity={15} blurTint="dark" style={styles.helperCard}>
              <Text style={styles.helperTextMsg}>טוען ילדים...</Text>
            </AppCard>
          )}

          {!isLoadingChildren && loadError ? (
            <AppCard useBlur blurIntensity={15} blurTint="dark" style={styles.helperCard}>
              <Text style={styles.helperTextMsg}>{loadError}</Text>
            </AppCard>
          ) : null}

          {!isLoadingChildren && !loadError && groupSections.length === 0 ? (
            <AppCard useBlur blurIntensity={15} blurTint="dark" style={styles.helperCard}>
              <Text style={styles.helperTextMsg}>לא נמצאו קבוצות פעילות עם ילדים.</Text>
            </AppCard>
          ) : null}

          {groupSections.map((group) => (
            <View key={String(group.groupId)} style={styles.groupSection}>
              <View style={styles.groupHeaderRow}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.groupName}>{group.groupName}</Text>
                  {group.groupDescription ? <Text style={styles.groupDesc}>{group.groupDescription}</Text> : null}
                </View>
                <View style={styles.groupIconBg}>
                  <Text style={styles.groupIcon}>🏊</Text>
                </View>
              </View>

              {group.children.length === 0 ? (
                <AppCard useBlur blurIntensity={15} blurTint="dark" style={[styles.helperCard, { marginTop: 10 }]}>
                  <Text style={styles.helperTextMsg}>אין ילדים פעילים בקבוצה זו.</Text>
                </AppCard>
              ) : (
                group.children.map((child) => (
                  <TouchableOpacity
                    key={String(child.childId)}
                    activeOpacity={0.85}
                    onPress={() => navigation.navigate('ChildProfile', {
                      child: {
                        childId: child.childId,
                        parentId: child.parentId,
                        name: child.name,
                        class: group.groupName,
                        status: 'פעיל',
                        parentFullName: child.parentLine,
                      },
                      groupName: group.groupName,
                      groupId: group.groupId,
                      instructorId,
                      authUser,
                    })}
                    style={{ marginTop: 10 }}
                  >
                    <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.childCard}>
                      <View style={styles.childTextWrap}>
                        <Text style={styles.childName}>{child.name}</Text>
                        <Text style={styles.childParent}>{child.parentLine}</Text>
                      </View>
                      <View style={styles.childAvatarBg}>
                        <Text style={styles.childAvatarText}>{child.name[0]}</Text>
                      </View>
                    </AppCard>
                  </TouchableOpacity>
                ))
              )}
            </View>
          ))}
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

  listContainer: { zIndex: 10 },

  helperCard: {
    padding: 18, borderRadius: 20,
    backgroundColor: 'rgba(255,255,255,0.05)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.1)',
  },
  helperTextMsg: {
    color: 'rgba(255,255,255,0.7)', fontSize: 16, textAlign: 'center', writingDirection: 'rtl',
  },

  groupSection: { marginTop: 10, marginBottom: 20 },
  groupHeaderRow: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end',
    backgroundColor: 'rgba(0, 212, 255, 0.15)',
    paddingVertical: 12, paddingHorizontal: 16, borderRadius: 16,
    borderWidth: 1, borderColor: 'rgba(0, 212, 255, 0.3)',
  },
  groupName: { color: '#fff', fontWeight: '800', fontSize: 18, writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left' },
  groupDesc: { color: 'rgba(255,255,255,0.7)', fontSize: 13, marginTop: 2, writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left' },
  groupIconBg: {
    width: 40, height: 40, borderRadius: 20, marginLeft: 15,
    backgroundColor: 'rgba(255,255,255,0.2)', justifyContent: 'center', alignItems: 'center',
  },
  groupIcon: { fontSize: 20 },

  childCard: {
    padding: 16, flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end',
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.18)',
    borderRadius: 20,
  },
  childTextWrap: { flex: 1, alignItems: Platform.OS === 'web' ? 'flex-end' : 'flex-start', marginRight: Platform.OS === 'web' ? 15 : 0, marginLeft: Platform.OS === 'web' ? 0 : 15 },
  childName: { color: '#fff', fontSize: 18, fontWeight: '700', writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left' },
  childParent: { color: 'rgba(255,255,255,0.5)', fontSize: 13, marginTop: 4, writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left' },
  childAvatarBg: {
    width: 46, height: 46, borderRadius: 23,
    backgroundColor: 'rgba(0,212,255,0.2)',
    borderWidth: 1, borderColor: 'rgba(0,212,255,0.5)',
    justifyContent: 'center', alignItems: 'center',
  },
  childAvatarText: { color: '#00d4ff', fontSize: 20, fontWeight: '800' },
});


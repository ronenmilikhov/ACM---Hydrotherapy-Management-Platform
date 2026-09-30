import React, { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  TextInput, StatusBar, Easing, KeyboardAvoidingView, Platform, Alert, Switch, Modal
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import PrimaryButton from './components/ui/PrimaryButton';
import AppCard from './components/ui/AppCard';
import LoginLoadingOverlay from './components/ui/LoginLoadingOverlay';
import { colors, radius, shadows } from './theme/tokens';
import { API_BASE_URL } from './apiConfig';

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
    <Animated.View
      style={[styles.waveContainer, { transform: [{ translateX }] }]}
    >
      <Svg
        width={2400}
        height={150}
        viewBox="0 0 2400 150"
        preserveAspectRatio="none"
      >
        <Path fill={color} d={d} />
      </Svg>
    </Animated.View>
  );
};


export default function LoginPage({ navigation }) {
  const scrollY = useRef(new Animated.Value(0)).current;

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(true);
  const [isPasswordVisible, setIsPasswordVisible] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Login overlay state
  const [loginOverlayVisible, setLoginOverlayVisible] = useState(false);
  const [loginStatus, setLoginStatus] = useState('loading');    // 'loading' | 'success' | 'error'
  const [loginMessage, setLoginMessage] = useState('');
  const pendingNavRef = useRef(null);  // store pending navigation

  const [resetModalVisible, setResetModalVisible] = useState(false);
  const [resetStep, setResetStep] = useState('email');
  const [resetEmail, setResetEmail] = useState('');
  const [resetCode, setResetCode] = useState('');
  const [resetNewPassword, setResetNewPassword] = useState('');
  const [resetConfirmPassword, setResetConfirmPassword] = useState('');
  const [resetLoading, setResetLoading] = useState(false);

  const isFormValid = useMemo(() => email.trim().length > 0 && password.length > 0, [email, password]);

  const handleLogin = useCallback(async (e = null, optEmail = null, optPassword = null) => {
    const actualEmail = (typeof optEmail === 'string' ? optEmail : email)
      .replace(/[\u200B-\u200D\u200E\u200F\uFEFF]/g, '')
      .trim()
      .toLowerCase();
    const actualPassword = typeof optPassword === 'string' ? optPassword : password;

    if (!actualEmail || !actualPassword) return;

    // Show overlay in loading state
    pendingNavRef.current = null;
    setLoginStatus('loading');
    setLoginMessage('מתחבר...');
    setLoginOverlayVisible(true);
    setIsSubmitting(true);

    try {
      const response = await fetch(`${API_BASE_URL}/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: actualEmail, password: actualPassword }),
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok || !payload) {
        const apiMessage = String(payload?.message || '').trim();
        const normalizedApiMessage = apiMessage.toLowerCase();
        const translatedMessage = (
          normalizedApiMessage === 'invalid email or password.'
          || normalizedApiMessage === 'invalid email or password'
        )
          ? 'אימייל או סיסמה שגויים.'
          : (apiMessage || 'אימייל או סיסמה שגויים.');

        setLoginStatus('error');
        setLoginMessage(translatedMessage);
        // Overlay will self-dismiss via onDismiss callback
        return;
      }

      const role = (payload?.role ?? '').toLowerCase();
      let navTarget = null;
      let navParams = null;

      if (role === 'manager') {
        const managerToken = String(payload?.token || '').trim();
        if (!managerToken) {
          setLoginStatus('error');
          setLoginMessage('לא התקבל אסימון התחברות למנהל.');
          return;
        }
        navTarget = 'ManagerHomepage';
        navParams = {
          managerAuth: {
            id: payload?.id,
            email: actualEmail,
            fullName: payload?.fullName || 'מנהל',
            token: managerToken,
            tokenExpiresAtUtc: payload?.tokenExpiresAtUtc || null,
          },
        };
      } else if (role === 'instructor') {
        navTarget = 'InstructorHomepage';
        navParams = { authUser: payload };
      } else if (role === 'parent') {
        navTarget = 'ParentHomepage';
        navParams = { authUser: payload };
      } else {
        setLoginStatus('error');
        setLoginMessage('לא זוהתה הרשאת משתמש מתאימה.');
        return;
      }

      // Success!
      setLoginStatus('success');
      setLoginMessage('התחברת בהצלחה!');
      pendingNavRef.current = { screen: navTarget, params: navParams };

      // Navigate after a brief success animation delay
      setTimeout(() => {
        setLoginOverlayVisible(false);
        setIsSubmitting(false);
        if (pendingNavRef.current) {
          navigation.replace(pendingNavRef.current.screen, pendingNavRef.current.params);
          pendingNavRef.current = null;
        }
      }, 1500);

    } catch (error) {
      setLoginStatus('error');
      const apiDebugLine = __DEV__ ? `\nכתובת API: ${API_BASE_URL}` : '';
      setLoginMessage(`שגיאת שרת — לא ניתן להתחבר כרגע.${apiDebugLine}`);
    }
  }, [email, password, navigation]);

  const handleOverlayDismiss = useCallback(() => {
    setLoginOverlayVisible(false);
    setIsSubmitting(false);
  }, []);

  const openResetModal = () => {
    setResetEmail('');
    setResetModalVisible(true);
  };

  const handleRequestCode = async () => {
    if (!resetEmail.trim()) {
      Alert.alert('שגיאה', 'יש להזין כתובת אימייל.');
      return;
    }
    try {
      setResetLoading(true);
      const cleanedResetEmail = resetEmail.replace(/[\u200B-\u200D\u200E\u200F\uFEFF]/g, '').trim().toLowerCase();
      const response = await fetch(`${API_BASE_URL}/PasswordReset/request`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: cleanedResetEmail }),
      });
      const data = await response.json().catch(() => null);
      if (response.ok) {
        Alert.alert('נשלח קישור לאיפוס', data?.message || 'אימייל לאיפוס סיסמה נשלח אליך! אנא עקוב אחר ההוראות באימייל שנשלח.');
        setResetModalVisible(false);
      } else {
        Alert.alert('שגיאה', data?.message || 'שגיאה בבקשת איפוס סיסמה. נסו שוב.');
      }
    } catch (error) {
      Alert.alert('שגיאה', 'לא ניתן לשלוח את הבקשה כרגע. נסו שוב מאוחר יותר.');
    } finally {
      setResetLoading(false);
    }
  };

  const navBg = scrollY.interpolate({
    inputRange: [0, 50],
    outputRange: ["rgba(0, 21, 41, 0.4)", "rgba(0, 21, 41, 0.85)"],
    extrapolate: "clamp",
  });

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent={true} />

      {/* Deep Ocean Base */}
      <LinearGradient colors={["#005a80", "#00456a", colors.bgDeep]} style={StyleSheet.absoluteFill} />

      {/* Surface Water */}
      <View style={[StyleSheet.absoluteFill]}>
        <LinearGradient colors={["#00aed8", "#007fa7", "#004d73"]} style={{ flex: 1 }} />
      </View>

      {/* Bottom Aligned Waves */}
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
        <View style={styles.navBrand}>
          <LinearGradient colors={["#00d4ff", "#0099cc"]} style={styles.logoIcon}>
            <Text style={styles.logoEmoji}>🐚</Text>
          </LinearGradient>
          <Text style={styles.logoText}>ACM</Text>
        </View>
      </AnimatedBlurView>

      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.keyboardWrap}
      >
        <Animated.ScrollView
          onScroll={Animated.event(
            [{ nativeEvent: { contentOffset: { y: scrollY } } }],
            { useNativeDriver: false },
          )}
          scrollEventThrottle={16}
          contentContainerStyle={styles.scrollContent}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {/* LOGIN CARD */}
          <AppCard useBlur blurIntensity={30} blurTint="dark" style={styles.loginCard}>
            <View style={styles.loginIconContainer}>
              <LinearGradient colors={["#00d4ff", "#0099cc"]} style={styles.loginIconGradient}>
                <Text style={styles.loginEmoji}>🐚</Text>
              </LinearGradient>
            </View>
            <Text style={styles.loginHeader}>ברוכים השבים</Text>
            <Text style={styles.loginSub}>התחברו לחשבון שלכם ב־ACM</Text>

            <View style={styles.inputGroup}>
              <Text style={styles.label}>כתובת אימייל</Text>
              <TextInput
                style={styles.input}
                placeholder="הכניסו אימייל"
                placeholderTextColor="rgba(255,255,255,0.4)"
                textAlign="right"
                keyboardType="email-address"
                autoCapitalize="none"
                value={email}
                onChangeText={setEmail}
              />
            </View>

            <View style={styles.inputGroup}>
              <Text style={styles.label}>סיסמה</Text>
              <View style={styles.passwordRow}>
                <TextInput
                  style={styles.passwordInput}
                  placeholder="הכניסו סיסמה"
                  placeholderTextColor="rgba(255,255,255,0.4)"
                  textAlign="right"
                  secureTextEntry={!isPasswordVisible}
                  value={password}
                  onChangeText={setPassword}
                />
                <TouchableOpacity style={styles.eyeIconButton} onPress={() => setIsPasswordVisible(!isPasswordVisible)}>
                  <Text style={styles.eyeIcon}>{isPasswordVisible ? '🙈' : '👁'}</Text>
                </TouchableOpacity>
              </View>
            </View>

            <View style={styles.metaRow}>
              <View style={styles.toggleRow}>
                <Switch
                  value={rememberMe}
                  onValueChange={setRememberMe}
                  trackColor={{ false: 'rgba(255,255,255,0.2)', true: '#00d4ff' }}
                  thumbColor={rememberMe ? '#fff' : 'rgba(255,255,255,0.4)'}
                />
                <Text style={styles.metaText}>זכור אותי</Text>
              </View>
              <TouchableOpacity activeOpacity={0.8} onPress={openResetModal}>
                <Text style={styles.forgotText}>שכחת סיסמה?</Text>
              </TouchableOpacity>
            </View>

            <PrimaryButton
              label={isSubmitting ? 'מתחבר...' : 'התחברות'}
              style={styles.loginBtn}
              gradientStyle={styles.btnGradient}
              textStyle={styles.primaryBtnText}
              onPress={() => handleLogin()}
              disabled={!isFormValid || isSubmitting}
              colorsOverride={
                (!isFormValid || isSubmitting) ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)'] : null
              }
            />

            <View style={styles.quickLoginContainer}>
              <View style={styles.quickLoginButtons}>
                <TouchableOpacity 
                  style={styles.quickLoginBtn} 
                  onPress={() => {
                    setEmail('manager@acm.com');
                    setPassword('123456');
                    handleLogin(null, 'manager@acm.com', '123456');
                  }}
                >
                  <Text style={styles.quickLoginBtnText}>Manager</Text>
                </TouchableOpacity>


                <TouchableOpacity 
                  style={styles.quickLoginBtn} 
                  onPress={() => {
                    setEmail('christian@gmail.com');
                    setPassword('123456');
                    handleLogin(null, 'christian@gmail.com', '123456');
                  }}
                >
                  <Text style={styles.quickLoginBtnText}>Instructor</Text>
                </TouchableOpacity>

                <TouchableOpacity 
                  style={styles.quickLoginBtn} 
                  onPress={() => {
                    setEmail('shlomi@gmail.com');
                    setPassword('123456');
                    handleLogin(null, 'shlomi@gmail.com', '123456');
                  }}
                >
                  <Text style={styles.quickLoginBtnText}>Parent</Text>
                </TouchableOpacity>
              </View>
            </View>
          </AppCard>
        </Animated.ScrollView>
      </KeyboardAvoidingView>

      {/* Password Reset Modal */}
      <Modal visible={resetModalVisible} transparent animationType="fade" onRequestClose={() => setResetModalVisible(false)}>
        <KeyboardAvoidingView style={styles.resetOverlay} behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>
          <View style={styles.resetCard}>
            <Text style={styles.resetTitle}>איפוס סיסמה</Text>
            <Text style={styles.resetDescription}>
              הזינו את כתובת האימייל שלכם ונשלח לכם קישור מאובטח לאיפוס הסיסמה.
            </Text>
            <TextInput
              style={styles.resetInput}
              placeholder="כתובת אימייל"
              placeholderTextColor="rgba(255,255,255,0.4)"
              textAlign="right"
              keyboardType="email-address"
              autoCapitalize="none"
              value={resetEmail}
              onChangeText={setResetEmail}
            />
            <TouchableOpacity style={styles.resetButton} onPress={handleRequestCode} disabled={resetLoading} activeOpacity={0.8}>
              <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.resetButtonGradient}>
                <Text style={styles.resetButtonText}>{resetLoading ? 'שולח...' : 'שלח קישור לאיפוס'}</Text>
              </LinearGradient>
            </TouchableOpacity>

            <TouchableOpacity style={styles.resetCancelButton} onPress={() => setResetModalVisible(false)} activeOpacity={0.8}>
              <Text style={styles.resetCancelText}>ביטול</Text>
            </TouchableOpacity>
          </View>
        </KeyboardAvoidingView>
      </Modal>

      {/* ✨ Animated Login Overlay ✨ */}
      <LoginLoadingOverlay
        visible={loginOverlayVisible}
        status={loginStatus}
        message={loginMessage}
        onDismiss={handleOverlayDismiss}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep, overflow: 'hidden' },
  keyboardWrap: { flex: 1 },
  scrollContent: {
    flexGrow: 1,
    justifyContent: 'center',
    paddingTop: 70,
    paddingBottom: 60,
    paddingHorizontal: "5%",
  },
  waveContainer: {
    position: "absolute",
    bottom: 0,
    left: (width - 2400) / 2,
    width: 2400,
    height: 150,
    zIndex: 0,
  },

  // --- NAVBAR ---
  navbar: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    paddingTop: Platform.OS === "ios" ? 42 : (StatusBar.currentHeight || 20) + 6,
    paddingBottom: 8,
    paddingHorizontal: 16,
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    zIndex: 100,
    borderBottomWidth: 1,
    borderBottomColor: "rgba(255,255,255,0.1)",
  },
  navBrand: { flexDirection: "row", alignItems: "center" },
  logoIcon: {
    width: 34,
    height: 34,
    borderRadius: 17,
    justifyContent: "center",
    alignItems: "center",
    marginRight: 10,
  },
  logoEmoji: { fontSize: 18, includeFontPadding: false },
  logoText: {
    color: "#fff",
    fontSize: 28,
    fontWeight: "900",
    includeFontPadding: false,
    letterSpacing: 2,
  },

  // --- LOGIN CARD ---
  loginCard: {
    borderRadius: 30,
    padding: 40,
    backgroundColor: "rgba(255, 255, 255, 0.08)",
    borderWidth: 1,
    borderColor: "rgba(255, 255, 255, 0.18)",
    overflow: "hidden",
    zIndex: 10,
  },
  loginIconContainer: { alignItems: "center", marginBottom: 20 },
  loginIconGradient: {
    width: 70,
    height: 70,
    borderRadius: 20,
    justifyContent: "center",
    alignItems: "center",
    shadowColor: "#00d4ff",
    shadowOpacity: 0.3,
    shadowRadius: 20,
  },
  loginEmoji: { fontSize: 32 },
  loginHeader: {
    color: "#fff",
    fontSize: 24,
    fontWeight: "700",
    textAlign: "center",
    marginBottom: 8,
  },
  loginSub: {
    color: "rgba(255, 255, 255, 0.7)",
    textAlign: "center",
    marginBottom: 35,
    fontSize: 14,
  },

  inputGroup: { marginBottom: 25 },
  label: {
    color: "rgba(255, 255, 255, 0.5)",
    fontSize: 12,
    marginBottom: 8,
    paddingRight: 2,
    textAlign: 'right',
  },
  input: {
    color: "#fff",
    fontSize: 16,
    paddingVertical: 14,
    paddingHorizontal: 20,
    backgroundColor: "rgba(255, 255, 255, 0.05)",
    borderWidth: 1,
    borderColor: "rgba(255, 255, 255, 0.15)",
    borderRadius: 30, // Pill shaped!
    writingDirection: 'rtl',
  },
  passwordRow: {
    position: "relative",
    backgroundColor: "rgba(255, 255, 255, 0.05)",
    borderWidth: 1,
    borderColor: "rgba(255, 255, 255, 0.15)",
    borderRadius: 30,
    overflow: "hidden",
  },
  passwordInput: {
    color: "#fff",
    fontSize: 16,
    paddingVertical: 14,
    paddingStart: 20,
    paddingEnd: 56,
    backgroundColor: "transparent",
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  eyeIconButton: {
    position: "absolute",
    right: 0,
    top: 0,
    bottom: 0,
    width: 50,
    justifyContent: "center",
    alignItems: "center",
  },
  eyeIcon: { 
    fontSize: 18, 
    opacity: 0.8,
  },

  metaRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 40,
  },
  toggleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  metaText: {
    color: 'rgba(255, 255, 255, 0.7)',
    fontSize: 12,
  },
  forgotText: {
    color: '#00d4ff',
    fontSize: 13,
  },

  loginBtn: { borderRadius: 30, overflow: "hidden" },
  btnGradient: {
    paddingVertical: 16,
    alignItems: "center",
    justifyContent: "center",
  },
  primaryBtnText: { color: "#001529", fontWeight: "bold", fontSize: 16 },

  quickLoginContainer: {
    marginTop: 20,
    alignItems: 'center'
  },
  quickLoginButtons: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    width: '100%',
    gap: 10
  },
  quickLoginBtn: {
    flex: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.1)',
    paddingVertical: 10,
    borderRadius: 20,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.2)'
  },
  quickLoginBtnText: {
    color: '#00d4ff',
    fontSize: 12,
    fontWeight: '600'
  },
  resetOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 15, 30, 0.85)',
    justifyContent: 'center',
    paddingHorizontal: 24,
  },
  resetCard: {
    backgroundColor: 'rgba(0, 51, 102, 0.95)',
    borderRadius: 20,
    padding: 24,
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.25)',
  },
  resetTitle: {
    color: '#fff',
    fontSize: 22,
    fontWeight: '800',
    textAlign: 'center',
    marginBottom: 8,
  },
  resetDescription: {
    color: 'rgba(255,255,255,0.7)',
    fontSize: 14,
    textAlign: 'center',
    marginBottom: 20,
    lineHeight: 20,
  },
  resetInput: {
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.2)',
    borderRadius: 12,
    paddingHorizontal: 16,
    paddingVertical: 14,
    color: '#fff',
    fontSize: 15,
    marginBottom: 12,
    textAlign: 'right',
  },
  resetButton: {
    marginTop: 8,
    borderRadius: 12,
    overflow: 'hidden',
  },
  resetButtonGradient: {
    paddingVertical: 14,
    alignItems: 'center',
    borderRadius: 12,
  },
  resetButtonText: {
    color: '#fff',
    fontSize: 16,
    fontWeight: '700',
  },
  resetCancelButton: {
    marginTop: 14,
    alignItems: 'center',
    paddingVertical: 10,
  },
  resetCancelText: {
    color: 'rgba(255,255,255,0.6)',
    fontSize: 14,
    fontWeight: '600',
  },
});

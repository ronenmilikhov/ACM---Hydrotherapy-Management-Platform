import React, { useState, useEffect, useRef, useMemo } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  TextInput, StatusBar, Easing, KeyboardAvoidingView, Platform, Alert, Switch
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import PrimaryButton from './components/ui/PrimaryButton';
import AppCard from './components/ui/AppCard';
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
        Animated.timing(sway, {
          toValue: 1,
          duration: 2000,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
        Animated.timing(sway, {
          toValue: -1,
          duration: 4000,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
        Animated.timing(sway, {
          toValue: 0,
          duration: 2000,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
      ]),
    );
    swayAnim.start();

    return () => {
      progress.stopAnimation();
      sway.stopAnimation();
    };
  }, []);

  const translateY = progress.interpolate({
    inputRange: [0, 1],
    outputRange: [height + 50, -150],
  });

  const opacity = progress.interpolate({
    inputRange: [0, 0.1, 0.8, 1],
    outputRange: [0, 1, 1, 0],
  });

  const translateX = sway.interpolate({
    inputRange: [-1, 1],
    outputRange: [-20, 20],
  });

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

export default function LoginPage({ navigation }) {
  const scrollY = useRef(new Animated.Value(0)).current;

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(true);
  const [isPasswordVisible, setIsPasswordVisible] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const bubblePool = useMemo(() => Array.from({ length: 6 }).map((_, i) => i), []);

  const isFormValid = useMemo(() => email.trim().length > 0 && password.length > 0, [email, password]);

  const handleLogin = async () => {
    const normalizedEmail = email.trim().toLowerCase();
    if (!normalizedEmail || !password) return;

    try {
      setIsSubmitting(true);
      const response = await fetch(`${API_BASE_URL}/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: normalizedEmail, password }),
      });

      const payload = await response.json();

      if (!response.ok) {
        const apiMessage = String(payload?.message || '').trim();
        const normalizedApiMessage = apiMessage.toLowerCase();
        const translatedMessage = (
          normalizedApiMessage === 'invalid email or password.'
          || normalizedApiMessage === 'invalid email or password'
        )
          ? 'אימייל או סיסמה שגויים.'
          : (apiMessage || 'אימייל או סיסמה שגויים.');

        Alert.alert('פרטי התחברות שגויים', translatedMessage);
        return;
      }

      const role = (payload?.role ?? '').toLowerCase();

      if (role === 'manager') {
        const managerToken = String(payload?.token || '').trim();
        if (!managerToken) {
          Alert.alert('שגיאת התחברות', 'לא התקבל אסימון התחברות למנהל. נסו שוב.');
          return;
        }

        navigation.replace('ManagerHomepage', {
          managerAuth: {
            email: normalizedEmail,
            token: managerToken,
            tokenExpiresAtUtc: payload?.tokenExpiresAtUtc || null,
          },
        });
        return;
      }
      if (role === 'instructor') {
        navigation.replace('InstructorHomepage', { authUser: payload });
        return;
      }
      if (role === 'parent') {
        navigation.replace('ParentHomepage', { authUser: payload });
        return;
      }
      Alert.alert('שגיאה', 'לא זוהתה הרשאת משתמש מתאימה.');
    } catch (error) {
      const apiDebugLine = __DEV__ ? `\nכתובת API נוכחית: ${API_BASE_URL}` : '';
      Alert.alert(
        'שגיאת שרת',
        `לא ניתן להתחבר לשרת כרגע. ודאו שה-ServerSide רץ וכתובת ה-API מתאימה לסביבה שלכם.${apiDebugLine}`
      );
    } finally {
      setIsSubmitting(false);
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
        <LinearGradient colors={["#00aed8", "#007fa7", "#004d73"]} style={StyleSheet.absoluteFill} />
      </View>

      {/* INFINITE BUBBLE POOL */}
      {bubblePool.map((id) => (
        <Bubble key={id} />
      ))}

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
                  textAlign="left"
                  secureTextEntry={!isPasswordVisible}
                  value={password}
                  onChangeText={setPassword}
                />
                <TouchableOpacity style={styles.eyeIconButton} onPress={() => setIsPasswordVisible(!isPasswordVisible)}>
                  <Text style={styles.eyeIcon}>{isPasswordVisible ? '🙈' : '👁️'}</Text>
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
              <TouchableOpacity activeOpacity={0.8}>
                <Text style={styles.forgotText}>שכחת סיסמה?</Text>
              </TouchableOpacity>
            </View>

            <PrimaryButton
              label={isSubmitting ? 'מתחבר...' : 'התחברות'}
              style={styles.loginBtn}
              gradientStyle={styles.btnGradient}
              textStyle={styles.primaryBtnText}
              onPress={handleLogin}
              disabled={!isFormValid || isSubmitting}
              colorsOverride={
                (!isFormValid || isSubmitting) ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)'] : null
              }
            />
          </AppCard>
        </Animated.ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  keyboardWrap: { flex: 1 },
  scrollContent: {
    flexGrow: 1,
    justifyContent: 'center',
    paddingTop: 100,
    paddingBottom: 60,
    paddingHorizontal: "5%",
  },
  waveContainer: {
    position: "absolute",
    bottom: 0,
    left: 0,
    width: 2400,
    height: 150,
    zIndex: 0,
  },
  bubbleWrapper: { position: "absolute", zIndex: 1 },

  // --- NAVBAR ---
  navbar: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    paddingTop: Platform.OS === "ios" ? 50 : (StatusBar.currentHeight || 20) + 15,
    paddingBottom: 15,
    paddingHorizontal: 20,
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
    paddingHorizontal: 20,
    paddingRight: 56,
    backgroundColor: "transparent",
  },
  eyeIconButton: {
    position: "absolute",
    right: 0,
    top: 0,
    bottom: 0,
    width: 50,
    borderLeftWidth: 1,
    borderLeftColor: "rgba(255, 255, 255, 0.15)",
    backgroundColor: "rgba(255, 255, 255, 0.08)",
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
});

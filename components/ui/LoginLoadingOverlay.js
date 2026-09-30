import React, { useEffect, useRef, useState } from 'react';
import {
  View, Text, StyleSheet, Animated, Easing, Dimensions,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import Svg, { Circle, Path, G } from 'react-native-svg';

const { width: SCREEN_W } = Dimensions.get('window');
const CIRCLE_SIZE = 110;
const STROKE_W = 3.5;
const R = (CIRCLE_SIZE - STROKE_W * 2) / 2;
const CIRCUMFERENCE = 2 * Math.PI * R;

// Checkmark path total length (approximate)
const CHECK_PATH = 'M30 56 L46 72 L74 38';
const CHECK_LEN = 68;

// X paths total length (approximate)
const X_PATH_1 = 'M35 35 L69 69';
const X_PATH_2 = 'M69 35 L35 69';
const X_LINE_LEN = 48;

/**
 * LoginLoadingOverlay — butter-smooth animated login feedback.
 *
 * Props:
 *   visible  – boolean
 *   status   – 'loading' | 'success' | 'error'
 *   message  – string
 */
export default function LoginLoadingOverlay({ visible, status = 'loading', message = '', onDismiss }) {
  /* ── core animated values ── */
  const overlayOpacity   = useRef(new Animated.Value(0)).current;
  const spinAngle        = useRef(new Animated.Value(0)).current;
  const arcSweep         = useRef(new Animated.Value(0.28)).current;     // portion of circle the arc covers
  const spinnerScale     = useRef(new Animated.Value(1)).current;
  const spinnerOpacity   = useRef(new Animated.Value(1)).current;

  const ringDraw         = useRef(new Animated.Value(0)).current;       // 0→1 draws the result ring
  const iconDraw         = useRef(new Animated.Value(0)).current;       // 0→1 draws check / X
  const resultScale      = useRef(new Animated.Value(0.6)).current;
  const resultOpacity    = useRef(new Animated.Value(0)).current;

  const glowScale        = useRef(new Animated.Value(0.5)).current;
  const glowOpacity      = useRef(new Animated.Value(0)).current;

  const msgOpacity       = useRef(new Animated.Value(0)).current;
  const msgTranslateY    = useRef(new Animated.Value(12)).current;

  const dot1             = useRef(new Animated.Value(0)).current;
  const dot2             = useRef(new Animated.Value(0)).current;
  const dot3             = useRef(new Animated.Value(0)).current;

  // SVG-driven state (updated via listeners since strokeDashoffset can't use native driver)
  const [ringOffset, setRingOffset]   = useState(CIRCUMFERENCE);
  const [checkOffset, setCheckOffset] = useState(CHECK_LEN);
  const [x1Offset, setX1Offset]       = useState(X_LINE_LEN);
  const [x2Offset, setX2Offset]       = useState(X_LINE_LEN);

  // Loop handles
  const spinLoop   = useRef(null);
  const pulseLoop  = useRef(null);
  const dotLoop    = useRef(null);

  /* ── overlay fade in/out ── */
  useEffect(() => {
    Animated.timing(overlayOpacity, {
      toValue: visible ? 1 : 0,
      duration: visible ? 280 : 220,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true,
    }).start();
  }, [visible]);

  /* ── LOADING phase ── */
  useEffect(() => {
    if (!visible || status !== 'loading') return;

    // Reset everything
    spinAngle.setValue(0);
    spinnerScale.setValue(1);
    spinnerOpacity.setValue(1);
    ringDraw.setValue(0);
    iconDraw.setValue(0);
    resultScale.setValue(0.6);
    resultOpacity.setValue(0);
    glowScale.setValue(0.5);
    glowOpacity.setValue(0);
    msgOpacity.setValue(0);
    msgTranslateY.setValue(12);
    setRingOffset(CIRCUMFERENCE);
    setCheckOffset(CHECK_LEN);
    setX1Offset(X_LINE_LEN);
    setX2Offset(X_LINE_LEN);

    // Continuous spin
    spinLoop.current = Animated.loop(
      Animated.timing(spinAngle, {
        toValue: 1,
        duration: 1000,
        easing: Easing.linear,
        useNativeDriver: true,
      }),
    );
    spinLoop.current.start();

    // Gentle pulse
    pulseLoop.current = Animated.loop(
      Animated.sequence([
        Animated.timing(spinnerScale, {
          toValue: 1.06, duration: 700, easing: Easing.inOut(Easing.sin), useNativeDriver: true,
        }),
        Animated.timing(spinnerScale, {
          toValue: 1, duration: 700, easing: Easing.inOut(Easing.sin), useNativeDriver: true,
        }),
      ]),
    );
    pulseLoop.current.start();

    // Loading dots wave
    dotLoop.current = Animated.loop(
      Animated.stagger(160, [
        Animated.sequence([
          Animated.timing(dot1, { toValue: 1, duration: 350, easing: Easing.out(Easing.sin), useNativeDriver: true }),
          Animated.timing(dot1, { toValue: 0, duration: 350, easing: Easing.in(Easing.sin), useNativeDriver: true }),
        ]),
        Animated.sequence([
          Animated.timing(dot2, { toValue: 1, duration: 350, easing: Easing.out(Easing.sin), useNativeDriver: true }),
          Animated.timing(dot2, { toValue: 0, duration: 350, easing: Easing.in(Easing.sin), useNativeDriver: true }),
        ]),
        Animated.sequence([
          Animated.timing(dot3, { toValue: 1, duration: 350, easing: Easing.out(Easing.sin), useNativeDriver: true }),
          Animated.timing(dot3, { toValue: 0, duration: 350, easing: Easing.in(Easing.sin), useNativeDriver: true }),
        ]),
      ]),
    );
    dotLoop.current.start();

    // Fade in message
    Animated.parallel([
      Animated.timing(msgOpacity, { toValue: 1, duration: 500, delay: 200, useNativeDriver: true }),
      Animated.timing(msgTranslateY, { toValue: 0, duration: 500, delay: 200, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
    ]).start();

    return () => {
      spinLoop.current?.stop();
      pulseLoop.current?.stop();
      dotLoop.current?.stop();
    };
  }, [visible, status]);

  /* ── SUCCESS / ERROR transition ── */
  useEffect(() => {
    if (status !== 'success' && status !== 'error') return;

    // Stop all loops
    spinLoop.current?.stop();
    pulseLoop.current?.stop();
    dotLoop.current?.stop();

    // Attach listeners for SVG-driven values
    const ringId = ringDraw.addListener(({ value }) => {
      setRingOffset(CIRCUMFERENCE * (1 - value));
    });

    const isSuccess = status === 'success';

    if (isSuccess) {
      const checkId = iconDraw.addListener(({ value }) => {
        setCheckOffset(CHECK_LEN * (1 - value));
      });

      runResultAnimation(checkId, ringId);
    } else {
      // For X we draw both lines sequentially
      const iconId = iconDraw.addListener(({ value }) => {
        // First half draws line 1, second half draws line 2
        if (value <= 0.5) {
          setX1Offset(X_LINE_LEN * (1 - value * 2));
          setX2Offset(X_LINE_LEN);
        } else {
          setX1Offset(0);
          setX2Offset(X_LINE_LEN * (1 - (value - 0.5) * 2));
        }
      });

      runResultAnimation(iconId, ringId);
    }

    function runResultAnimation(iconListenerId, ringListenerId) {
      // Phase 1: Shrink & fade spinner (200ms)
      Animated.parallel([
        Animated.timing(spinnerOpacity, {
          toValue: 0, duration: 200, easing: Easing.out(Easing.cubic), useNativeDriver: true,
        }),
        Animated.timing(spinnerScale, {
          toValue: 0.4, duration: 200, easing: Easing.in(Easing.cubic), useNativeDriver: true,
        }),
      ]).start(() => {
        // Phase 2: Spring in result + draw ring + draw icon
        Animated.parallel([
          // Scale spring
          Animated.spring(resultScale, {
            toValue: 1,
            friction: 6,
            tension: 90,
            useNativeDriver: true,
          }),
          // Fade in result
          Animated.timing(resultOpacity, {
            toValue: 1, duration: 200, useNativeDriver: true,
          }),
          // Draw ring (0→1)
          Animated.timing(ringDraw, {
            toValue: 1,
            duration: 600,
            easing: Easing.out(Easing.cubic),
            useNativeDriver: false,
          }),
          // Draw icon with slight delay
          Animated.sequence([
            Animated.delay(250),
            Animated.timing(iconDraw, {
              toValue: 1,
              duration: 450,
              easing: Easing.out(Easing.cubic),
              useNativeDriver: false,
            }),
          ]),
          // Glow
          Animated.parallel([
            Animated.timing(glowOpacity, {
              toValue: 1, duration: 500, useNativeDriver: true,
            }),
            Animated.spring(glowScale, {
              toValue: 1, friction: 7, tension: 50, useNativeDriver: true,
            }),
          ]),
          // Message
          Animated.parallel([
            Animated.timing(msgOpacity, {
              toValue: 1, duration: 350, delay: 300, useNativeDriver: true,
            }),
            Animated.timing(msgTranslateY, {
              toValue: 0, duration: 350, delay: 300, easing: Easing.out(Easing.cubic), useNativeDriver: true,
            }),
          ]),
        ]).start(() => {
          ringDraw.removeListener(ringListenerId);
          iconDraw.removeListener(iconListenerId);

          // For errors: hold briefly, then slowly fade entire overlay out
          if (status === 'error' && onDismiss) {
            setTimeout(() => {
              Animated.timing(overlayOpacity, {
                toValue: 0,
                duration: 800,
                easing: Easing.inOut(Easing.cubic),
                useNativeDriver: true,
              }).start(() => {
                onDismiss();
              });
            }, 1200);
          }
        });
      });
    }

    return () => {
      ringDraw.removeAllListeners();
      iconDraw.removeAllListeners();
    };
  }, [status]);

  if (!visible) return null;

  const spin = spinAngle.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] });

  const isSuccess = status === 'success';
  const isError   = status === 'error';
  const isResult  = isSuccess || isError;
  const accentClr = isSuccess ? '#22c55e' : (isError ? '#ef4444' : '#00d4ff');

  const dotScale = (v) => v.interpolate({ inputRange: [0, 1], outputRange: [0.5, 1.15] });
  const dotOp    = (v) => v.interpolate({ inputRange: [0, 1], outputRange: [0.25, 1] });
  const dotY     = (v) => v.interpolate({ inputRange: [0, 0.5, 1], outputRange: [0, -6, 0] });

  return (
    <Animated.View style={[styles.overlay, { opacity: overlayOpacity }]} pointerEvents={visible ? 'auto' : 'none'}>
      <LinearGradient
        colors={['rgba(0, 12, 35, 0.94)', 'rgba(0, 25, 55, 0.90)', 'rgba(0, 12, 35, 0.94)']}
        style={StyleSheet.absoluteFill}
      />

      {/* ── Ambient glow ── */}
      <Animated.View style={[styles.glowOuter, {
        opacity: isResult ? glowOpacity : 0.25,
        transform: [{ scale: isResult ? glowScale : spinnerScale }],
      }]}>
        <View style={[styles.glowDisc, {
          backgroundColor: isResult
            ? (isSuccess ? 'rgba(34,197,94,0.08)' : 'rgba(239,68,68,0.08)')
            : 'rgba(0,180,255,0.06)',
          shadowColor: accentClr,
        }]} />
      </Animated.View>

      {/* ── Outer ring glow ── */}
      <Animated.View style={[styles.ringGlow, {
        opacity: isResult
          ? glowOpacity.interpolate({ inputRange: [0, 1], outputRange: [0, 0.45] })
          : 0.15,
        transform: [{ scale: isResult ? glowScale : spinnerScale }],
        borderColor: isResult
          ? (isSuccess ? 'rgba(34,197,94,0.25)' : 'rgba(239,68,68,0.25)')
          : 'rgba(0,212,255,0.2)',
      }]} />

      {/* ── SPINNER (loading phase) ── */}
      <Animated.View style={[styles.iconArea, {
        opacity: spinnerOpacity,
        transform: [{ rotate: spin }, { scale: spinnerScale }],
      }]}>
        <Svg width={CIRCLE_SIZE} height={CIRCLE_SIZE} viewBox={`0 0 ${CIRCLE_SIZE} ${CIRCLE_SIZE}`}>
          {/* Background track */}
          <Circle
            cx={CIRCLE_SIZE / 2} cy={CIRCLE_SIZE / 2} r={R}
            stroke="rgba(0,180,255,0.10)"
            strokeWidth={STROKE_W}
            fill="none"
          />
          {/* Spinning arc */}
          <Circle
            cx={CIRCLE_SIZE / 2} cy={CIRCLE_SIZE / 2} r={R}
            stroke="#00d4ff"
            strokeWidth={STROKE_W}
            fill="none"
            strokeLinecap="round"
            strokeDasharray={`${CIRCUMFERENCE * 0.28} ${CIRCUMFERENCE * 0.72}`}
            transform={`rotate(-90 ${CIRCLE_SIZE / 2} ${CIRCLE_SIZE / 2})`}
          />
        </Svg>
      </Animated.View>

      {/* ── RESULT (success / error) ── */}
      {isResult && (
        <Animated.View style={[styles.iconArea, {
          opacity: resultOpacity,
          transform: [{ scale: resultScale }],
        }]}>
          <Svg width={CIRCLE_SIZE} height={CIRCLE_SIZE} viewBox="0 0 104 104">
            {/* Drawn ring */}
            <Circle
              cx="52" cy="52" r="46"
              stroke={accentClr}
              strokeWidth={STROKE_W}
              fill="none"
              strokeLinecap="round"
              strokeDasharray={CIRCUMFERENCE}
              strokeDashoffset={ringOffset}
              transform="rotate(-90 52 52)"
            />
            {/* Icon paths */}
            {isSuccess ? (
              <Path
                d={CHECK_PATH}
                stroke="#22c55e"
                strokeWidth={4.5}
                strokeLinecap="round"
                strokeLinejoin="round"
                fill="none"
                strokeDasharray={CHECK_LEN}
                strokeDashoffset={checkOffset}
              />
            ) : (
              <G>
                <Path
                  d={X_PATH_1}
                  stroke="#ef4444"
                  strokeWidth={4.5}
                  strokeLinecap="round"
                  fill="none"
                  strokeDasharray={X_LINE_LEN}
                  strokeDashoffset={x1Offset}
                />
                <Path
                  d={X_PATH_2}
                  stroke="#ef4444"
                  strokeWidth={4.5}
                  strokeLinecap="round"
                  fill="none"
                  strokeDasharray={X_LINE_LEN}
                  strokeDashoffset={x2Offset}
                />
              </G>
            )}
          </Svg>
        </Animated.View>
      )}

      {/* ── LOADING DOTS ── */}
      {status === 'loading' && (
        <Animated.View style={[styles.dotsRow, { opacity: msgOpacity }]}>
          {[dot1, dot2, dot3].map((d, i) => (
            <Animated.View key={i} style={[styles.dot, {
              transform: [{ scale: dotScale(d) }, { translateY: dotY(d) }],
              opacity: dotOp(d),
            }]} />
          ))}
        </Animated.View>
      )}

      {/* ── MESSAGE TEXT ── */}
      <Animated.Text style={[
        styles.message,
        {
          opacity: msgOpacity,
          transform: [{ translateY: msgTranslateY }],
          color: isError ? '#fca5a5' : (isSuccess ? '#86efac' : 'rgba(255,255,255,0.85)'),
        },
      ]}>
        {message}
      </Animated.Text>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  overlay: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 9999,
    justifyContent: 'center',
    alignItems: 'center',
  },
  glowOuter: {
    position: 'absolute',
    width: 220,
    height: 220,
    justifyContent: 'center',
    alignItems: 'center',
  },
  glowDisc: {
    width: 200,
    height: 200,
    borderRadius: 100,
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.9,
    shadowRadius: 60,
    elevation: 25,
  },
  ringGlow: {
    position: 'absolute',
    width: 150,
    height: 150,
    borderRadius: 75,
    borderWidth: 1.5,
  },
  iconArea: {
    width: CIRCLE_SIZE,
    height: CIRCLE_SIZE,
    justifyContent: 'center',
    alignItems: 'center',
    position: 'absolute',
  },
  dotsRow: {
    flexDirection: 'row',
    marginTop: CIRCLE_SIZE / 2 + 30,
    gap: 10,
  },
  dot: {
    width: 7,
    height: 7,
    borderRadius: 3.5,
    backgroundColor: '#00d4ff',
  },
  message: {
    position: 'absolute',
    bottom: '32%',
    fontSize: 16,
    fontWeight: '600',
    textAlign: 'center',
    letterSpacing: 0.4,
    maxWidth: SCREEN_W * 0.78,
  },
});

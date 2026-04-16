import React, { useEffect, useMemo, useRef } from 'react';
import { Animated, Dimensions, Easing, StyleSheet, View } from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { colors } from '../../theme/tokens';

const { width, height } = Dimensions.get('window');

const sharedVariant = {
  baseGradient: ['#005a80', '#00456a', colors.bgDeep],
  overlayGradient: ['#00aed8', '#007fa7', '#004d73'],
  bubble: 'rgba(255, 255, 255, 0.18)',
  waveBack: 'rgba(0, 153, 204, 0.3)',
  waveFront: 'rgba(0, 212, 255, 0.25)',
};

const variants = {
  manager: sharedVariant,
  instructor: sharedVariant,
  parent: sharedVariant,
  auth: sharedVariant,
};

function Bubble({ color, delay, left, size, duration }) {
  const progress = useRef(new Animated.Value(0)).current;
  const sway = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    const floatAnim = Animated.loop(
      Animated.sequence([
        Animated.delay(delay),
        Animated.timing(progress, {
          toValue: 1,
          duration,
          easing: Easing.linear,
          useNativeDriver: true,
        }),
        Animated.timing(progress, {
          toValue: 0,
          duration: 0,
          useNativeDriver: true,
        }),
      ]),
    );

    const swayAnim = Animated.loop(
      Animated.sequence([
        Animated.timing(sway, { toValue: 1, duration: 1800, useNativeDriver: true }),
        Animated.timing(sway, { toValue: -1, duration: 2600, useNativeDriver: true }),
        Animated.timing(sway, { toValue: 0, duration: 1800, useNativeDriver: true }),
      ]),
    );

    floatAnim.start();
    swayAnim.start();

    return () => {
      progress.stopAnimation();
      sway.stopAnimation();
    };
  }, [delay, duration, progress, sway]);

  const translateY = progress.interpolate({
    inputRange: [0, 1],
    outputRange: [height + 50, -120],
  });

  const opacity = progress.interpolate({
    inputRange: [0, 0.12, 0.85, 1],
    outputRange: [0, 0.9, 0.7, 0],
  });

  const translateX = sway.interpolate({
    inputRange: [-1, 1],
    outputRange: [-14, 14],
  });

  return (
    <Animated.View
      pointerEvents="none"
      style={[
        styles.bubble,
        {
          left,
          width: size,
          height: size,
          borderRadius: size / 2,
          backgroundColor: color,
          transform: [{ translateY }, { translateX }],
          opacity,
        },
      ]}
    />
  );
}

export default function AquaticBackground({ variant = 'manager', showWaves = true }) {
  const theme = variants[variant] || variants.manager;
  const bubbles = useMemo(
    () =>
      Array.from({ length: 8 }).map((_, index) => ({
        id: `bubble-${index}`,
        left: Math.random() * width,
        size: 10 + Math.random() * 22,
        delay: Math.floor(Math.random() * 12000),
        duration: 12000 + Math.floor(Math.random() * 8000),
      })),
    [],
  );

  return (
    <>
      <LinearGradient colors={theme.baseGradient} style={StyleSheet.absoluteFill} />

      <View style={StyleSheet.absoluteFill}>
        <LinearGradient colors={theme.overlayGradient} style={StyleSheet.absoluteFill} />
      </View>

      {showWaves ? (
        <>
          <View pointerEvents="none" style={[styles.waveBack, { backgroundColor: theme.waveBack }]} />
          <View pointerEvents="none" style={[styles.waveFront, { backgroundColor: theme.waveFront }]} />
        </>
      ) : null}

      {bubbles.map((bubble) => (
        <Bubble
          key={bubble.id}
          color={theme.bubble}
          left={bubble.left}
          size={bubble.size}
          delay={bubble.delay}
          duration={bubble.duration}
        />
      ))}
    </>
  );
}

const styles = StyleSheet.create({
  waveBack: {
    position: 'absolute',
    top: 84,
    left: -28,
    right: -28,
    height: 92,
    borderRadius: 90,
  },
  waveFront: {
    position: 'absolute',
    top: 102,
    left: -18,
    right: -18,
    height: 82,
    borderRadius: 90,
  },
  bubble: {
    position: 'absolute',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.35)',
  },
});

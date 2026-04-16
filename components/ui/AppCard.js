import React from 'react';
import { View, StyleSheet } from 'react-native';
import { BlurView } from 'expo-blur';
import { radius } from '../../theme/tokens';

export default function AppCard({
  children,
  style,
  useBlur = false,
  blurIntensity = 30,
  blurTint = 'dark',
}) {
  if (useBlur) {
    return (
      <BlurView intensity={blurIntensity} tint={blurTint} style={[styles.card, style]}>
        {children}
      </BlurView>
    );
  }

  return <View style={[styles.card, style]}>{children}</View>;
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.lg,
    overflow: 'hidden',
  },
});
